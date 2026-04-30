using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace GrappleShip.EditorTools;

/// <summary>
/// Minimal file-drop IPC for the GrappleShip MCP server. The MCP writes
/// req-&lt;id&gt;.json into &lt;tmp&gt;/grappleship-bridge/, the bridge processes one
/// request per editor frame on the main thread, and writes res-&lt;id&gt;.json
/// back. Used for things that genuinely need the editor (schema export,
/// scene reloads, button presses) — everything else lives in the static
/// MCP tooling.
/// </summary>
public static class EditorBridge
{
	const string IpcDirName = "grappleship-bridge";

	static readonly ConcurrentQueue<string> _incoming = new();
	static readonly UTF8Encoding _utf8NoBom = new( false );
	static Timer _scanner;
	static readonly object _scanLock = new();
	static readonly HashSet<string> _seen = new();
	static bool _running;

	static EditorBridge()
	{
		Start();
	}

	[Menu( "Editor", "GrappleShip/Restart Bridge" )]
	public static void RestartFromMenu()
	{
		_running = false;
		_scanner?.Dispose();
		_scanner = null;
		_seen.Clear();
		while ( _incoming.TryDequeue( out _ ) ) { }
		Start();
		EditorUtility.DisplayDialog( "Bridge restarted", $"Watching {IpcDir()}" );
	}

	public static void Start()
	{
		if ( _running ) return;
		_running = true;
		var dir = IpcDir();
		Directory.CreateDirectory( dir );
		_scanner = new Timer( _ => Scan(), null, 0, 100 );
		Log.Info( $"[EditorBridge] watching {dir}" );
	}

	static string IpcDir()
	{
		return Path.Combine( Path.GetTempPath(), IpcDirName );
	}

	static void Scan()
	{
		lock ( _scanLock )
		{
			try
			{
				var dir = IpcDir();
				if ( !Directory.Exists( dir ) ) return;
				foreach ( var path in Directory.EnumerateFiles( dir, "req-*.json" ) )
				{
					if ( _seen.Contains( path ) ) continue;
					_seen.Add( path );
					_incoming.Enqueue( path );
				}
			}
			catch ( System.Exception e )
			{
				Log.Warning( $"[EditorBridge] scan error: {e.Message}" );
			}
		}
	}

	[EditorEvent.Frame]
	public static void OnFrame()
	{
		if ( !_running ) Start();
		// Process one request per frame to avoid stalling the editor.
		if ( !_incoming.TryDequeue( out var path ) ) return;
		HandleRequest( path );
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
			_seen.Remove( path );
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
			default:
				throw new System.InvalidOperationException( $"unknown action: {action}" );
		}
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
		File.Move( tmp, resPath, overwrite: true );
	}
}
