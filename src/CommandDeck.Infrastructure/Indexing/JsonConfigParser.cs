using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CommandDeck;

internal sealed class JsonConfigParser : IStructuredConfigParser
{
    private static readonly string[] SemanticNameProperties = ["name", "id", "title"];

    public static JsonConfigParser Instance { get; } = new( );

    public IReadOnlyList<ConfigEntry> Parse( string text )
    {
        var utf8 = Encoding.UTF8.GetBytes( text );
        try
        {
            Utf8JsonReader reader = new( utf8, new JsonReaderOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            } );
            if ( !reader.Read( ) )
            {
                return [];
            }

            var root = ReadNode( ref reader, utf8 );
            List<ConfigEntry> entries = [];
            Flatten( root, string.Empty, root.TokenOffset, entries, new Utf8SourceMap( utf8 ) );
            return entries;
        }
        catch ( JsonException )
        {
            return [];
        }
        catch ( InvalidOperationException )
        {
            return [];
        }
    }

    private static JsonNode ReadNode( ref Utf8JsonReader reader, byte[] utf8 ) =>
        reader.TokenType switch
        {
            JsonTokenType.StartObject => ReadObject( ref reader, utf8 ),
            JsonTokenType.StartArray => ReadArray( ref reader, utf8 ),
            JsonTokenType.String => JsonNode.Scalar(
                reader.TokenStartIndex,
                reader.GetString( ) ?? string.Empty ),
            JsonTokenType.Number => JsonNode.Scalar(
                reader.TokenStartIndex,
                RawToken( ref reader, utf8 ) ),
            JsonTokenType.True => JsonNode.Scalar( reader.TokenStartIndex, "true" ),
            JsonTokenType.False => JsonNode.Scalar( reader.TokenStartIndex, "false" ),
            JsonTokenType.Null => JsonNode.Scalar( reader.TokenStartIndex, "null" ),
            _ => throw new JsonException( $"Unexpected JSON token {reader.TokenType}." )
        };

    private static JsonNode ReadObject( ref Utf8JsonReader reader, byte[] utf8 )
    {
        var offset = reader.TokenStartIndex;
        List<JsonPropertyNode> properties = [];
        while ( reader.Read( ) && reader.TokenType != JsonTokenType.EndObject )
        {
            if ( reader.TokenType != JsonTokenType.PropertyName )
            {
                throw new JsonException( "Expected a JSON property name." );
            }

            var name = reader.GetString( ) ?? string.Empty;
            var keyOffset = reader.TokenStartIndex;
            if ( !reader.Read( ) )
            {
                throw new JsonException( "Expected a JSON property value." );
            }

            properties.Add( new JsonPropertyNode( name, keyOffset, ReadNode( ref reader, utf8 ) ) );
        }

        return JsonNode.Object( offset, properties );
    }

    private static JsonNode ReadArray( ref Utf8JsonReader reader, byte[] utf8 )
    {
        var offset = reader.TokenStartIndex;
        List<JsonNode> elements = [];
        while ( reader.Read( ) && reader.TokenType != JsonTokenType.EndArray )
        {
            elements.Add( ReadNode( ref reader, utf8 ) );
        }

        return JsonNode.Array( offset, elements );
    }

    private static string RawToken( ref Utf8JsonReader reader, byte[] utf8 )
    {
        var start = checked(( int ) reader.TokenStartIndex);
        var length = checked(( int ) (reader.BytesConsumed - reader.TokenStartIndex));
        return Encoding.UTF8.GetString( utf8, start, length );
    }

    private static void Flatten(
        JsonNode node,
        string prefix,
        long keyOffset,
        List<ConfigEntry> entries,
        Utf8SourceMap sourceMap )
    {
        if ( node.Properties is not null )
        {
            foreach ( var property in node.Properties )
            {
                var path = prefix.Length == 0
                    ? property.Name
                    : $"{prefix}.{property.Name}";
                Flatten( property.Value, path, property.KeyOffset, entries, sourceMap );
            }

            return;
        }

        if ( node.Elements is not null )
        {
            var index = 0;
            HashSet<string> usedSegments = new( StringComparer.Ordinal );
            foreach ( var element in node.Elements )
            {
                var segment = BuildArraySegment( element, index, usedSegments );
                Flatten( element, $"{prefix}[{segment}]", element.TokenOffset, entries, sourceMap );
                index++;
            }

            return;
        }

        var key = sourceMap.Locate( keyOffset );
        var value = sourceMap.Locate( node.TokenOffset );
        entries.Add( new ConfigEntry
        {
            Key = prefix,
            Value = StructuredConfigParserSupport.Truncate( node.ScalarValue ?? string.Empty ),
            Section = Parent( prefix ),
            KeyLine = key.Line,
            KeyColumn = key.Column,
            ValueLine = value.Line,
            ValueColumn = value.Column
        } );
    }

    private static string BuildArraySegment(
        JsonNode element,
        int index,
        HashSet<string> usedSegments )
    {
        var fallback = index.ToString( CultureInfo.InvariantCulture );
        var semanticName = GetSemanticArrayElementName( element );
        if ( semanticName is not null && usedSegments.Add( semanticName ) )
        {
            return semanticName;
        }

        var candidate = fallback;
        var suffix = 2;
        while ( !usedSegments.Add( candidate ) )
        {
            candidate = $"{fallback}#{suffix.ToString( CultureInfo.InvariantCulture )}";
            suffix++;
        }

        return candidate;
    }

    private static string? GetSemanticArrayElementName( JsonNode element )
    {
        if ( element.Properties is null )
        {
            return null;
        }

        foreach ( var propertyName in SemanticNameProperties )
        {
            var property = element.Properties.FirstOrDefault( candidate =>
                string.Equals( candidate.Name, propertyName, StringComparison.Ordinal ) );
            if ( property?.Value.ScalarValue is { Length: > 0 } value )
            {
                return value.Trim( )
                    .Replace( "\\", "\\\\", StringComparison.Ordinal )
                    .Replace( "]", "\\]", StringComparison.Ordinal );
            }
        }

        return null;
    }

    private static string Parent( string value )
    {
        var dot = value.LastIndexOf( '.' );
        return dot > 0 ? value[..dot] : string.Empty;
    }

    private sealed record JsonPropertyNode( string Name, long KeyOffset, JsonNode Value );

    private sealed class JsonNode
    {
        private JsonNode( long tokenOffset ) => TokenOffset = tokenOffset;

        public long TokenOffset { get; }
        public string? ScalarValue { get; private init; }
        public IReadOnlyList<JsonPropertyNode>? Properties { get; private init; }
        public IReadOnlyList<JsonNode>? Elements { get; private init; }

        public static JsonNode Scalar( long offset, string value ) =>
            new( offset )
            {
                ScalarValue = value
            };

        public static JsonNode Object( long offset, IReadOnlyList<JsonPropertyNode> properties ) =>
            new( offset )
            {
                Properties = properties
            };

        public static JsonNode Array( long offset, IReadOnlyList<JsonNode> elements ) =>
            new( offset )
            {
                Elements = elements
            };
    }

    private sealed class Utf8SourceMap
    {
        private readonly byte[] _utf8;
        private readonly int[] _lineStarts;

        public Utf8SourceMap( byte[] utf8 )
        {
            _utf8 = utf8;
            List<int> starts = [0];
            for ( var index = 0; index < utf8.Length; index++ )
            {
                if ( utf8[index] == ( byte ) '\n' )
                {
                    starts.Add( index + 1 );
                }
            }

            _lineStarts = [.. starts];
        }

        public SourcePosition Locate( long offset )
        {
            var byteOffset = Math.Clamp( checked(( int ) offset), 0, _utf8.Length );
            var lineIndex = Array.BinarySearch( _lineStarts, byteOffset );
            if ( lineIndex < 0 )
            {
                lineIndex = ~lineIndex - 1;
            }

            var column = Encoding.UTF8.GetCharCount(
                _utf8.AsSpan( _lineStarts[lineIndex], byteOffset - _lineStarts[lineIndex] ) ) + 1;
            return new SourcePosition( lineIndex + 1, column );
        }
    }

    private readonly record struct SourcePosition( int Line, int Column );
}
