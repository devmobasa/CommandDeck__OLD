using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandDeck.Specs;

public static partial class Program
{
    private const string ConfigSchemaRelativePath = "schemas/commanddeck.schema.json";
    private static readonly NullabilityInfoContext SchemaNullability = new( );

    private static readonly JsonSerializerOptions SchemaJsonOptions = new( )
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static void GeneratedConfigSchemaMatchesOptionsModel( )
    {
        var root = FindRepositoryRoot( );
        var built = BuildConfigSchemaArtifact( );
        Equal(
            NormalizeArtifact( built ),
            NormalizeArtifact( File.ReadAllText( Path.Combine( root, ConfigSchemaRelativePath ) ) ) );

        var schema = JsonNode.Parse( built );
        if ( schema is null )
        {
            throw new InvalidOperationException( "Generated config schema was empty." );
        }

        foreach ( var option in EnumerateOptions( ) )
        {
            True(
                SchemaContainsOptionPath( schema, option.Path ),
                $"schema missing option path {option.Path}" );
        }

        var examplePath = Path.Combine( root, "config/commanddeck.example.jsonc" );
        var exampleJson = File.ReadAllText( examplePath );
        True(
            exampleJson.Contains( "\"$schema\"", StringComparison.Ordinal ),
            "example config must hint at the generated schema" );
        var example = JsonNode.Parse(
                          exampleJson,
                          documentOptions: new JsonDocumentOptions
                          {
                              CommentHandling = JsonCommentHandling.Skip,
                              AllowTrailingCommas = true
                          } );
        if ( example is null )
        {
            throw new InvalidOperationException( "Example config was empty." );
        }

        AssertInstanceMatchesSchema(
            schema,
            example,
            path: "$" );

        var codexPathSchema = schema["properties"]?["agent"]?["properties"]?["codexBinaryPath"];
        True(
            SchemaAllowsNull( codexPathSchema ),
            "agent.codexBinaryPath is string? and must accept JSON null" );
        var nullableAgent = JsonNode.Parse( """{"agent":{"codexBinaryPath":null}}""" );
        if ( nullableAgent is null )
        {
            throw new InvalidOperationException( "nullable agent fixture was empty." );
        }

        AssertInstanceMatchesSchema( schema, nullableAgent, path: "$" );

        var quicklinkItems = schema["properties"]?["quicklinks"]?["items"];
        True(
            SchemaRequires( quicklinkItems, "id", "title", "urlTemplate" ),
            "quicklink objects require id, title, and urlTemplate" );
        try
        {
            var emptyQuicklink = JsonNode.Parse( """{"quicklinks":[{}]}""" );
            if ( emptyQuicklink is null )
            {
                throw new InvalidOperationException( "empty quicklink fixture was empty." );
            }

            AssertInstanceMatchesSchema( schema, emptyQuicklink, path: "$" );
            throw new InvalidOperationException( "An empty quicklink object must fail the generated schema." );
        }
        catch ( InvalidOperationException exception ) when (
            exception.Message.Contains( "quicklinks[0].id", StringComparison.Ordinal ) )
        {
        }
    }

    private static void WriteConfigSchemaArtifact( string root )
    {
        var path = Path.Combine( root, ConfigSchemaRelativePath );
        Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        File.WriteAllText( path, BuildConfigSchemaArtifact( ) );
    }

    private static string BuildConfigSchemaArtifact( )
    {
        var root = BuildObjectSchema( typeof( CommandDeckOptions ) );
        var properties = root["properties"]?.AsObject( );
        if ( properties is null )
        {
            throw new InvalidOperationException( "Root config schema has no properties." );
        }

        JsonObject ordered = new( )
        {
            ["$schema"] = new JsonObject { ["type"] = "string" }
        };
        foreach ( (var name, var node) in properties )
        {
            ordered[name] = node?.DeepClone( );
        }

        JsonObject schema = new( )
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://local.commanddeck/schemas/commanddeck.schema.json",
            ["title"] = "CommandDeck configuration",
            ["description"] =
                "JSONC configuration for the CommandDeck daemon. Comments and trailing commas are allowed. The runtime ignores unknown properties; this schema flags them so editors can catch typos.",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = ordered
        };

        return schema.ToJsonString( SchemaJsonOptions ) + Environment.NewLine;
    }

    private static JsonObject BuildObjectSchema( Type ownerType )
    {
        JsonObject properties = [];
        JsonArray required = [];
        foreach ( var property in ownerType.GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName( property.Name );
            properties[name] = BuildPropertySchema( property );
            if ( property.GetCustomAttribute<RequiredMemberAttribute>( ) is not null )
            {
                required.Add( name );
            }
        }

        JsonObject schema = new( )
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties
        };
        if ( required.Count > 0 )
        {
            schema["required"] = required;
        }

        return schema;
    }

    private static JsonObject BuildPropertySchema( PropertyInfo property )
    {
        var schema = BuildTypeSchema( property.PropertyType, property );
        if ( property.DeclaringType == typeof( CalculatorOptions ) &&
            property.Name == nameof( CalculatorOptions.UseQalc ) )
        {
            schema["enum"] = new JsonArray(
                CalculatorOptions.UseQalcAuto,
                CalculatorOptions.UseQalcNever,
                CalculatorOptions.UseQalcAlways );
        }

        return schema;
    }

    private static JsonObject BuildTypeSchema( Type type, PropertyInfo? property )
    {
        var nullableValue = Nullable.GetUnderlyingType( type );
        var effective = nullableValue ?? type;
        var schema = BuildNonNullTypeSchema( effective );
        var nullableReference = property is not null &&
                                !effective.IsValueType &&
                                SchemaNullability.Create( property ).ReadState == NullabilityState.Nullable;
        return nullableValue is not null || nullableReference ? WithNull( schema ) : schema;
    }

    private static JsonObject BuildNonNullTypeSchema( Type type )
    {
        if ( type == typeof( string ) )
        {
            return new JsonObject { ["type"] = "string" };
        }

        if ( type == typeof( bool ) )
        {
            return new JsonObject { ["type"] = "boolean" };
        }

        if ( type == typeof( int ) || type == typeof( long ) )
        {
            return new JsonObject { ["type"] = "integer" };
        }

        if ( type == typeof( double ) || type == typeof( float ) )
        {
            return new JsonObject { ["type"] = "number" };
        }

        if ( IsList( type ) )
        {
            var itemType = type.GetGenericArguments( )[0];
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = BuildTypeSchema( itemType, property: null )
            };
        }

        if ( type.IsGenericType && type.GetGenericTypeDefinition( ) == typeof( Dictionary<,> ) )
        {
            var valueType = type.GetGenericArguments( )[1];
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = BuildTypeSchema( valueType, property: null )
            };
        }

        var nested = NestedOptionType( type ) ??
                     (type.Namespace == typeof( CommandDeckOptions ).Namespace &&
                      type.Name.EndsWith( "Options", StringComparison.Ordinal )
                         ? type
                         : null);
        if ( nested is not null )
        {
            return BuildObjectSchema( nested );
        }

        return new JsonObject { ["type"] = "object" };
    }

    private static JsonObject WithNull( JsonObject schema )
    {
        if ( schema["type"] is JsonValue typeValue && typeValue.TryGetValue( out string? typeName ) )
        {
            schema["type"] = new JsonArray { typeName, "null" };
        }

        return schema;
    }

    private static bool SchemaContainsOptionPath( JsonNode schema, string path )
    {
        var current = schema;
        foreach ( var segment in path.Split( '.' ) )
        {
            var name = segment.EndsWith( "[]", StringComparison.Ordinal )
                ? segment[..^2]
                : segment;
            current = current?["properties"]?[name];
            if ( current is null )
            {
                return false;
            }

            if ( segment.EndsWith( "[]", StringComparison.Ordinal ) )
            {
                current = current["items"];
            }
        }

        return current is not null;
    }

    private static void AssertInstanceMatchesSchema( JsonNode schema, JsonNode instance, string path )
    {
        if ( IsJsonNull( instance ) )
        {
            AssertNullAllowed( schema, path );
            return;
        }

        if ( instance is not JsonObject instanceObject )
        {
            return;
        }

        AssertRequiredPropertiesPresent( schema, instanceObject, path );
        var additionalAllowed = AdditionalPropertiesAllowed( schema );
        var properties = schema["properties"]?.AsObject( );
        foreach ( (var name, var value) in instanceObject )
        {
            if ( properties is not null &&
                properties.TryGetPropertyValue( name, out var propertySchema ) &&
                propertySchema is not null )
            {
                AssertPropertyValueMatchesSchema( propertySchema, value, $"{path}.{name}" );
                continue;
            }

            if ( !additionalAllowed )
            {
                throw new InvalidOperationException(
                    $"Example config key {path}.{name} is not in the generated schema." );
            }
        }
    }

    private static void AssertRequiredPropertiesPresent( JsonNode schema, JsonObject instance, string path )
    {
        if ( schema["required"] is not JsonArray required )
        {
            return;
        }

        foreach ( var requiredNode in required )
        {
            var requiredName = requiredNode?.GetValue<string>( );
            if ( requiredName is null )
            {
                throw new InvalidOperationException( $"Schema required entry at {path} is not a string." );
            }

            if ( !instance.ContainsKey( requiredName ) )
            {
                throw new InvalidOperationException(
                    $"Example config missing required {path}.{requiredName}." );
            }
        }
    }

    private static bool AdditionalPropertiesAllowed( JsonNode schema ) =>
        schema["additionalProperties"] is JsonObject ||
        (schema["additionalProperties"] is JsonValue flag && flag.TryGetValue( out bool allowed ) && allowed);

    private static void AssertPropertyValueMatchesSchema( JsonNode schema, JsonNode? value, string path )
    {
        if ( value is null || IsJsonNull( value ) )
        {
            AssertNullAllowed( schema, path );
            return;
        }

        if ( value is JsonArray array )
        {
            var itemSchema = schema["items"] ?? schema;
            var index = 0;
            foreach ( var item in array )
            {
                if ( item is not null )
                {
                    AssertInstanceMatchesSchema( itemSchema, item, $"{path}[{index}]" );
                }

                index++;
            }

            return;
        }

        if ( value is JsonObject )
        {
            AssertInstanceMatchesSchema( schema, value, path );
        }
    }

    private static void AssertNullAllowed( JsonNode schema, string path )
    {
        if ( !SchemaAllowsNull( schema ) )
        {
            throw new InvalidOperationException( $"Schema rejects null at {path}." );
        }
    }

    private static bool IsJsonNull( JsonNode instance ) =>
        instance is JsonValue jsonValue && jsonValue.GetValueKind( ) == JsonValueKind.Null;

    private static bool SchemaAllowsNull( JsonNode? schema )
    {
        if ( schema?["type"] is JsonArray types )
        {
            foreach ( var type in types )
            {
                if ( type is JsonValue value && value.TryGetValue( out string? name ) &&
                    string.Equals( name, "null", StringComparison.Ordinal ) )
                {
                    return true;
                }
            }
        }

        return schema?["type"] is JsonValue single &&
            single.TryGetValue( out string? typeName ) &&
            string.Equals( typeName, "null", StringComparison.Ordinal );
    }

    private static bool SchemaRequires( JsonNode? schema, params string[] names )
    {
        if ( schema?["required"] is not JsonArray required )
        {
            return false;
        }

        HashSet<string> present = new( StringComparer.Ordinal );
        foreach ( var node in required )
        {
            if ( node is JsonValue value && value.TryGetValue( out string? name ) )
            {
                present.Add( name );
            }
        }

        return names.All( present.Contains );
    }
}
