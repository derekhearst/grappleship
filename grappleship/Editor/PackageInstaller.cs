using System.Reflection;
using System.Text.Json;

namespace GrappleShip.EditorTools;

/// <summary>
/// Installs a cloud package by ident (e.g. "arghbeef.vikinghelmet"). Wraps
/// Editor.AssetSystem.InstallAsync — the same API the editor's "Install"
/// button uses. Blocks the calling frame until the install completes; this
/// can take a few seconds for big packages so the bridge call should use a
/// generous timeout (≥ 60s).
/// </summary>
public static class PackageInstaller
{
	public static object Install( JsonElement req )
	{
		var ident = req.TryGetProperty( "ident", out var i ) ? i.GetString() : null;
		if ( string.IsNullOrEmpty( ident ) )
		{
			throw new System.InvalidOperationException( "missing 'ident' (e.g. 'arghbeef.vikinghelmet')" );
		}

		System.Type assetSystem = null;
		foreach ( var asm in System.AppDomain.CurrentDomain.GetAssemblies() )
		{
			assetSystem = asm.GetType( "Editor.AssetSystem", false );
			if ( assetSystem != null ) break;
		}
		if ( assetSystem == null )
		{
			throw new System.InvalidOperationException( "Editor.AssetSystem not found" );
		}

		// InstallAsync(string ident, bool ?, Action<float> progress, CancellationToken ct)
		var method = assetSystem.GetMethod(
			"InstallAsync",
			BindingFlags.Public | BindingFlags.Static,
			null,
			new[] { typeof( string ), typeof( bool ), typeof( System.Action<float> ), typeof( System.Threading.CancellationToken ) },
			null );
		if ( method == null )
		{
			throw new System.InvalidOperationException( "InstallAsync(string,bool,Action<float>,ct) not found" );
		}

		var task = method.Invoke( null, new object[] { ident, false, null, default( System.Threading.CancellationToken ) } );
		if ( task == null )
		{
			throw new System.InvalidOperationException( "InstallAsync returned null" );
		}

		object result;
		try
		{
			var awaiter = task.GetType().GetMethod( "GetAwaiter" )?.Invoke( task, null );
			result = awaiter?.GetType().GetMethod( "GetResult" )?.Invoke( awaiter, null );
		}
		catch ( System.Exception ex )
		{
			throw new System.InvalidOperationException( $"InstallAsync threw: {ex.GetBaseException().Message}" );
		}

		// Result is the primary Asset (or null if install failed).
		if ( result == null )
		{
			return new { ok = false, ident, error = "install returned null (package may not exist)" };
		}

		// Asset.Package.Title / Asset.Package.PrimaryAsset / Asset.Path
		var assetPath = ReadString( result, "Path" )
			?? ReadString( result, "RelativePath" );
		var assetName = ReadString( result, "Name" );

		string packageTitle = null;
		string primaryAsset = null;
		string packageIdent = null;
		var pkgProp = result.GetType().GetProperty( "Package", BindingFlags.Public | BindingFlags.Instance );
		if ( pkgProp != null )
		{
			var pkg = pkgProp.GetValue( result );
			if ( pkg != null )
			{
				packageTitle = ReadString( pkg, "Title" );
				primaryAsset = ReadString( pkg, "PrimaryAsset" );
				packageIdent = ReadString( pkg, "FullIdent" ) ?? ReadString( pkg, "Ident" );
			}
		}

		return new
		{
			ok = true,
			ident,
			package_ident = packageIdent ?? ident,
			package_title = packageTitle,
			primary_asset = primaryAsset ?? assetPath,
			asset_path = assetPath,
			asset_name = assetName,
		};
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
}
