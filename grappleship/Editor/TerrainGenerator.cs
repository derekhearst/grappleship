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

		// Push heightfield to GPU so it renders. Some s&box versions need
		// SyncCPUTexture too — try both, ignore signature mismatches.
		try
		{
			var syncCpu = terrain.GetType().GetMethod( "SyncCPUTexture", BindingFlags.Public | BindingFlags.Instance );
			if ( syncCpu != null )
			{
				var pars = syncCpu.GetParameters();
				if ( pars.Length == 0 ) syncCpu.Invoke( terrain, null );
				// If it has 2 args (SyncFlags, RectInt) we just rely on SyncGPUTexture below.
			}
		}
		catch { /* best effort */ }
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
		};
	}

	static Sandbox.Terrain FindTerrainByGuid( System.Guid id )
	{
		// Try the editor's scene first.
		var session = FindType( "Editor.SceneEditorSession" );
		if ( session != null )
		{
			var active = session.GetProperty( "Active", BindingFlags.Public | BindingFlags.Static )?.GetValue( null );
			if ( active != null )
			{
				var sceneProp = active.GetType().GetProperty( "Scene", BindingFlags.Public | BindingFlags.Instance );
				if ( sceneProp?.GetValue( active ) is Sandbox.Scene scene )
				{
					foreach ( var t in scene.GetAllComponents<Sandbox.Terrain>() )
					{
						if ( t.Id == id ) return t;
					}
				}
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
}
