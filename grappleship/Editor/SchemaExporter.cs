using System.IO;
using System.Reflection;
using System.Text.Json;

namespace GrappleShip.EditorTools;

/// <summary>
/// One-shot dumper: walks every Component subclass via TypeLibrary reflection
/// and writes a sorted JSON catalog to docs/sbox/builtin-types.json. Run this
/// after upgrading s&box (or when a new built-in component is needed). The
/// output is consumed by the GrappleShip MCP server in tools/mcp/.
/// </summary>
public static class SchemaExporter
{
	[Menu( "Editor", "GrappleShip/Refresh Built-in Type Schema" )]
	public static void ExportSchema()
	{
		var (path, written, total, skipped) = ExportSchemaCore();
		EditorUtility.DisplayDialog(
			"Schema Exported",
			$"{written} components written ({skipped} skipped of {total} total) to:\n{path}\n\nCommit this file to the repo." );
	}

	public static void ExportSchemaSilent()
	{
		ExportSchemaCore();
	}

	static (string path, int written, int total, int skipped) ExportSchemaCore()
	{
		var output = new SortedDictionary<string, object>();
		int total = 0, skipped = 0, written = 0;

		foreach ( var typeDesc in EditorTypeLibrary.GetTypes<Component>() )
		{
			total++;

			if ( typeDesc.TargetType == null ) { skipped++; continue; }
			if ( typeDesc.TargetType.IsAbstract ) { skipped++; continue; }
			if ( typeDesc.IsGenericType ) { skipped++; continue; }

			var fullName = typeDesc.FullName ?? typeDesc.TargetType.FullName;
			if ( string.IsNullOrEmpty( fullName ) ) { skipped++; continue; }

			// Skip our own GrappleShip components - the MCP parses .cs source
			// directly so it always reflects the current source of truth.
			if ( fullName.StartsWith( "GrappleShip." ) ) { skipped++; continue; }

			var props = new SortedDictionary<string, object>();
			var seenNames = new HashSet<string>();

			// Pass 1: every property TypeLibrary knows about (covers [Property]
			// fields including [Hide]'d ones — they still serialize).
			foreach ( var prop in typeDesc.Properties )
			{
				if ( seenNames.Contains( prop.Name ) ) continue;
				seenNames.Add( prop.Name );
				props[prop.Name] = DescribeProperty( typeDesc.TargetType, prop );
			}

			// Pass 2: also include any public read-write property reachable via
			// reflection (catches inherited / engine-internal members like
			// ModelRenderer.LodOverride that the scene serializer writes but
			// TypeLibrary may filter out).
			foreach ( var info in typeDesc.TargetType.GetProperties(
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy ) )
			{
				if ( seenNames.Contains( info.Name ) ) continue;
				if ( info.GetIndexParameters().Length > 0 ) continue;
				if ( !info.CanRead && !info.CanWrite ) continue;
				seenNames.Add( info.Name );
				props[info.Name] = DescribePropertyInfo( info );
			}

			output[fullName] = new SortedDictionary<string, object>
			{
				["title"] = typeDesc.Title ?? typeDesc.Name,
				["category"] = typeDesc.Group ?? "",
				["icon"] = typeDesc.Icon ?? "",
				["properties"] = props,
			};
			written++;
		}

		var json = JsonSerializer.Serialize( output, new JsonSerializerOptions
		{
			WriteIndented = true,
		} );

		var path = ResolveOutputPath();
		File.WriteAllText( path, json, new System.Text.UTF8Encoding( false ) );

		Log.Info( $"[SchemaExporter] {written} components written, {skipped} skipped of {total} total." );
		Log.Info( $"[SchemaExporter] Output: {path}" );
		return (path, written, total, skipped);
	}

	static SortedDictionary<string, object> DescribeProperty( System.Type ownerType, PropertyDescription prop )
	{
		var entry = new SortedDictionary<string, object>
		{
			["type"] = MapType( prop.PropertyType ),
		};

		var attrs = GetAttributes( ownerType, prop.Name );

		// Range: read Min/Max generically (covers RangeAttribute + variants).
		var range = FindAttribute( attrs, "RangeAttribute" );
		if ( range != null )
		{
			var min = ReadNumber( range, "Min" );
			var max = ReadNumber( range, "Max" );
			if ( min.HasValue && max.HasValue )
			{
				entry["range"] = new[] { min.Value, max.Value };
			}
		}

		// Group: read whatever string field exposes the group name.
		var group = FindAttribute( attrs, "GroupAttribute" );
		if ( group != null )
		{
			var name = ReadFirstString( group );
			if ( !string.IsNullOrEmpty( name ) ) entry["group"] = name;
		}

		if ( FindAttribute( attrs, "ReadOnlyAttribute" ) != null )
		{
			entry["readonly"] = true;
		}

		if ( prop.PropertyType.IsEnum )
		{
			entry["values"] = System.Enum.GetNames( prop.PropertyType );
		}

		return entry;
	}

	static SortedDictionary<string, object> DescribePropertyInfo( PropertyInfo info )
	{
		var entry = new SortedDictionary<string, object>
		{
			["type"] = MapType( info.PropertyType ),
		};
		if ( info.PropertyType.IsEnum )
		{
			entry["values"] = System.Enum.GetNames( info.PropertyType );
		}
		return entry;
	}

	static object[] GetAttributes( System.Type ownerType, string propName )
	{
		if ( ownerType == null ) return System.Array.Empty<object>();
		var info = ownerType.GetProperty( propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
		if ( info == null ) return System.Array.Empty<object>();
		return info.GetCustomAttributes( true );
	}

	static object FindAttribute( object[] attrs, string typeName )
	{
		foreach ( var attr in attrs )
		{
			if ( attr.GetType().Name == typeName ) return attr;
		}
		return null;
	}

	static float? ReadNumber( object obj, string name )
	{
		var t = obj.GetType();
		var p = t.GetProperty( name );
		object raw = null;
		if ( p != null ) raw = p.GetValue( obj );
		else
		{
			var f = t.GetField( name );
			if ( f != null ) raw = f.GetValue( obj );
		}
		if ( raw == null ) return null;
		try { return System.Convert.ToSingle( raw ); }
		catch { return null; }
	}

	static string ReadFirstString( object obj )
	{
		var t = obj.GetType();
		foreach ( var p in t.GetProperties() )
		{
			if ( p.PropertyType == typeof( string ) )
			{
				var v = (string)p.GetValue( obj );
				if ( !string.IsNullOrEmpty( v ) ) return v;
			}
		}
		foreach ( var f in t.GetFields() )
		{
			if ( f.FieldType == typeof( string ) )
			{
				var v = (string)f.GetValue( obj );
				if ( !string.IsNullOrEmpty( v ) ) return v;
			}
		}
		return null;
	}

	static string MapType( System.Type t )
	{
		if ( t == null ) return "unknown:null";
		if ( t == typeof( bool ) ) return "bool";
		if ( t == typeof( int ) || t == typeof( uint ) || t == typeof( long ) || t == typeof( ulong ) ) return "int";
		if ( t == typeof( float ) || t == typeof( double ) ) return "float";
		if ( t == typeof( string ) ) return "string";
		if ( t == typeof( Vector3 ) ) return "vector3";
		if ( t == typeof( Vector2 ) ) return "vector2";
		if ( t == typeof( Rotation ) ) return "rotation";
		if ( t == typeof( Color ) ) return "color";
		if ( t == typeof( Angles ) ) return "angles";
		if ( t.IsEnum )
		{
			// Flags enums serialize as either integers OR named member strings
			// depending on the engine version / property. Tag permissively so
			// the validator accepts both.
			if ( t.IsDefined( typeof( System.FlagsAttribute ), false ) ) return $"unknown:flags:{t.Name}";
			return "enum";
		}
		if ( typeof( Component ).IsAssignableFrom( t ) ) return $"component_ref:{t.Name}";
		if ( t == typeof( GameObject ) ) return "gameobject_ref";
		if ( t.IsGenericType ) return $"generic:{t.Name}";
		return $"unknown:{t.FullName ?? t.Name}";
	}

	static string ResolveOutputPath()
	{
		// Walk up from the editor's content root looking for the marker file
		// (.mcp.json at the repo root). This is more robust than guessing
		// the directory layout — we landed in <repo>/grappleship/docs/sbox/
		// the first time because Content.GetFullPath was deeper than we
		// expected.
		var start = Editor.FileSystem.Content.GetFullPath( "/" );
		var dir = new DirectoryInfo( start );
		for ( int i = 0; i < 8 && dir != null; i++ )
		{
			if ( File.Exists( Path.Combine( dir.FullName, ".mcp.json" ) ) )
			{
				var outDir = Path.Combine( dir.FullName, "docs", "sbox" );
				Directory.CreateDirectory( outDir );
				return Path.Combine( outDir, "builtin-types.json" );
			}
			dir = dir.Parent;
		}
		// Fallback: write next to the addon and log loudly.
		Log.Warning( "[SchemaExporter] Could not find .mcp.json marker; writing next to addon." );
		var fallback = Path.Combine( start, "docs", "sbox" );
		Directory.CreateDirectory( fallback );
		return Path.Combine( fallback, "builtin-types.json" );
	}
}
