using System.IO;
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
		if ( File.Exists( resPath ) ) File.Delete( resPath );
		File.Move( tmp, resPath );
	}
}
