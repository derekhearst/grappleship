using System.Reflection;
using System.Text.Json;

namespace GrappleShip.EditorTools;

/// <summary>
/// Live asset search invoked by the MCP via EditorBridge. Queries every API
/// surface we can find — local mounted assets, online/cloud catalog if
/// reachable. Returns a ranked, sized list rather than a giant dump.
/// </summary>
public static class AssetSearcher
{
	public static object Search( JsonElement req )
	{
		var query = TryStr( req, "query" ) ?? "";
		var kindFilter = TryStr( req, "kind" );
		var limit = TryInt( req, "limit" ) ?? 50;
		var includeCloud = TryBool( req, "include_cloud" ) ?? false;

		var lowered = query.ToLowerInvariant();
		var hits = new List<AssetHit>();

		// Local: AssetSystem.All — fast, always available when editor is up.
		var localCount = 0;
		foreach ( var asset in EnumerateLocalAssets() )
		{
			if ( asset == null ) continue;
			localCount++;
			var path = ReadString( asset, "Path" )
				?? ReadString( asset, "RelativePath" )
				?? ReadString( asset, "RootRelativePath" );
			if ( string.IsNullOrEmpty( path ) ) continue;
			path = path.Replace( '\\', '/' );

			// Exclude derived/internal files: .generated.vtex etc, *_c compiled,
			// IsTrivialChild flag if present.
			if ( path.Contains( ".generated." ) ) continue;
			if ( ReadBool( asset, "IsTrivialChild" ) == true ) continue;

			var name = ReadString( asset, "Name" );
			var package = ReadPackageInfo( asset );

			var pathScore = ScoreMatch( path.ToLowerInvariant(), lowered );
			var nameScore = !string.IsNullOrEmpty( name ) ? ScoreMatch( name.ToLowerInvariant(), lowered ) : 0;
			// Package title is what the Library Manager shows, e.g. "Ship Large"
			// for kenneynl.shiplarge. Match against it too so users can search
			// by the displayed name.
			var titleScore = !string.IsNullOrEmpty( package.title ) ? ScoreMatch( package.title.ToLowerInvariant(), lowered ) : 0;
			var identScore = !string.IsNullOrEmpty( package.ident ) ? ScoreMatch( package.ident.ToLowerInvariant(), lowered ) : 0;
			var score = pathScore;
			if ( nameScore > score ) score = nameScore;
			if ( titleScore > score ) score = titleScore;
			if ( identScore > score ) score = identScore;
			if ( score == 0 ) continue;

			// Primary-asset boost: if this asset is the package's PrimaryAsset,
			// it's the canonical entry-point users should see first.
			if ( !string.IsNullOrEmpty( package.primaryAsset ) && package.primaryAsset == path )
			{
				score += 10;
			}

			var kind = ResolveKind( asset, path );
			if ( !string.IsNullOrEmpty( kindFilter ) && !kind.Contains( kindFilter ) ) continue;

			hits.Add( new AssetHit
			{
				path = path,
				name = name,
				kind = kind,
				score = score,
				origin = package.isRemote ? "cloud" : "local",
				package_ident = package.ident,
				package_title = package.title,
			} );
		}

		// Cloud: hits asset.party / sbox.game's package index via
		// Sandbox.Package.FindAsync. Returns packages we don't have installed.
		var cloudHits = includeCloud ? TryCloudSearch( query, kindFilter, limit ) : null;
		if ( cloudHits != null ) hits.AddRange( cloudHits );

		hits.Sort( ( a, b ) => b.score - a.score );
		if ( hits.Count > limit ) hits.RemoveRange( limit, hits.Count - limit );

		return new
		{
			query,
			local_scanned = localCount,
			cloud_attempted = includeCloud,
			cloud_supported = cloudHits != null,
			cloud_hits = cloudHits?.Count ?? 0,
			match_count = hits.Count,
			matches = hits,
		};
	}

	public class AssetHit
	{
		public string path { get; set; }
		public string name { get; set; }
		public string kind { get; set; }
		public int score { get; set; }
		public string origin { get; set; }
		public string package_ident { get; set; }
		public string package_title { get; set; }
	}

	struct PackageInfo
	{
		public string ident;
		public string title;
		public bool isRemote;
		public string primaryAsset;
	}

	static PackageInfo ReadPackageInfo( object asset )
	{
		var info = new PackageInfo();
		var packageProp = asset.GetType().GetProperty( "Package", BindingFlags.Public | BindingFlags.Instance );
		if ( packageProp == null ) return info;
		var pkg = packageProp.GetValue( asset );
		if ( pkg == null ) return info;
		info.ident = ReadString( pkg, "FullIdent" ) ?? ReadString( pkg, "Ident" );
		info.title = ReadString( pkg, "Title" );
		info.primaryAsset = ReadString( pkg, "PrimaryAsset" );
		info.isRemote = ReadBool( pkg, "IsRemote" ) ?? false;
		return info;
	}

	static bool? ReadBool( object obj, string name )
	{
		var t = obj.GetType();
		var p = t.GetProperty( name, BindingFlags.Public | BindingFlags.Instance );
		if ( p != null && p.PropertyType == typeof( bool ) )
		{
			var v = p.GetValue( obj );
			if ( v is bool b ) return b;
		}
		return null;
	}

	static int ScoreMatch( string pathLower, string queryLower )
	{
		if ( string.IsNullOrEmpty( queryLower ) ) return 1; // empty query = match all (used for listings)
		var slash = pathLower.LastIndexOf( '/' );
		var basename = slash >= 0 ? pathLower.Substring( slash + 1 ) : pathLower;
		var stem = basename;
		var dot = stem.LastIndexOf( '.' );
		if ( dot > 0 ) stem = stem.Substring( 0, dot );
		if ( basename == queryLower ) return 100;
		if ( stem == queryLower ) return 95;
		if ( basename.StartsWith( queryLower ) ) return 80;
		if ( basename.Contains( queryLower ) ) return 60;
		if ( pathLower.Contains( queryLower ) ) return 40;
		return 0;
	}

	static string ResolveKind( object asset, string path )
	{
		var t = ReadString( asset, "AssetType" )
			?? ReadString( asset, "TypeName" );
		if ( !string.IsNullOrEmpty( t ) ) return t;
		var typeProp = asset.GetType().GetProperty( "AssetType", BindingFlags.Public | BindingFlags.Instance );
		if ( typeProp != null )
		{
			var typeVal = typeProp.GetValue( asset );
			if ( typeVal != null )
			{
				var hint = ReadString( typeVal, "Name" )
					?? ReadString( typeVal, "Identifier" )
					?? ReadString( typeVal, "FileExtension" );
				if ( !string.IsNullOrEmpty( hint ) ) return hint;
			}
		}
		var ext = System.IO.Path.GetExtension( path );
		return string.IsNullOrEmpty( ext ) ? "" : ext.Substring( 1 );
	}

	static System.Collections.IEnumerable EnumerateLocalAssets()
	{
		var t = FindType( "Editor.AssetSystem", "Sandbox.AssetSystem" );
		if ( t == null ) return System.Linq.Enumerable.Empty<object>();
		var prop = t.GetProperty( "All", BindingFlags.Public | BindingFlags.Static );
		if ( prop != null && prop.GetValue( null ) is System.Collections.IEnumerable list ) return list;
		var method = t.GetMethod( "All", BindingFlags.Public | BindingFlags.Static, null, System.Type.EmptyTypes, null );
		if ( method != null && method.Invoke( null, null ) is System.Collections.IEnumerable list2 ) return list2;
		return System.Linq.Enumerable.Empty<object>();
	}

	static List<AssetHit> TryCloudSearch( string query, string kindFilter, int limit )
	{
		if ( string.IsNullOrEmpty( query ) ) return new List<AssetHit>();
		var packageType = FindType( "Sandbox.Package" );
		if ( packageType == null ) return null;
		// Sandbox.Package.FindAsync(string, int take, int skip, CancellationToken)
		var findAsync = packageType.GetMethod(
			"FindAsync",
			BindingFlags.Public | BindingFlags.Static,
			null,
			new[] { typeof( string ), typeof( int ), typeof( int ), typeof( System.Threading.CancellationToken ) },
			null );
		if ( findAsync == null ) return null;

		// Asset.party packages are organized by type; querying with type
		// prefixes ("type:model ship") narrows results, but FindAsync's
		// signature only takes a single string — pass the raw query.
		object task;
		try
		{
			task = findAsync.Invoke( null, new object[] { query, limit, 0, default( System.Threading.CancellationToken ) } );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[AssetSearcher] Package.FindAsync threw: {ex.Message}" );
			return new List<AssetHit>();
		}
		if ( task == null ) return new List<AssetHit>();

		// FindAsync returns Task<Package.FindResult>. Await via GetAwaiter()
		// (works for Task and ValueTask). We're on the frame thread; blocking
		// for a network round-trip is acceptable for an explicit search.
		object findResult;
		try
		{
			var awaiter = task.GetType().GetMethod( "GetAwaiter" )?.Invoke( task, null );
			findResult = awaiter?.GetType().GetMethod( "GetResult" )?.Invoke( awaiter, null );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[AssetSearcher] cloud search await failed: {ex.GetBaseException().Message}" );
			return new List<AssetHit>();
		}
		if ( findResult == null ) return new List<AssetHit>();

		// FindResult.Packages: RemotePackage[]
		var packagesProp = findResult.GetType().GetProperty( "Packages" );
		var packagesArr = packagesProp?.GetValue( findResult ) as System.Collections.IEnumerable;
		if ( packagesArr == null ) return new List<AssetHit>();

		var hits = new List<AssetHit>();
		foreach ( var pkg in packagesArr )
		{
			if ( pkg == null ) continue;
			var title = ReadString( pkg, "Title" );
			var ident = ReadString( pkg, "FullIdent" ) ?? ReadString( pkg, "Ident" );
			var primary = ReadString( pkg, "PrimaryAsset" );
			var typeName = ReadString( pkg, "TypeName" );

			// Server already filtered so anything returned is relevant. Score
			// against title/ident for sub-ranking.
			var matchSurface = $"{title} {ident} {primary}".ToLowerInvariant();
			var score = ScoreMatch( matchSurface, query.ToLowerInvariant() );
			if ( score == 0 ) score = 30;

			if ( !string.IsNullOrEmpty( kindFilter ) && !string.IsNullOrEmpty( typeName )
				&& !typeName.ToLowerInvariant().Contains( kindFilter.ToLowerInvariant() ) ) continue;

			hits.Add( new AssetHit
			{
				path = primary ?? "",
				name = title,
				kind = typeName ?? "",
				score = score,
				origin = "cloud-uninstalled",
				package_ident = ident,
				package_title = title,
			} );
		}
		return hits;
	}

	static System.Type FindType( params string[] candidates )
	{
		foreach ( var n in candidates )
		{
			foreach ( var asm in System.AppDomain.CurrentDomain.GetAssemblies() )
			{
				var t = asm.GetType( n, throwOnError: false );
				if ( t != null ) return t;
			}
		}
		return null;
	}

	static string ReadString( object obj, string name )
	{
		var t = obj.GetType();
		var p = t.GetProperty( name, BindingFlags.Public | BindingFlags.Instance );
		if ( p != null && p.PropertyType == typeof( string ) ) return (string)p.GetValue( obj );
		var f = t.GetField( name, BindingFlags.Public | BindingFlags.Instance );
		if ( f != null && f.FieldType == typeof( string ) ) return (string)f.GetValue( obj );
		return null;
	}

	static string TryStr( JsonElement req, string name )
	{
		return req.TryGetProperty( name, out var el ) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
	}
	static int? TryInt( JsonElement req, string name )
	{
		if ( req.TryGetProperty( name, out var el ) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32( out var v ) ) return v;
		return null;
	}
	static bool? TryBool( JsonElement req, string name )
	{
		if ( req.TryGetProperty( name, out var el ) )
		{
			if ( el.ValueKind == JsonValueKind.True ) return true;
			if ( el.ValueKind == JsonValueKind.False ) return false;
		}
		return null;
	}
}
