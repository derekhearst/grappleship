using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace GrappleShip.EditorTools;

/// <summary>
/// Minimal file-drop IPC. The MCP writes req-&lt;id&gt;.json into
/// &lt;tmp&gt;/grappleship-bridge/, the bridge picks one up per editor frame
/// (throttled to every ~10 frames so we don't hammer the disk), processes it
/// on the main thread, and writes res-&lt;id&gt;.json back.
///
/// Deliberately minimal: no Timer, no ConcurrentQueue, no static constructor,
/// nothing that touches the editor at type-load time. Everything happens
/// inside the [EditorEvent.Frame] handler, which the editor only invokes
/// once it's actually ready.
/// </summary>
public static class EditorBridge
{
	const string IpcDirName = "grappleship-bridge";

	static readonly UTF8Encoding _utf8NoBom = new( false );
	static int _frameCounter;
	static bool _logged;

	[Menu( "Editor", "GrappleShip/Bridge: Where Is It?" )]
	public static void ShowDir()
	{
		EditorUtility.DisplayDialog( "Bridge directory", IpcDir() );
	}

	[EditorEvent.Frame]
	public static void OnFrame()
	{
		// Throttle: only scan every ~10 frames. At 60fps that's 6 scans/sec —
		// plenty fast for chat-driven IPC, light enough to be invisible.
		_frameCounter++;
		if ( _frameCounter % 10 != 0 ) return;

		try
		{
			var dir = IpcDir();
			if ( !Directory.Exists( dir ) )
			{
				Directory.CreateDirectory( dir );
				return;
			}

			if ( !_logged )
			{
				Log.Info( $"[EditorBridge] watching {dir}" );
				_logged = true;
			}

			// Pick the oldest pending request, if any.
			string oldest = null;
			System.DateTime oldestTime = System.DateTime.MaxValue;
			foreach ( var path in Directory.EnumerateFiles( dir, "req-*.json" ) )
			{
				var t = File.GetCreationTimeUtc( path );
				if ( t < oldestTime )
				{
					oldestTime = t;
					oldest = path;
				}
			}
			if ( oldest != null ) HandleRequest( oldest );
		}
		catch ( System.Exception e )
		{
			// Never let the bridge take the editor down.
			Log.Warning( $"[EditorBridge] frame error: {e.Message}" );
		}
	}

	static string IpcDir()
	{
		return Path.Combine( Path.GetTempPath(), IpcDirName );
	}

	static void HandleRequest( string path )
	{
		string id = null;
		try
		{
			if ( !File.Exists( path ) ) return;
			var text = File.ReadAllText( path, _utf8NoBom );
			if ( text.Length > 0 && text[0] == '﻿' ) text = text.Substring( 1 );
			using var doc = JsonDocument.Parse( text );
			var root = doc.RootElement;
			id = root.TryGetProperty( "id", out var idEl ) ? idEl.GetString() : null;
			var action = root.TryGetProperty( "action", out var actEl ) ? actEl.GetString() : null;

			if ( string.IsNullOrEmpty( id ) || string.IsNullOrEmpty( action ) )
			{
				WriteResponse( id, false, null, "missing id or action" );
				return;
			}

			object result = null;
			string error = null;
			try
			{
				result = Dispatch( action, root );
			}
			catch ( System.Exception ex )
			{
				error = ex.Message;
			}

			WriteResponse( id, error == null, result, error );
		}
		catch ( System.Exception ex )
		{
			WriteResponse( id, false, null, $"bridge handler crash: {ex.Message}" );
		}
		finally
		{
			try { File.Delete( path ); } catch { /* best effort */ }
		}
	}

	static object Dispatch( string action, JsonElement req )
	{
		switch ( action )
		{
			case "ping":
				return new { pong = true, time = System.DateTime.UtcNow.ToString( "o" ) };
			case "refresh_schema":
				SchemaExporter.ExportSchemaSilent();
				return new { ok = true };
			case "get_log_path":
				return new { path = ResolveLogPath() };
			case "search_assets":
				return AssetSearcher.Search( req );
			case "install_package":
				return PackageInstaller.Install( req );
			case "probe_asset_apis":
				return ProbeAssetApis();
			case "probe_type":
				return ProbeType( req );
			case "probe_url_strings":
				return ProbeUrlStrings();
			case "probe_find_async":
				return ProbeFindAsync( req );
			default:
				throw new System.InvalidOperationException( $"unknown action: {action}" );
		}
	}

	static object ProbeType( JsonElement req )
	{
		var name = req.TryGetProperty( "name", out var el ) ? el.GetString() : null;
		if ( string.IsNullOrEmpty( name ) ) throw new System.InvalidOperationException( "missing 'name'" );
		System.Type type = null;
		foreach ( var asm in System.AppDomain.CurrentDomain.GetAssemblies() )
		{
			type = asm.GetType( name, throwOnError: false );
			if ( type != null ) break;
		}
		if ( type == null ) return new { found = false };
		var members = new List<string>();
		var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
		foreach ( var m in type.GetMethods( flags ) )
		{
			if ( m.IsSpecialName ) continue;
			members.Add( $"M{(m.IsStatic ? "[static]" : "")}: {m.Name}({string.Join(",", System.Linq.Enumerable.Select(m.GetParameters(), p => p.ParameterType.Name))}) -> {m.ReturnType.Name}" );
		}
		foreach ( var p in type.GetProperties( flags ) )
		{
			members.Add( $"P{(p.GetMethod?.IsStatic ?? false ? "[static]" : "")}: {p.Name}: {p.PropertyType.Name}" );
		}
		foreach ( var f in type.GetFields( flags ) )
		{
			members.Add( $"F{(f.IsStatic ? "[static]" : "")}: {f.Name}: {f.FieldType.Name}" );
		}
		return new { found = true, full_name = type.FullName, base_type = type.BaseType?.FullName, members };
	}

	static object ProbeFindAsync( JsonElement req )
	{
		var query = req.TryGetProperty( "query", out var q ) ? q.GetString() : "pirate";
		System.Type packageType = null;
		foreach ( var asm in System.AppDomain.CurrentDomain.GetAssemblies() )
		{
			packageType = asm.GetType( "Sandbox.Package", false );
			if ( packageType != null ) break;
		}
		if ( packageType == null ) return new { error = "Sandbox.Package not found" };

		// Find FindAsync(string, int, int, CancellationToken)
		var find = packageType.GetMethod( "FindAsync",
			BindingFlags.Public | BindingFlags.Static,
			null,
			new[] { typeof( string ), typeof( int ), typeof( int ), typeof( System.Threading.CancellationToken ) },
			null );
		if ( find == null ) return new { error = "FindAsync(string,int,int,ct) not found" };

		var task = find.Invoke( null, new object[] { query, 5, 0, default( System.Threading.CancellationToken ) } );
		if ( task == null ) return new { error = "FindAsync returned null" };

		var taskType = task.GetType();
		var info = new SortedDictionary<string, object>
		{
			["task_type"] = taskType.FullName,
			["return_signature"] = find.ReturnType.FullName,
		};

		// Try GetAwaiter().GetResult() — handles Task and ValueTask
		try
		{
			var awaiter = task.GetType().GetMethod( "GetAwaiter" )?.Invoke( task, null );
			if ( awaiter == null )
			{
				info["awaiter_found"] = false;
				return info;
			}
			var getResult = awaiter.GetType().GetMethod( "GetResult" );
			if ( getResult == null )
			{
				info["get_result_found"] = false;
				return info;
			}
			var result = getResult.Invoke( awaiter, null );
			if ( result == null )
			{
				info["result"] = "null";
				return info;
			}
			info["result_type"] = result.GetType().FullName;
			// Dump top-level shape
			var members = new List<string>();
			foreach ( var p in result.GetType().GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
			{
				try
				{
					var v = p.GetValue( result );
					var summary = v == null ? "null" : v.GetType().Name;
					if ( v is System.Collections.ICollection col ) summary += $"(count={col.Count})";
					members.Add( $"{p.Name}: {summary}" );
				}
				catch ( System.Exception ex ) { members.Add( $"{p.Name}: <error: {ex.Message}>" ); }
			}
			info["members"] = members;
			// If result IS enumerable, sample first item
			if ( result is System.Collections.IEnumerable e )
			{
				var sampled = new List<string>();
				int n = 0;
				foreach ( var item in e )
				{
					if ( item == null ) { sampled.Add( "null" ); continue; }
					var t = item.GetType();
					var title = t.GetProperty( "Title" )?.GetValue( item );
					var ident = t.GetProperty( "FullIdent" )?.GetValue( item );
					sampled.Add( $"{t.Name}: title={title} ident={ident}" );
					if ( ++n >= 3 ) break;
				}
				info["enumerable_sample"] = sampled;
			}
		}
		catch ( System.Exception ex )
		{
			info["await_error"] = ex.GetType().FullName + ": " + ex.Message;
			if ( ex.InnerException != null ) info["inner"] = ex.InnerException.Message;
		}
		return info;
	}

	static object ProbeUrlStrings()
	{
		var hits = new List<object>();
		var seen = new HashSet<string>();
		foreach ( var asm in System.AppDomain.CurrentDomain.GetAssemblies() )
		{
			System.Type[] types;
			try { types = asm.GetTypes(); }
			catch { continue; }
			foreach ( var t in types )
			{
				foreach ( var f in t.GetFields( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static ) )
				{
					if ( f.FieldType != typeof( string ) ) continue;
					try
					{
						var v = (string)f.GetValue( null );
						if ( v == null ) continue;
						if ( !v.Contains( "sbox.game" ) && !v.Contains( "asset.party" )
							&& !v.Contains( "facepunch.com" ) && !v.Contains( "/api/" ) ) continue;
						var key = $"{t.FullName}.{f.Name}";
						if ( seen.Contains( key ) ) continue;
						seen.Add( key );
						hits.Add( new { type = t.FullName, field = f.Name, value = v } );
					}
					catch { }
				}
			}
		}
		return new { count = hits.Count, urls = hits };
	}

	static object ProbeAssetApis()
	{
		var hits = new List<object>();
		foreach ( var asm in System.AppDomain.CurrentDomain.GetAssemblies() )
		{
			System.Type[] types;
			try { types = asm.GetTypes(); }
			catch { continue; }
			foreach ( var t in types )
			{
				var n = t.FullName ?? "";
				if ( n.Contains( "Cloud" ) || n.Contains( "Online" )
					|| n.Contains( "AssetBrowser" ) || n.EndsWith( "AssetSystem" )
					|| n.EndsWith( ".Asset" ) || n.Contains( "AssetMount" )
					|| n.Contains( "AssetSource" ) || n.Contains( "AssetLibrary" ) )
				{
					var staticMembers = new List<string>();
					foreach ( var m in t.GetMethods( BindingFlags.Public | BindingFlags.Static ) )
					{
						if ( m.DeclaringType != t ) continue;
						staticMembers.Add( $"M:{m.Name}({string.Join(",", System.Linq.Enumerable.Select(m.GetParameters(), p => p.ParameterType.Name))})" );
					}
					foreach ( var p in t.GetProperties( BindingFlags.Public | BindingFlags.Static ) )
					{
						if ( p.DeclaringType != t ) continue;
						staticMembers.Add( $"P:{p.Name}:{p.PropertyType.Name}" );
					}
					hits.Add( new { full_name = n, assembly = asm.GetName().Name, members = staticMembers } );
				}
			}
		}
		return new { count = hits.Count, types = hits };
	}

	static string ResolveLogPath()
	{
		// s&box writes to <install>/logs/sbox-dev.log. Find it by walking up
		// from the running assembly's base directory.
		var dir = new DirectoryInfo( System.AppDomain.CurrentDomain.BaseDirectory );
		for ( int i = 0; i < 8 && dir != null; i++ )
		{
			var candidate = Path.Combine( dir.FullName, "logs", "sbox-dev.log" );
			if ( File.Exists( candidate ) ) return candidate;
			dir = dir.Parent;
		}
		return null;
	}

	static void WriteResponse( string id, bool ok, object result, string error )
	{
		if ( string.IsNullOrEmpty( id ) ) return;
		var dir = IpcDir();
		Directory.CreateDirectory( dir );
		var resPath = Path.Combine( dir, $"res-{id}.json" );
		var payload = new SortedDictionary<string, object>
		{
			["id"] = id,
			["ok"] = ok,
		};
		if ( result != null ) payload["result"] = result;
		if ( error != null ) payload["error"] = error;
		var json = JsonSerializer.Serialize( payload );
		var tmp = $"{resPath}.tmp";
		File.WriteAllText( tmp, json, _utf8NoBom );
		if ( File.Exists( resPath ) ) File.Delete( resPath );
		File.Move( tmp, resPath );
	}
}
