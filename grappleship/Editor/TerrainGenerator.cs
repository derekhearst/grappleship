using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace GrappleShip.EditorTools;

/// <summary>
/// Procedurally fills a Sandbox.Terrain's TerrainStorage with multi-octave
/// value noise. Mirrors the TS-side generator in
/// tools/scripts/generate-dune-heightmap.ts so the same seed produces the
/// same dunes either way. Operates directly on the engine's UInt16[]
/// heightfield — no PNG / .vtex round-trip required.
/// </summary>
public static class TerrainGenerator
{
	public static object Generate( JsonElement req )
	{
		var compGuid = req.TryGetProperty( "component_guid", out var g ) ? g.GetString() : null;
		var resolution = req.TryGetProperty( "resolution", out var rEl ) ? rEl.GetInt32() : 512;
		var seed = req.TryGetProperty( "seed", out var sEl ) ? sEl.GetInt32() : 42;
		var minOut = req.TryGetProperty( "min_norm", out var mn ) ? mn.GetSingle() : 0.30f;
		var maxOut = req.TryGetProperty( "max_norm", out var mx ) ? mx.GetSingle() : 1.90f;
		var terrainSize = req.TryGetProperty( "terrain_size", out var ts ) ? ts.GetSingle() : 2000f;
		var terrainHeight = req.TryGetProperty( "terrain_height", out var th ) ? th.GetSingle() : 200f;
		var assetPath = req.TryGetProperty( "asset_path", out var ap ) ? ap.GetString() : "maps/dunes.terrain";

		if ( string.IsNullOrEmpty( compGuid ) )
			throw new System.InvalidOperationException( "missing component_guid" );
		if ( resolution < 32 || resolution > 4096 )
			throw new System.InvalidOperationException( "resolution must be in [32, 4096]" );

		var terrain = FindTerrainByGuid( System.Guid.Parse( compGuid ) );
		if ( terrain == null )
			throw new System.InvalidOperationException( $"Terrain component not found by guid {compGuid}" );

		// Initialize Storage if it hasn't been created yet. Try Terrain.Create()
		// first (matches the editor's "Create New" button), fall back to a
		// plain `new TerrainStorage()` construction.
		if ( terrain.Storage == null )
		{
			try
			{
				var createMethod = terrain.GetType().GetMethod( "Create", BindingFlags.Public | BindingFlags.Instance );
				createMethod?.Invoke( terrain, null );
			}
			catch ( System.Exception ex )
			{
				Log.Warning( $"[TerrainGenerator] Terrain.Create() threw: {ex.GetBaseException().Message}" );
			}
		}
		if ( terrain.Storage == null )
		{
			var storageType = FindType( "Sandbox.TerrainStorage" );
			if ( storageType != null )
			{
				var inst = System.Activator.CreateInstance( storageType );
				var prop = terrain.GetType().GetProperty( "Storage", BindingFlags.Public | BindingFlags.Instance );
				prop?.SetValue( terrain, inst );
			}
		}
		var storage = terrain.Storage;
		if ( storage == null )
			throw new System.InvalidOperationException( "Failed to initialize Terrain.Storage (tried Terrain.Create() and `new TerrainStorage()`)" );

		// Resize the heightfield. SetResolution allocates HeightMap[] of size N×N.
		storage.SetResolution( resolution );

		// Set the world-scale dimensions on both storage and the Terrain
		// component so the renderer + collision agree with the inspector.
		storage.TerrainSize = terrainSize;
		storage.TerrainHeight = terrainHeight;
		try
		{
			var sizeProp = terrain.GetType().GetProperty( "TerrainSize", BindingFlags.Public | BindingFlags.Instance );
			sizeProp?.SetValue( terrain, terrainSize );
			var hProp = terrain.GetType().GetProperty( "TerrainHeight", BindingFlags.Public | BindingFlags.Instance );
			hProp?.SetValue( terrain, terrainHeight );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[TerrainGenerator] couldn't set Terrain size on component: {ex.Message}" );
		}

		var heights = storage.HeightMap;
		if ( heights == null || heights.Length != resolution * resolution )
			throw new System.InvalidOperationException(
				$"Storage.HeightMap is wrong size after SetResolution({resolution}): got {(heights == null ? -1 : heights.Length)}" );

		var range = maxOut - minOut;
		if ( range <= 0 ) range = 1;

		for ( int y = 0; y < resolution; y++ )
		{
			for ( int x = 0; x < resolution; x++ )
			{
				float fx = (float)x / resolution * 6f;
				float fy = (float)y / resolution * 6f;

				float big = ValueNoise( fx, fy, seed );
				float mid = ValueNoise( fx * 2.3f, fy * 2.3f, seed + 1 ) * 0.45f;
				float small = ValueNoise( fx * 5.7f, fy * 5.7f, seed + 2 ) * 0.18f;
				float stretched = ValueNoise( fx * 0.6f, fy * 2.8f, seed + 3 ) * 0.55f;
				float h = big + mid + small + stretched;

				// Normalize using the expected raw range, then quantize to 16 bits.
				float norm = (h - minOut) / range;
				if ( norm < 0 ) norm = 0;
				else if ( norm > 1 ) norm = 1;
				heights[y * resolution + x] = (ushort)(norm * 65535f);
			}
		}

		// Persist as a real .terrain asset on disk so the scene can reference
		// it by path (otherwise the in-memory Storage is lost on scene reload).
		var savedPath = PersistAsAsset( storage, assetPath );

		try
		{
			terrain.SyncGPUTexture();
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[TerrainGenerator] SyncGPUTexture warning: {ex.Message}" );
		}

		return new
		{
			ok = true,
			resolution,
			cells = heights.Length,
			storage_size = storage.TerrainSize,
			storage_height = storage.TerrainHeight,
			asset_path = savedPath,
		};
	}

	/// <summary>
	/// Save the TerrainStorage to disk as a .terrain asset and link it to the
	/// terrain via Asset.LoadResource(). Required because TerrainStorage is a
	/// GameResource (asset-backed) — in-memory assignments are dropped on
	/// scene reload.
	/// </summary>
	static string PersistAsAsset( Sandbox.TerrainStorage storage, string relPath )
	{
		var assetSystem = FindType( "Editor.AssetSystem" );
		if ( assetSystem == null )
		{
			Log.Warning( "[TerrainGenerator] Editor.AssetSystem not found — cannot persist Storage" );
			return null;
		}
		var contentRoot = Editor.FileSystem.Content.GetFullPath( "/" );
		var fullPath = System.IO.Path.Combine( contentRoot, relPath );
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( fullPath ) );
		if ( !System.IO.File.Exists( fullPath ) )
		{
			System.IO.File.WriteAllText( fullPath, "{}" );
		}
		// FindByPath first (content-relative); if missing, RegisterFile expects
		// the absolute disk path.
		var findMethod = assetSystem.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var asset = findMethod?.Invoke( null, new object[] { relPath } );
		if ( asset == null )
		{
			var registerMethod = assetSystem.GetMethod( "RegisterFile", BindingFlags.Public | BindingFlags.Static );
			asset = registerMethod?.Invoke( null, new object[] { fullPath } );
		}
		if ( asset == null )
		{
			Log.Warning( $"[TerrainGenerator] could not register asset at {relPath}" );
			return null;
		}
		// Save the storage into the asset.
		var saveMethod = asset.GetType().GetMethod( "SaveToDisk", BindingFlags.Public | BindingFlags.Instance );
		if ( saveMethod != null )
		{
			try
			{
				var ok = (bool)saveMethod.Invoke( asset, new object[] { storage } );
				if ( !ok ) Log.Warning( "[TerrainGenerator] Asset.SaveToDisk returned false" );
			}
			catch ( System.Exception ex )
			{
				Log.Warning( $"[TerrainGenerator] Asset.SaveToDisk threw: {ex.GetBaseException().Message}" );
			}
		}
		return relPath;
	}

	/// <summary>
	/// Add a TerrainMaterial (a .tmat asset) to the terrain's Materials list,
	/// re-save Storage to disk, and trigger rebuild. Without at least one
	/// TerrainMaterial in the list, the terrain renders unlit/black.
	/// </summary>
	public static object AddTerrainMaterial( JsonElement req )
	{
		var compGuid = req.TryGetProperty( "component_guid", out var g ) ? g.GetString() : null;
		var tmatPath = req.TryGetProperty( "tmat_path", out var p ) ? p.GetString() : null;
		var replace = req.TryGetProperty( "replace", out var rp ) && rp.GetBoolean();
		if ( string.IsNullOrEmpty( compGuid ) ) throw new System.InvalidOperationException( "missing component_guid" );
		if ( string.IsNullOrEmpty( tmatPath ) ) throw new System.InvalidOperationException( "missing tmat_path" );

		var terrain = FindTerrainByGuid( System.Guid.Parse( compGuid ) );
		if ( terrain == null ) throw new System.InvalidOperationException( "Terrain not found" );
		if ( terrain.Storage == null ) throw new System.InvalidOperationException( "Terrain.Storage is null — generate the heightmap first" );

		// Load the TerrainMaterial via the asset system.
		var assetSystem = FindType( "Editor.AssetSystem" );
		if ( assetSystem == null ) throw new System.InvalidOperationException( "AssetSystem not found" );
		var findByPath = assetSystem.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var asset = findByPath?.Invoke( null, new object[] { tmatPath } );
		if ( asset == null ) throw new System.InvalidOperationException( $"asset not found: {tmatPath}" );

		// CRITICAL: force a fresh compile *immediately* before LoadResource.
		// Without this, the cached Sandbox.TerrainMaterial returned by
		// LoadResource() may have null BCRTexture/NHOTexture handles — the
		// terrain shader then samples nothing and renders engine-magenta even
		// though the .tmat_c on disk is fine. Compiling refreshes the engine's
		// GPU-side texture handles so the next LoadResource picks them up.
		try
		{
			var compileMethod = asset.GetType().GetMethod( "Compile", BindingFlags.Public | BindingFlags.Instance );
			compileMethod?.Invoke( asset, new object[] { true } );
		}
		catch ( System.Exception ex ) { Log.Warning( $"[AddTerrainMaterial] pre-add Compile threw: {ex.GetBaseException().Message}" ); }

		var loadResource = asset.GetType().GetMethods( BindingFlags.Public | BindingFlags.Instance )
			.FirstOrDefault( m => m.Name == "LoadResource" && m.GetParameters().Length == 0 && !m.IsGenericMethodDefinition );
		var material = loadResource?.Invoke( asset, null );
		if ( material == null ) throw new System.InvalidOperationException( $"could not load material from {tmatPath}" );
		if ( material.GetType().Name != "TerrainMaterial" )
			throw new System.InvalidOperationException( $"asset is {material.GetType().Name}, expected TerrainMaterial" );

		// Add to Storage.Materials list.
		var listProp = terrain.Storage.GetType().GetProperty( "Materials" );
		var list = listProp?.GetValue( terrain.Storage );
		if ( list == null ) throw new System.InvalidOperationException( "Storage.Materials is null" );
		if ( replace )
		{
			var clearMethod = list.GetType().GetMethod( "Clear" );
			clearMethod?.Invoke( list, null );
		}
		var addMethod = list.GetType().GetMethod( "Add" );
		addMethod?.Invoke( list, new[] { material } );

		// Persist to disk via FindByPath (content-relative) to avoid leading-slash bugs.
		var findByPathStorage = assetSystem.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var storageAsset = findByPathStorage?.Invoke( null, new object[] { "maps/dunes.terrain" } );
		if ( storageAsset != null )
		{
			var saveMethod = storageAsset.GetType().GetMethod( "SaveToDisk", BindingFlags.Public | BindingFlags.Instance );
			saveMethod?.Invoke( storageAsset, new object[] { terrain.Storage } );
		}

		// Rebuild GPU.
		try { terrain.GetType().GetMethod( "Create", BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null )?.Invoke( terrain, null ); } catch { }
		try { terrain.SyncGPUTexture(); } catch { }

		// Count items in the list now.
		var countProp = list.GetType().GetProperty( "Count" );
		var count = countProp != null ? (int)countProp.GetValue( list ) : -1;

		return new
		{
			ok = true,
			tmat_path = tmatPath,
			material_type = material.GetType().FullName,
			materials_count = count,
		};
	}

	/// <summary>
	/// Force the terrain to rebuild its GPU representation from the current
	/// Storage. After scene reload the engine has loaded our .terrain asset
	/// into Storage, but the GPU texture / mesh may not have been pushed.
	/// Tries every rebuild surface we can find via reflection.
	/// </summary>
	public static object RebuildTerrain( JsonElement req )
	{
		var compGuid = req.TryGetProperty( "component_guid", out var g ) ? g.GetString() : null;
		if ( string.IsNullOrEmpty( compGuid ) )
			throw new System.InvalidOperationException( "missing component_guid" );
		var terrain = FindTerrainByGuid( System.Guid.Parse( compGuid ) );
		if ( terrain == null )
			throw new System.InvalidOperationException( "Terrain not found" );

		var report = new System.Collections.Generic.List<string>();
		var hasStorage = terrain.Storage != null;
		report.Add( $"storage: {(hasStorage ? "present" : "null")}" );
		if ( hasStorage )
		{
			report.Add( $"resolution: {terrain.Storage.Resolution}" );
			report.Add( $"heightmap_len: {(terrain.Storage.HeightMap?.Length ?? 0)}" );
		}

		// Try every method that might rebuild/sync.
		foreach ( var name in new[] { "Create", "Rebuild", "Refresh", "RegenerateMesh", "BuildTerrain" } )
		{
			try
			{
				var m = terrain.GetType().GetMethod( name, BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null );
				if ( m != null )
				{
					m.Invoke( terrain, null );
					report.Add( $"called {name}() OK" );
				}
			}
			catch ( System.Exception ex )
			{
				report.Add( $"{name}() threw: {ex.GetBaseException().Message}" );
			}
		}

		// SyncCPUTexture(SyncFlags, RectInt) — pass defaults.
		try
		{
			var syncCpu = terrain.GetType().GetMethod( "SyncCPUTexture", BindingFlags.Public | BindingFlags.Instance );
			if ( syncCpu != null )
			{
				var pars = syncCpu.GetParameters();
				var args = new object[pars.Length];
				for ( int i = 0; i < pars.Length; i++ )
				{
					var pt = pars[i].ParameterType;
					args[i] = pt.IsValueType ? System.Activator.CreateInstance( pt ) : null;
				}
				syncCpu.Invoke( terrain, args );
				report.Add( "called SyncCPUTexture(default args) OK" );
			}
		}
		catch ( System.Exception ex )
		{
			report.Add( $"SyncCPUTexture threw: {ex.GetBaseException().Message}" );
		}

		try
		{
			terrain.SyncGPUTexture();
			report.Add( "called SyncGPUTexture() OK" );
		}
		catch ( System.Exception ex )
		{
			report.Add( $"SyncGPUTexture threw: {ex.GetBaseException().Message}" );
		}

		var hasHmTex = terrain.HeightMap != null;
		report.Add( $"after rebuild — terrain.HeightMap (texture): {(hasHmTex ? "non-null" : "null")}" );
		var hasCmTex = terrain.ControlMap != null;
		report.Add( $"after rebuild — terrain.ControlMap (texture): {(hasCmTex ? "non-null" : "null")}" );

		return new { ok = true, report };
	}

	/// <summary>
	/// Construct a TerrainMaterial in-memory pointing at known-good source
	/// images, save it as a .tmat asset in the project, and return its path.
	/// Use to dodge broken cloud .tmats whose source images didn't ship.
	/// </summary>
	public static object CreateTerrainMaterial( JsonElement req )
	{
		var outPath = req.TryGetProperty( "out_path", out var op ) ? op.GetString() : "maps/dunes_sand.tmat";
		string albedo = req.TryGetProperty( "albedo", out var a ) ? a.GetString() : null;
		string roughness = req.TryGetProperty( "roughness", out var ro ) ? ro.GetString() : "materials/default/default_rough_s1import.tga";
		string normal = req.TryGetProperty( "normal", out var n ) ? n.GetString() : "materials/default/default_normal.tga";
		string ao = req.TryGetProperty( "ao", out var ax ) ? ax.GetString() : "materials/default/default_ao.tga";
		float uvScale = req.TryGetProperty( "uv_scale", out var us ) ? us.GetSingle() : 1.0f;
		bool noTiling = req.TryGetProperty( "no_tiling", out var nt ) ? nt.GetBoolean() : true;

		var tmatType = FindType( "Sandbox.TerrainMaterial" );
		if ( tmatType == null ) throw new System.InvalidOperationException( "TerrainMaterial type not found" );
		var inst = System.Activator.CreateInstance( tmatType );

		void Set( string prop, object value )
		{
			var p = tmatType.GetProperty( prop, BindingFlags.Public | BindingFlags.Instance );
			if ( p == null ) return;
			try { p.SetValue( inst, value ); } catch { }
		}
		if ( !string.IsNullOrEmpty( albedo ) ) Set( "AlbedoImage", albedo );
		Set( "RoughnessImage", roughness );
		Set( "NormalImage", normal );
		Set( "AOImage", ao );
		Set( "UVScale", uvScale );
		Set( "Metalness", 0.0f );
		Set( "NormalStrength", 1.0f );
		Set( "NoTiling", noTiling );

		// Persist via Asset.SaveToDisk pattern.
		var assetSystem = FindType( "Editor.AssetSystem" );
		if ( assetSystem == null ) throw new System.InvalidOperationException( "AssetSystem not found" );
		var contentRoot = Editor.FileSystem.Content.GetFullPath( "/" );
		var fullPath = System.IO.Path.Combine( contentRoot, outPath );
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( fullPath ) );
		if ( !System.IO.File.Exists( fullPath ) ) System.IO.File.WriteAllText( fullPath, "{}" );
		// FindByPath first (content-relative). If it can't find an existing
		// registration, RegisterFile with the absolute disk path (it expects
		// absolute; relative returns null).
		var findMethod = assetSystem.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var asset = findMethod?.Invoke( null, new object[] { outPath } );
		if ( asset == null )
		{
			var registerMethod = assetSystem.GetMethod( "RegisterFile", BindingFlags.Public | BindingFlags.Static );
			asset = registerMethod?.Invoke( null, new object[] { fullPath } );
		}
		if ( asset == null ) throw new System.InvalidOperationException( $"could not register asset at {outPath}" );
		var saveMethod = asset.GetType().GetMethod( "SaveToDisk", BindingFlags.Public | BindingFlags.Instance );
		bool saved = false;
		try { saved = (bool)saveMethod.Invoke( asset, new object[] { inst } ); }
		catch ( System.Exception ex ) { Log.Warning( $"[CreateTerrainMaterial] SaveToDisk threw: {ex.GetBaseException().Message}" ); }

		return new
		{
			ok = true,
			tmat_path = outPath,
			saved,
			albedo,
			roughness,
			normal,
			ao,
		};
	}

	/// <summary>
	/// Set a GameObject's WorldPosition directly on the running editor scene.
	/// Bypasses the MCP scene-file validator (useful when validator is on stale
	/// code) — the editor will persist the new position on next save.
	/// </summary>
	public static object SetGameObjectPosition( JsonElement req )
	{
		var goGuid = req.TryGetProperty( "gameobject_guid", out var g ) ? g.GetString() : null;
		if ( string.IsNullOrEmpty( goGuid ) ) throw new System.InvalidOperationException( "missing gameobject_guid" );
		var x = req.TryGetProperty( "x", out var xe ) ? xe.GetSingle() : 0f;
		var y = req.TryGetProperty( "y", out var ye ) ? ye.GetSingle() : 0f;
		var z = req.TryGetProperty( "z", out var ze ) ? ze.GetSingle() : 0f;

		var targetId = System.Guid.Parse( goGuid );
		var sessionType = FindType( "Editor.SceneEditorSession" );
		var allList = sessionType?.GetProperty( "All", BindingFlags.Public | BindingFlags.Static )?.GetValue( null ) as System.Collections.IEnumerable;
		if ( allList == null ) throw new System.InvalidOperationException( "no editor sessions" );

		foreach ( var session in allList )
		{
			var sceneProp = session.GetType().GetProperty( "Scene", BindingFlags.Public | BindingFlags.Instance );
			if ( sceneProp?.GetValue( session ) is not Sandbox.Scene scene ) continue;
			foreach ( var go in scene.GetAllObjects( true ) )
			{
				if ( go.Id != targetId ) continue;
				go.WorldPosition = new Vector3( x, y, z );
				// Best effort: mark scene dirty so editor persists.
				try
				{
					var dirtyMethod = session.GetType().GetMethod( "MakeDirty", BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null );
					dirtyMethod?.Invoke( session, null );
				}
				catch { }
				return new
				{
					ok = true,
					gameobject_name = go.Name,
					scene_name = scene.Name,
					new_position = $"{x},{y},{z}",
				};
			}
		}
		throw new System.InvalidOperationException( $"GameObject not found: {goGuid}" );
	}

	/// Probe a list of candidate paths to see which resolve to assets the engine can load.
	public static object TestAssetPaths( JsonElement req )
	{
		var paths = new System.Collections.Generic.List<string>();
		if ( req.TryGetProperty( "paths", out var p ) && p.ValueKind == JsonValueKind.Array )
		{
			foreach ( var el in p.EnumerateArray() ) paths.Add( el.GetString() );
		}
		var assetSystem = FindType( "Editor.AssetSystem" );
		var findByPath = assetSystem?.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var results = new System.Collections.Generic.List<object>();
		foreach ( var path in paths )
		{
			var asset = findByPath?.Invoke( null, new object[] { path } );
			results.Add( new
			{
				path,
				found = asset != null,
				name = asset?.GetType().GetProperty( "Name" )?.GetValue( asset )?.ToString(),
				asset_path = asset?.GetType().GetProperty( "Path" )?.GetValue( asset )?.ToString(),
			} );
		}
		return new { ok = true, results };
	}

	/// Set Terrain.MaterialOverride to a Material loaded from a .vmat path.
	public static object SetMaterialOverride( JsonElement req )
	{
		var compGuid = req.TryGetProperty( "component_guid", out var g ) ? g.GetString() : null;
		var vmatPath = req.TryGetProperty( "vmat_path", out var v ) ? v.GetString() : null;
		if ( string.IsNullOrEmpty( compGuid ) ) throw new System.InvalidOperationException( "missing component_guid" );

		var terrain = FindTerrainByGuid( System.Guid.Parse( compGuid ) );
		if ( terrain == null ) throw new System.InvalidOperationException( "Terrain not found" );

		Sandbox.Material material = null;
		if ( !string.IsNullOrEmpty( vmatPath ) )
		{
			material = Sandbox.Material.Load( vmatPath );
			if ( material == null ) throw new System.InvalidOperationException( $"could not load material at {vmatPath}" );
		}
		terrain.MaterialOverride = material;
		try { terrain.SyncGPUTexture(); } catch { }

		return new
		{
			ok = true,
			vmat_path = vmatPath,
			material_loaded = material != null,
		};
	}

	/// <summary>
	/// Paint multi-octave noise into the terrain's ControlMap (the splatmap)
	/// so multiple TerrainMaterial layers blend across the surface, breaking
	/// up the obvious tiling of any single texture.
	///
	/// ControlMap is UInt32[]: byte 0 = layer 0 weight, byte 1 = layer 1, etc.
	/// We write a smooth noise pattern that drifts between layers so adjacent
	/// areas pick different sand variants.
	/// </summary>
	public static object PaintSplatmapNoise( JsonElement req )
	{
		var compGuid = req.TryGetProperty( "component_guid", out var g ) ? g.GetString() : null;
		var seed = req.TryGetProperty( "seed", out var sEl ) ? sEl.GetInt32() : 1234;
		var noiseScale = req.TryGetProperty( "scale", out var ns ) ? ns.GetSingle() : 4.0f;
		if ( string.IsNullOrEmpty( compGuid ) ) throw new System.InvalidOperationException( "missing component_guid" );

		var terrain = FindTerrainByGuid( System.Guid.Parse( compGuid ) );
		if ( terrain == null ) throw new System.InvalidOperationException( "Terrain not found" );
		if ( terrain.Storage == null ) throw new System.InvalidOperationException( "Storage is null" );

		var resolution = terrain.Storage.Resolution;
		var ctrlProp = terrain.Storage.GetType().GetProperty( "ControlMap" );
		var ctrl = ctrlProp?.GetValue( terrain.Storage ) as uint[];
		if ( ctrl == null ) throw new System.InvalidOperationException( "ControlMap is null" );
		if ( ctrl.Length != resolution * resolution )
			throw new System.InvalidOperationException( $"ControlMap length {ctrl.Length} != {resolution * resolution}" );

		// Multi-octave noise → smooth value in [0,1] per cell.
		for ( int y = 0; y < resolution; y++ )
		{
			for ( int x = 0; x < resolution; x++ )
			{
				float fx = (float)x / resolution * noiseScale;
				float fy = (float)y / resolution * noiseScale;
				float n = ValueNoise( fx, fy, seed )
					+ ValueNoise( fx * 2.7f, fy * 2.7f, seed + 1 ) * 0.4f
					+ ValueNoise( fx * 6.3f, fy * 6.3f, seed + 2 ) * 0.15f;
				n /= 1.55f; // back to ~[0,1]
				if ( n < 0 ) n = 0;
				else if ( n > 1 ) n = 1;
				// Soft step to make the layer transition crisper but still smooth.
				float w1 = (float)System.Math.Pow( n, 1.5 );
				byte b0 = (byte)System.Math.Clamp( (int)((1f - w1) * 255f), 0, 255 );
				byte b1 = (byte)System.Math.Clamp( (int)(w1 * 255f), 0, 255 );
				// Pack: layer0=b0, layer1=b1, layer2=0, layer3=0
				ctrl[y * resolution + x] = (uint)b0 | ((uint)b1 << 8);
			}
		}

		// Persist via FindByPath (content-relative) to avoid leading-slash bugs.
		var assetSystem = FindType( "Editor.AssetSystem" );
		if ( assetSystem != null )
		{
			var findByPath = assetSystem.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
			var storageAsset = findByPath?.Invoke( null, new object[] { "maps/dunes.terrain" } );
			if ( storageAsset != null )
			{
				var saveMethod = storageAsset.GetType().GetMethod( "SaveToDisk", BindingFlags.Public | BindingFlags.Instance );
				saveMethod?.Invoke( storageAsset, new object[] { terrain.Storage } );
			}
		}

		try { terrain.SyncGPUTexture(); } catch { }

		return new
		{
			ok = true,
			resolution,
			cells = ctrl.Length,
			noise_scale = noiseScale,
			seed,
		};
	}

	/// Diagnostic: report the byte-distribution of the live ControlMap.
	/// If most cells have all 4 bytes = 0, no layer is selected and the
	/// terrain renders error-magenta.
	public static object InspectSplatmap( JsonElement req )
	{
		var compGuid = req.TryGetProperty( "component_guid", out var g ) ? g.GetString() : null;
		if ( string.IsNullOrEmpty( compGuid ) ) throw new System.InvalidOperationException( "missing component_guid" );
		var terrain = FindTerrainByGuid( System.Guid.Parse( compGuid ) );
		if ( terrain == null || terrain.Storage == null ) throw new System.InvalidOperationException( "terrain or Storage missing" );
		var ctrl = terrain.Storage.GetType().GetProperty( "ControlMap" )?.GetValue( terrain.Storage ) as uint[];
		if ( ctrl == null ) throw new System.InvalidOperationException( "ControlMap null" );

		long sumB0 = 0, sumB1 = 0, sumB2 = 0, sumB3 = 0;
		long allZero = 0, b2OrB3NonZero = 0;
		uint maxB2 = 0, maxB3 = 0;
		// Sample — full scan is fine (1024² = 1M cells).
		for ( int i = 0; i < ctrl.Length; i++ )
		{
			uint v = ctrl[i];
			byte b0 = (byte)(v & 0xff);
			byte b1 = (byte)((v >> 8) & 0xff);
			byte b2 = (byte)((v >> 16) & 0xff);
			byte b3 = (byte)((v >> 24) & 0xff);
			sumB0 += b0; sumB1 += b1; sumB2 += b2; sumB3 += b3;
			if ( b0 == 0 && b1 == 0 && b2 == 0 && b3 == 0 ) allZero++;
			if ( b2 != 0 || b3 != 0 ) b2OrB3NonZero++;
			if ( b2 > maxB2 ) maxB2 = b2;
			if ( b3 > maxB3 ) maxB3 = b3;
		}
		return new
		{
			ok = true,
			cells = ctrl.Length,
			avg_b0 = sumB0 / (double)ctrl.Length,
			avg_b1 = sumB1 / (double)ctrl.Length,
			avg_b2 = sumB2 / (double)ctrl.Length,
			avg_b3 = sumB3 / (double)ctrl.Length,
			cells_all_zero = allZero,
			cells_with_b2_or_b3 = b2OrB3NonZero,
			max_b2 = maxB2,
			max_b3 = maxB3,
			first_5_raw = new[] { ctrl[0], ctrl[1], ctrl[2], ctrl[3], ctrl[4] },
		};
	}

	/// Paint the entire ControlMap with a single layer's full weight. Use
	/// this for diagnosis — if the terrain renders correctly with this,
	/// the splatmap was the issue, not the materials.
	public static object PaintSplatmapSolid( JsonElement req )
	{
		var compGuid = req.TryGetProperty( "component_guid", out var g ) ? g.GetString() : null;
		var layer = req.TryGetProperty( "layer", out var lEl ) ? lEl.GetInt32() : 0;
		if ( string.IsNullOrEmpty( compGuid ) ) throw new System.InvalidOperationException( "missing component_guid" );
		if ( layer < 0 || layer > 3 ) throw new System.InvalidOperationException( "layer must be 0..3" );

		var terrain = FindTerrainByGuid( System.Guid.Parse( compGuid ) );
		if ( terrain == null || terrain.Storage == null ) throw new System.InvalidOperationException( "terrain or Storage missing" );
		var ctrl = terrain.Storage.GetType().GetProperty( "ControlMap" )?.GetValue( terrain.Storage ) as uint[];
		if ( ctrl == null ) throw new System.InvalidOperationException( "ControlMap null" );

		uint v = (uint)255 << (layer * 8);
		for ( int i = 0; i < ctrl.Length; i++ ) ctrl[i] = v;

		var assetSystem = FindType( "Editor.AssetSystem" );
		var findByPath = assetSystem?.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var storageAsset = findByPath?.Invoke( null, new object[] { "maps/dunes.terrain" } );
		if ( storageAsset != null )
		{
			var saveMethod = storageAsset.GetType().GetMethod( "SaveToDisk", BindingFlags.Public | BindingFlags.Instance );
			saveMethod?.Invoke( storageAsset, new object[] { terrain.Storage } );
		}
		try { terrain.SyncGPUTexture(); } catch { }
		return new { ok = true, layer, cells_painted = ctrl.Length, raw_value = v };
	}

	/// Force a recompile on an asset by path. Useful when the cached .generated.* files are stale.
	public static object CompileAsset( JsonElement req )
	{
		var path = req.TryGetProperty( "path", out var p ) ? p.GetString() : null;
		if ( string.IsNullOrEmpty( path ) ) throw new System.InvalidOperationException( "missing 'path'" );
		var assetSystem = FindType( "Editor.AssetSystem" );
		var findByPath = assetSystem?.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var asset = findByPath?.Invoke( null, new object[] { path } );
		if ( asset == null ) throw new System.InvalidOperationException( $"asset not found: {path}" );
		var compileMethod = asset.GetType().GetMethod( "Compile", BindingFlags.Public | BindingFlags.Instance );
		if ( compileMethod == null ) throw new System.InvalidOperationException( "Asset.Compile not found" );
		bool result = false;
		try { result = (bool)compileMethod.Invoke( asset, new object[] { true } ); }
		catch ( System.Exception ex ) { return new { ok = false, error = ex.GetBaseException().Message }; }
		return new { ok = true, path, compiled = result };
	}

	/// Read a texture's GPU pixels and write as PNG to a project-relative path.
	public static object ExtractMaterialTexture( JsonElement req )
	{
		var vmat = req.TryGetProperty( "vmat_path", out var v ) ? v.GetString() : null;
		var texSlot = req.TryGetProperty( "slot", out var s ) ? s.GetString() : "g_tColor";
		var outPath = req.TryGetProperty( "out_path", out var o ) ? o.GetString() : "textures/terrain/extracted.png";
		if ( string.IsNullOrEmpty( vmat ) ) throw new System.InvalidOperationException( "missing vmat_path" );

		var material = Sandbox.Material.Load( vmat );
		if ( material == null ) throw new System.InvalidOperationException( $"could not load {vmat}" );
		var getTexture = material.GetType().GetMethod( "GetTexture", new[] { typeof( string ) } );
		var tex = getTexture?.Invoke( material, new object[] { texSlot } );
		if ( tex == null ) throw new System.InvalidOperationException( $"no texture at slot {texSlot}" );

		var texType = tex.GetType();
		var width = (int)texType.GetProperty( "Width" ).GetValue( tex );
		var height = (int)texType.GetProperty( "Height" ).GetValue( tex );

		// GetPixels(int mip) → Color32[]
		var getPixelsMethod = texType.GetMethod( "GetPixels", new[] { typeof( int ) } );
		if ( getPixelsMethod == null ) throw new System.InvalidOperationException( "GetPixels(int) not found" );
		var pixelsArr = getPixelsMethod.Invoke( tex, new object[] { 0 } ) as System.Collections.IList;
		if ( pixelsArr == null ) throw new System.InvalidOperationException( "GetPixels returned null" );
		var c32Type = pixelsArr.Count > 0 ? pixelsArr[0].GetType() : FindType( "Color32" );
		if ( c32Type == null ) throw new System.InvalidOperationException( "could not find Color32 type" );
		var rField = c32Type.GetField( "r" );
		var gField = c32Type.GetField( "g" );
		var bField = c32Type.GetField( "b" );
		if ( rField == null || gField == null || bField == null ) throw new System.InvalidOperationException( "Color32 missing r/g/b fields" );

		// Optional per-channel multiplier (for creating tinted variants of the same source texture).
		float tintR = req.TryGetProperty( "tint_r", out var tr ) ? tr.GetSingle() : 1.0f;
		float tintG = req.TryGetProperty( "tint_g", out var tg ) ? tg.GetSingle() : 1.0f;
		float tintB = req.TryGetProperty( "tint_b", out var tb ) ? tb.GetSingle() : 1.0f;

		var rgb = new byte[width * height * 3];
		int idx = 0;
		for ( int i = 0; i < pixelsArr.Count && idx < rgb.Length; i++ )
		{
			var px = pixelsArr[i];
			byte r = (byte)rField.GetValue( px );
			byte g = (byte)gField.GetValue( px );
			byte b = (byte)bField.GetValue( px );
			rgb[idx + 0] = (byte)System.Math.Clamp( (int)(r * tintR), 0, 255 );
			rgb[idx + 1] = (byte)System.Math.Clamp( (int)(g * tintG), 0, 255 );
			rgb[idx + 2] = (byte)System.Math.Clamp( (int)(b * tintB), 0, 255 );
			idx += 3;
		}
		var pixelsRead = idx / 3;

		// Encode PNG (RGB).
		var png = EncodePng( rgb, width, height );

		var contentRoot = Editor.FileSystem.Content.GetFullPath( "/" );
		var fullPath = System.IO.Path.Combine( contentRoot, outPath );
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( fullPath ) );
		System.IO.File.WriteAllBytes( fullPath, png );

		return new
		{
			ok = true,
			vmat_path = vmat,
			slot = texSlot,
			out_path = outPath,
			width,
			height,
			pixels_read = pixelsRead,
			png_bytes = png.Length,
		};
	}

	static byte[] EncodePng( byte[] rgb, int width, int height )
	{
		// Minimal PNG encoder (RGB24)
		var sig = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
		var ihdr = new byte[13];
		System.Buffer.BlockCopy( BigEndian( width ), 0, ihdr, 0, 4 );
		System.Buffer.BlockCopy( BigEndian( height ), 0, ihdr, 4, 4 );
		ihdr[8] = 8; // bit depth
		ihdr[9] = 2; // RGB
		var stride = width * 3 + 1;
		var raw = new byte[stride * height];
		for ( int y = 0; y < height; y++ )
		{
			raw[y * stride] = 0;
			System.Buffer.BlockCopy( rgb, y * width * 3, raw, y * stride + 1, width * 3 );
		}
		using var ms = new System.IO.MemoryStream();
		using ( var ds = new System.IO.Compression.DeflateStream( ms, System.IO.Compression.CompressionLevel.Optimal, true ) )
		{
			ds.Write( raw, 0, raw.Length );
		}
		// zlib wrapper: 78 9C + deflate + adler32
		var deflate = ms.ToArray();
		var idat = new byte[deflate.Length + 6];
		idat[0] = 0x78; idat[1] = 0x9c;
		System.Buffer.BlockCopy( deflate, 0, idat, 2, deflate.Length );
		var adler = Adler32( raw );
		System.Buffer.BlockCopy( BigEndian( (int)adler ), 0, idat, 2 + deflate.Length, 4 );

		var output = new System.Collections.Generic.List<byte>();
		output.AddRange( sig );
		AppendChunk( output, "IHDR", ihdr );
		AppendChunk( output, "IDAT", idat );
		AppendChunk( output, "IEND", new byte[0] );
		return output.ToArray();
	}

	static byte[] BigEndian( int v )
	{
		return new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
	}
	static uint Adler32( byte[] data )
	{
		const uint MOD = 65521;
		uint a = 1, b = 0;
		foreach ( var x in data ) { a = (a + x) % MOD; b = (b + a) % MOD; }
		return (b << 16) | a;
	}
	static void AppendChunk( System.Collections.Generic.List<byte> list, string type, byte[] data )
	{
		list.AddRange( BigEndian( data.Length ) );
		var typeBytes = System.Text.Encoding.ASCII.GetBytes( type );
		list.AddRange( typeBytes );
		list.AddRange( data );
		var crcBuf = new byte[typeBytes.Length + data.Length];
		System.Buffer.BlockCopy( typeBytes, 0, crcBuf, 0, typeBytes.Length );
		System.Buffer.BlockCopy( data, 0, crcBuf, typeBytes.Length, data.Length );
		list.AddRange( BigEndian( (int)Crc32( crcBuf ) ) );
	}
	static uint Crc32( byte[] data )
	{
		uint c = 0xffffffff;
		for ( int i = 0; i < data.Length; i++ )
		{
			c ^= data[i];
			for ( int k = 0; k < 8; k++ ) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : (c >> 1);
		}
		return c ^ 0xffffffff;
	}

	/// Load a Material via Sandbox.Material.Load and dump its texture bindings.
	public static object InspectMaterial( JsonElement req )
	{
		var vmat = req.TryGetProperty( "vmat_path", out var v ) ? v.GetString() : null;
		if ( string.IsNullOrEmpty( vmat ) ) throw new System.InvalidOperationException( "missing vmat_path" );
		var material = Sandbox.Material.Load( vmat );
		if ( material == null ) throw new System.InvalidOperationException( $"could not load {vmat}" );

		var info = new System.Collections.Generic.Dictionary<string, object>();
		var t = material.GetType();
		// Common texture properties on s&box Material
		foreach ( var name in new[] { "Color", "Roughness", "Normal", "AmbientOcclusion", "Metalness", "Height", "Emission", "Tint" } )
		{
			var p = t.GetProperty( name, BindingFlags.Public | BindingFlags.Instance );
			if ( p != null )
			{
				try
				{
					var val = p.GetValue( material );
					info[name] = val == null ? "null" : (val.GetType().Name + ": " + val.ToString());
				}
				catch ( System.Exception ex ) { info[name] = "err: " + ex.Message; }
			}
		}
		// Try GetTexture by common attribute names + dump every property of the returned Texture
		var getTexture = t.GetMethod( "GetTexture", new[] { typeof( string ) } );
		if ( getTexture != null )
		{
			foreach ( var name in new[] { "Color", "g_tColor", "Normal", "g_tNormal", "AO", "g_tAmbientOcclusion" } )
			{
				try
				{
					var tex = getTexture.Invoke( material, new object[] { name } );
					if ( tex == null ) continue;
					var texInfo = new System.Collections.Generic.Dictionary<string, object>();
					foreach ( var p in tex.GetType().GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
					{
						try
						{
							var val = p.GetValue( tex );
							texInfo[p.Name] = val?.ToString() ?? "null";
						}
						catch { }
					}
					info["GetTexture(" + name + ")"] = texInfo;
				}
				catch { }
			}
		}
		// Properties exposed on Material like Attributes / Resource path
		foreach ( var name in new[] { "ResourceName", "ResourcePath", "Name" } )
		{
			var p = t.GetProperty( name, BindingFlags.Public | BindingFlags.Instance );
			if ( p != null )
			{
				try { info[name] = p.GetValue( material )?.ToString(); }
				catch { }
			}
		}
		return new { ok = true, vmat_path = vmat, material_type = t.FullName, info };
	}

	/// <summary>
	/// Deep inspection of every live Sandbox.Terrain across all open editor
	/// sessions. Reports Storage's Materials list, MaterialSettings, and
	/// whether each layer's TerrainMaterial loads cleanly. Use to diagnose
	/// terrain rendering issues that aren't explained by .tmat_c content.
	/// </summary>
	public static object InspectTerrains()
	{
		var hits = new System.Collections.Generic.List<object>();
		var sessionType = FindType( "Editor.SceneEditorSession" );
		var allList = sessionType?.GetProperty( "All", BindingFlags.Public | BindingFlags.Static )?.GetValue( null ) as System.Collections.IEnumerable;
		if ( allList == null ) return new { ok = false, error = "no editor sessions" };

		foreach ( var session in allList )
		{
			var sceneProp = session.GetType().GetProperty( "Scene", BindingFlags.Public | BindingFlags.Instance );
			var scene = sceneProp?.GetValue( session ) as Sandbox.Scene;
			if ( scene == null ) continue;

			foreach ( var terrain in scene.GetAllComponents<Sandbox.Terrain>() )
			{
				var storage = terrain.Storage;
				var info = new System.Collections.Generic.Dictionary<string, object>
				{
					["scene"] = scene.Name,
					["terrain_id"] = terrain.Id.ToString(),
					["go"] = terrain.GameObject?.Name,
					["enabled"] = terrain.Enabled,
					["go_enabled"] = terrain.GameObject?.Enabled ?? false,
					["has_storage"] = storage != null,
				};
				if ( storage != null )
				{
					var storageType = storage.GetType();
					info["resolution"] = storageType.GetProperty( "Resolution" )?.GetValue( storage );
					info["terrain_size"] = storageType.GetProperty( "TerrainSize" )?.GetValue( storage );
					info["terrain_height"] = storageType.GetProperty( "TerrainHeight" )?.GetValue( storage );

					var mats = storageType.GetProperty( "Materials" )?.GetValue( storage ) as System.Collections.IEnumerable;
					var matsInfo = new System.Collections.Generic.List<object>();
					if ( mats != null )
					{
						int idx = 0;
						foreach ( var m in mats )
						{
							if ( m == null ) { matsInfo.Add( new { layer = idx, value = "null" } ); idx++; continue; }
							var mt = m.GetType();
							var mInfo = new System.Collections.Generic.Dictionary<string, object>
							{
								["layer"] = idx,
								["type"] = mt.FullName,
								["is_string"] = m is string,
							};
							if ( m is string s )
							{
								mInfo["path"] = s;
								var assetSystem = FindType( "Editor.AssetSystem" );
								var find = assetSystem?.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
								var asset = find?.Invoke( null, new object[] { s } );
								mInfo["asset_found"] = asset != null;
								if ( asset != null )
								{
									mInfo["albedo"] = ReadResourceField( asset, "AlbedoImage" ) ?? "<none>";
								}
							}
							else
							{
								mInfo["albedo"] = mt.GetProperty( "AlbedoImage" )?.GetValue( m )?.ToString() ?? "<none>";
								mInfo["resource_path"] = mt.GetProperty( "ResourcePath" )?.GetValue( m )?.ToString();
								// Check the actual GPU texture handles — these
								// are what the shader samples. Null/Error here
								// is what makes the terrain render purple.
								void TexInfo( string slot )
								{
									var tex = mt.GetProperty( slot )?.GetValue( m );
									if ( tex == null ) { mInfo[$"{slot}_state"] = "null"; return; }
									var tt = tex.GetType();
									var w = tt.GetProperty( "Width" )?.GetValue( tex );
									var h = tt.GetProperty( "Height" )?.GetValue( tex );
									var isErr = tt.GetProperty( "IsError" )?.GetValue( tex );
									var isLoaded = tt.GetProperty( "IsLoaded" )?.GetValue( tex );
									var isValid = tt.GetProperty( "IsValid" )?.GetValue( tex );
									mInfo[$"{slot}_state"] = $"valid={isValid} loaded={isLoaded} error={isErr} {w}x{h}";
								}
								TexInfo( "BCRTexture" );
								TexInfo( "NHOTexture" );
							}
							matsInfo.Add( mInfo );
							idx++;
						}
					}
					info["materials"] = matsInfo;
					info["materials_count"] = matsInfo.Count;

					var settings = storageType.GetProperty( "MaterialSettings" )?.GetValue( storage );
					if ( settings != null )
					{
						var st = settings.GetType();
						info["height_blend_enabled"] = st.GetProperty( "HeightBlendEnabled" )?.GetValue( settings );
						info["height_blend_sharpness"] = st.GetProperty( "HeightBlendSharpness" )?.GetValue( settings );
					}

					// Material override on the Terrain component itself (overrides per-layer).
					var matOverride = terrain.GetType().GetProperty( "MaterialOverride" )?.GetValue( terrain );
					info["material_override"] = matOverride?.ToString() ?? "none";
				}
				hits.Add( info );
			}
		}
		return new { ok = true, count = hits.Count, terrains = hits };
	}

	public static object ListTerrains()
	{
		var hits = new System.Collections.Generic.List<object>();
		var sessionType = FindType( "Editor.SceneEditorSession" );
		var allList = sessionType?.GetProperty( "All", BindingFlags.Public | BindingFlags.Static )?.GetValue( null ) as System.Collections.IEnumerable;
		if ( allList == null )
		{
			hits.Add( new { stage = "no_All_property" } );
			return new { ok = true, results = hits };
		}
		int sessionIdx = 0;
		foreach ( var session in allList )
		{
			var sceneProp = session.GetType().GetProperty( "Scene", BindingFlags.Public | BindingFlags.Instance );
			var scene = sceneProp?.GetValue( session ) as Sandbox.Scene;
			hits.Add( new { stage = $"session[{sessionIdx}]", scene_name = scene?.Name, scene_found = scene != null } );
			if ( scene != null )
			{
				foreach ( var t in scene.GetAllComponents<Sandbox.Terrain>() )
				{
					hits.Add( new
					{
						stage = $"terrain in '{scene.Name}'",
						id = t.Id.ToString(),
						has_storage = t.Storage != null,
						gameobject_name = t.GameObject?.Name,
					} );
				}
			}
			sessionIdx++;
		}
		return new { ok = true, session_count = sessionIdx, results = hits };
	}

	static Sandbox.Terrain FindTerrainByGuid( System.Guid id )
	{
		// Walk every open editor session — the user may be viewing MainMap
		// while Testing is the "Active" tab, so checking just .Active misses it.
		var sessionType = FindType( "Editor.SceneEditorSession" );
		if ( sessionType == null ) return null;
		var allProp = sessionType.GetProperty( "All", BindingFlags.Public | BindingFlags.Static );
		var allList = allProp?.GetValue( null ) as System.Collections.IEnumerable;
		if ( allList == null )
		{
			// Fallback to .Active.
			var active = sessionType.GetProperty( "Active", BindingFlags.Public | BindingFlags.Static )?.GetValue( null );
			allList = active != null ? new[] { active } : System.Linq.Enumerable.Empty<object>();
		}
		foreach ( var session in allList )
		{
			var sceneProp = session.GetType().GetProperty( "Scene", BindingFlags.Public | BindingFlags.Instance );
			if ( sceneProp?.GetValue( session ) is not Sandbox.Scene scene ) continue;
			foreach ( var t in scene.GetAllComponents<Sandbox.Terrain>() )
			{
				if ( t.Id == id ) return t;
			}
		}
		return null;
	}

	// ---- Value-noise helpers (mirror tools/scripts/generate-dune-heightmap.ts) -------

	static float Hash( int x, int y, int s )
	{
		unchecked
		{
			int h = x * 374761393 + y * 668265263 + s * 982451653;
			h = h ^ (int)((uint)h >> 13);
			h = h * 1274126177;
			h = h ^ (int)((uint)h >> 16);
			return (uint)h / (float)uint.MaxValue;
		}
	}

	static float Smooth( float t ) => t * t * (3f - 2f * t);

	static float ValueNoise( float x, float y, int s )
	{
		int xi = (int)System.Math.Floor( x );
		int yi = (int)System.Math.Floor( y );
		float xf = x - xi;
		float yf = y - yi;
		float u = Smooth( xf );
		float v = Smooth( yf );
		float a = Hash( xi, yi, s );
		float b = Hash( xi + 1, yi, s );
		float c = Hash( xi, yi + 1, s );
		float d = Hash( xi + 1, yi + 1, s );
		return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v;
	}

	static System.Type FindType( string name )
	{
		foreach ( var asm in System.AppDomain.CurrentDomain.GetAssemblies() )
		{
			var t = asm.GetType( name, false );
			if ( t != null ) return t;
		}
		return null;
	}

	// ─────────────────────────────────────────────────────────────────────
	// .tmat / GameResource cache-drift fixes (2026-04-30)
	//
	// The earlier CreateTerrainMaterial pattern (Activator.CreateInstance →
	// reflection-set props → write "{}" placeholder → RegisterFile →
	// SaveToDisk(inst)) leaves the engine's *in-memory* cached GameResource
	// stuck on the empty placeholder, even though the disk file ends up
	// correct. The texture compiler reads from that cached GameResource and
	// hands empty strings to the vtex compiler, which silently emits a
	// useless .tmat_c that renders as engine-magenta. See sbox-dev.log:
	//   "Error reading texture \"\" for \"color\" when compiling file
	//    .../dunes_sand_tmat_bcr.generated.vtex" → [FAIL]
	//
	// Fix: write full JSON to disk *first*, then RegisterFile so the first
	// load reads correct content, then force-reload if the asset was already
	// registered, then verify in-memory == disk before compiling.
	// ─────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Force a registered asset to drop its cached GameResource and reload
	/// from disk. Editor.Asset doesn't expose a Reload(), but it does expose
	/// SetInMemoryReplacement(String) / ClearInMemoryReplacement() — using
	/// SetInMemoryReplacement(diskJson) seeds the in-memory cache with the
	/// content we just wrote to disk, which is exactly the cache-fix we need.
	/// </summary>
	static string TryReloadAsset( object asset )
	{
		if ( asset == null ) return "asset-null";
		var t = asset.GetType();

		// Best path: SetInMemoryReplacement(json). Read the disk JSON via the
		// asset's own ReadJson(), then push it back as the in-memory rep. This
		// guarantees in-memory == disk for the compiler.
		var readJson = t.GetMethod( "ReadJson", BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null );
		var setRep = t.GetMethod( "SetInMemoryReplacement", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof( string ) }, null );
		var clearRep = t.GetMethod( "ClearInMemoryReplacement", BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null );
		if ( readJson != null && setRep != null )
		{
			try
			{
				var json = (string)readJson.Invoke( asset, null );
				if ( !string.IsNullOrEmpty( json ) )
				{
					var ok = (bool)setRep.Invoke( asset, new object[] { json } );
					if ( ok )
					{
						// Then immediately clear so the engine treats the on-disk
						// state as authoritative going forward (no in-memory
						// override sticking around). The Set call already
						// updated the parsed cache.
						try { clearRep?.Invoke( asset, null ); } catch { }
						return "SetInMemoryReplacement(diskJson)";
					}
				}
			}
			catch ( System.Exception ex )
			{
				Log.Warning( $"[TryReloadAsset] SetInMemoryReplacement threw: {ex.GetBaseException().Message}" );
			}
		}
		return "no-reload-method-found";
	}

	/// <summary>
	/// Read a single property off the asset's cached GameResource via
	/// reflection. Uses the unambiguous LoadResource(Type) overload to avoid
	/// matching the generic LoadResource&lt;T&gt;() at the same time.
	/// </summary>
	static string ReadResourceField( object asset, string fieldName )
	{
		if ( asset == null ) return null;
		var loadResource = asset.GetType().GetMethod( "LoadResource", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof( System.Type ) }, null );
		if ( loadResource == null ) return null;
		// Pass typeof(GameResource) — the engine resolves the actual subclass.
		var gameResType = FindType( "Sandbox.GameResource" ) ?? typeof( object );
		object resource;
		try { resource = loadResource.Invoke( asset, new object[] { gameResType } ); }
		catch ( System.Exception ex ) { Log.Warning( $"[ReadResourceField] LoadResource threw: {ex.GetBaseException().Message}" ); return null; }
		if ( resource == null ) return null;
		var prop = resource.GetType().GetProperty( fieldName );
		if ( prop == null ) return null;
		try { return prop.GetValue( resource )?.ToString(); }
		catch { return null; }
	}

	static object LoadResourceForAsset( object asset )
	{
		if ( asset == null ) return null;
		var loadResource = asset.GetType().GetMethod( "LoadResource", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof( System.Type ) }, null );
		if ( loadResource == null ) return null;
		var gameResType = FindType( "Sandbox.GameResource" ) ?? typeof( object );
		try { return loadResource.Invoke( asset, new object[] { gameResType } ); }
		catch ( System.Exception ex ) { Log.Warning( $"[LoadResourceForAsset] threw: {ex.GetBaseException().Message}" ); return null; }
	}

	/// <summary>
	/// Diagnostic: returns both the on-disk JSON contents and the live
	/// in-memory GameResource property values for a .tmat (or any
	/// GameResource asset). If they differ, the engine has stale cached
	/// state and the compiler will emit a broken artifact.
	/// </summary>
	public static object InspectTmat( JsonElement req )
	{
		var path = req.TryGetProperty( "path", out var p ) ? p.GetString() : null;
		if ( string.IsNullOrEmpty( path ) ) throw new System.InvalidOperationException( "missing 'path'" );

		var contentRoot = Editor.FileSystem.Content.GetFullPath( "/" );
		var fullPath = System.IO.Path.Combine( contentRoot, path );

		object disk = null;
		if ( System.IO.File.Exists( fullPath ) )
		{
			try
			{
				var diskJson = System.IO.File.ReadAllText( fullPath );
				using var doc = JsonDocument.Parse( diskJson );
				var dict = new SortedDictionary<string, string>();
				foreach ( var prop in doc.RootElement.EnumerateObject() )
					dict[prop.Name] = prop.Value.ToString();
				disk = dict;
			}
			catch ( System.Exception ex ) { disk = $"<parse error: {ex.Message}>"; }
		}
		else
		{
			disk = "<file not found>";
		}

		object memory = null;
		string resourceTypeName = null;
		var assetSystem = FindType( "Editor.AssetSystem" );
		var findByPath = assetSystem?.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var asset = findByPath?.Invoke( null, new object[] { path } );
		if ( asset != null )
		{
			var resource = LoadResourceForAsset( asset );
			if ( resource != null )
			{
				resourceTypeName = resource.GetType().FullName;
				var dict = new SortedDictionary<string, string>();
				foreach ( var prop in resource.GetType().GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
				{
					if ( !prop.CanRead ) continue;
					try
					{
						var v = prop.GetValue( resource );
						dict[prop.Name] = v?.ToString() ?? "null";
					}
					catch ( System.Exception ex ) { dict[prop.Name] = $"<error: {ex.Message}>"; }
				}
				memory = dict;
			}
			else
			{
				memory = "<LoadResource returned null>";
			}
		}
		else
		{
			memory = "<asset not registered>";
		}

		return new { path, registered = asset != null, resource_type = resourceTypeName, disk, memory };
	}

	/// <summary>
	/// Force a registered asset to drop its cached GameResource and reread
	/// from disk. Use after editing a resource source file outside the
	/// engine's I/O path.
	/// </summary>
	public static object ReloadAsset( JsonElement req )
	{
		var path = req.TryGetProperty( "path", out var p ) ? p.GetString() : null;
		if ( string.IsNullOrEmpty( path ) ) throw new System.InvalidOperationException( "missing 'path'" );
		var assetSystem = FindType( "Editor.AssetSystem" );
		var findByPath = assetSystem?.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var asset = findByPath?.Invoke( null, new object[] { path } );
		if ( asset == null ) throw new System.InvalidOperationException( $"asset not registered: {path}" );
		var method = TryReloadAsset( asset );
		// Best-effort verification: re-read AlbedoImage if it's a TerrainMaterial.
		var albedo = ReadResourceField( asset, "AlbedoImage" );
		return new { ok = true, path, method, in_memory_albedo = albedo };
	}

	/// <summary>
	/// Fresh, cache-safe rewrite of CreateTerrainMaterial. Writes the full
	/// JSON to disk before any asset registration, registers (or finds),
	/// force-reloads the cached GameResource, and verifies in-memory
	/// AlbedoImage matches what was just written. Caller can then trigger
	/// a compile knowing the source-of-truth in memory is correct.
	/// </summary>
	public static object CreateTerrainMaterialV2( JsonElement req )
	{
		var outPath = req.TryGetProperty( "out_path", out var op ) ? op.GetString() : "maps/dunes_sand.tmat";
		string albedo = req.TryGetProperty( "albedo", out var a ) ? a.GetString() : null;
		string roughness = req.TryGetProperty( "roughness", out var ro ) ? ro.GetString() : "materials/default/default_rough_s1import.tga";
		string normal = req.TryGetProperty( "normal", out var n ) ? n.GetString() : "materials/default/default_normal.tga";
		string height = req.TryGetProperty( "height", out var hh ) ? hh.GetString() : "materials/default/default_ao.tga";
		string ao = req.TryGetProperty( "ao", out var ax ) ? ax.GetString() : "materials/default/default_ao.tga";
		float uvScale = req.TryGetProperty( "uv_scale", out var us ) ? us.GetSingle() : 1.0f;

		if ( string.IsNullOrEmpty( albedo ) )
			throw new System.InvalidOperationException( "missing 'albedo' (texture path)" );

		// Build the JSON that mirrors the working stock forest_ground.tmat shape.
		// Property order matches the engine's TerrainMaterial serialization so
		// the file looks like one the editor itself would have written.
		var dict = new System.Collections.Generic.Dictionary<string, object>
		{
			["AlbedoImage"] = albedo,
			["RoughnessImage"] = roughness,
			["NormalImage"] = normal,
			["HeightImage"] = height,
			["AOImage"] = ao,
			["UVScale"] = uvScale,
			["UVRotation"] = 0,
			["Metalness"] = 0,
			["NormalStrength"] = 1.0f,
			["HeightBlendStrength"] = 1.0f,
			["__references"] = System.Array.Empty<string>(),
			["__version"] = 0,
		};
		var json = JsonSerializer.Serialize( dict, new JsonSerializerOptions { WriteIndented = true } );

		var contentRoot = Editor.FileSystem.Content.GetFullPath( "/" );
		var fullPath = System.IO.Path.Combine( contentRoot, outPath );
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( fullPath ) );

		// Write the full content to disk *before* any asset registration so the
		// engine's first parse populates the cached GameResource correctly.
		System.IO.File.WriteAllText( fullPath, json );

		var assetSystem = FindType( "Editor.AssetSystem" );
		if ( assetSystem == null ) throw new System.InvalidOperationException( "Editor.AssetSystem not found" );
		var findByPath = assetSystem.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var asset = findByPath?.Invoke( null, new object[] { outPath } );
		bool wasAlreadyRegistered = asset != null;

		if ( asset == null )
		{
			var registerFile = assetSystem.GetMethod( "RegisterFile", BindingFlags.Public | BindingFlags.Static );
			asset = registerFile?.Invoke( null, new object[] { fullPath } );
		}
		if ( asset == null ) throw new System.InvalidOperationException( $"could not register asset at {outPath}" );

		// If the asset was already registered, the engine may still have a stale
		// cached resource from a previous (broken) write. Force a reload.
		string reloadMethod = wasAlreadyRegistered ? TryReloadAsset( asset ) : "fresh-registration";

		// Verify in-memory state matches what we just wrote.
		string memAlbedo = ReadResourceField( asset, "AlbedoImage" ) ?? "";
		bool inMemoryMatches = memAlbedo == albedo;

		// If still out of sync, last-resort: try one more reload pass.
		if ( !inMemoryMatches )
		{
			var second = TryReloadAsset( asset );
			memAlbedo = ReadResourceField( asset, "AlbedoImage" ) ?? "";
			inMemoryMatches = memAlbedo == albedo;
			reloadMethod = $"{reloadMethod}; retry={second}";
		}

		return new
		{
			ok = inMemoryMatches,
			tmat_path = outPath,
			was_already_registered = wasAlreadyRegistered,
			reload_method = reloadMethod,
			in_memory_albedo = memAlbedo,
			in_memory_matches_disk = inMemoryMatches,
			disk_albedo = albedo,
		};
	}

	/// <summary>
	/// Tighter compile_asset variant: forces a recompile, reads the last N
	/// lines of sbox-dev.log immediately after, and surfaces any
	/// TextureCompiler [FAIL] entries that mention the asset's vtex children.
	/// </summary>
	public static object CompileAssetVerified( JsonElement req )
	{
		var path = req.TryGetProperty( "path", out var p ) ? p.GetString() : null;
		if ( string.IsNullOrEmpty( path ) ) throw new System.InvalidOperationException( "missing 'path'" );
		var assetSystem = FindType( "Editor.AssetSystem" );
		var findByPath = assetSystem?.GetMethod( "FindByPath", BindingFlags.Public | BindingFlags.Static );
		var asset = findByPath?.Invoke( null, new object[] { path } );
		if ( asset == null ) throw new System.InvalidOperationException( $"asset not found: {path}" );
		var compileMethod = asset.GetType().GetMethod( "Compile", BindingFlags.Public | BindingFlags.Instance );
		if ( compileMethod == null ) throw new System.InvalidOperationException( "Asset.Compile not found" );

		bool compileResult = false;
		string compileError = null;
		try { compileResult = (bool)compileMethod.Invoke( asset, new object[] { true } ); }
		catch ( System.Exception ex ) { compileError = ex.GetBaseException().Message; }

		// Scan the recent log for FAILs related to this asset's vtex children.
		// Path "maps/dunes_sand.tmat" → vtex names start with "dunes_sand_tmat_".
		var stem = System.IO.Path.GetFileNameWithoutExtension( path );
		var vtexMarker = stem + "_tmat_";
		var fails = new System.Collections.Generic.List<string>();
		var logPath = ResolveLogPath();
		if ( logPath != null && System.IO.File.Exists( logPath ) )
		{
			try
			{
				// Read last ~200 lines. The engine holds the log open for writes,
				// so use FileShare.ReadWrite to avoid "in use by another process".
				var allLines = new System.Collections.Generic.List<string>();
				using ( var fs = new System.IO.FileStream( logPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite ) )
				using ( var sr = new System.IO.StreamReader( fs ) )
				{
					string ln;
					while ( (ln = sr.ReadLine()) != null ) allLines.Add( ln );
				}
				int start = System.Math.Max( 0, allLines.Count - 400 );
				for ( int i = start; i < allLines.Count; i++ )
				{
					var line = allLines[i];
					if ( line.Contains( "[engine/TextureCompiler]" ) && line.Contains( "FAIL" ) ) fails.Add( line );
					else if ( line.Contains( vtexMarker ) && (line.Contains( "FAIL" ) || line.Contains( "Error" )) ) fails.Add( line );
				}
			}
			catch ( System.Exception ex ) { fails.Add( $"<log read error: {ex.Message}>" ); }
		}

		return new
		{
			ok = compileResult && fails.Count == 0,
			path,
			compile_returned = compileResult,
			compile_error = compileError,
			recent_fails = fails,
			vtex_marker = vtexMarker,
		};
	}

	static string ResolveLogPath()
	{
		var dir = new System.IO.DirectoryInfo( System.AppDomain.CurrentDomain.BaseDirectory );
		for ( int i = 0; i < 8 && dir != null; i++ )
		{
			var candidate = System.IO.Path.Combine( dir.FullName, "logs", "sbox-dev.log" );
			if ( System.IO.File.Exists( candidate ) ) return candidate;
			dir = dir.Parent;
		}
		// Env override.
		var env = System.Environment.GetEnvironmentVariable( "GRAPPLESHIP_SBOX_LOG_PATH" );
		if ( !string.IsNullOrEmpty( env ) && System.IO.File.Exists( env ) ) return env;
		return null;
	}
}
