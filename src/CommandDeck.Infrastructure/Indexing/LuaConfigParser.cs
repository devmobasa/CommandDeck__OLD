namespace CommandDeck;

internal sealed class LuaConfigParser : IStructuredConfigParser
{
    // Table fields end with a separator that is not part of the value.
    private const char FieldSeparator = ',';
    private const char AlternateFieldSeparator = ';';

    public static LuaConfigParser Instance { get; } = new( );

    public IReadOnlyList<ConfigEntry> Parse( string text )
    {
        var lines = StructuredConfigParserSupport.Lines( text );
        List<ConfigEntry> entries = [];
        for ( var index = 0; index < lines.Length; index++ )
        {
            var source = lines[index];
            var trimmed = source.Trimmed;
            if ( trimmed.Length == 0 || trimmed.StartsWith( "--", StringComparison.Ordinal ) )
            {
                continue;
            }

            var assignment = trimmed.StartsWith( "local ", StringComparison.Ordinal )
                ? trimmed[6..].TrimStart( )
                : trimmed;
            var separator = StructuredConfigParserSupport.FindUnquoted( assignment, '=' );
            if ( separator <= 0 )
            {
                continue;
            }

            var key = assignment[..separator].Trim( );
            if ( !IsLuaIdentifierPath( key ) )
            {
                continue;
            }

            var rawValue = assignment[(separator + 1)..].TrimStart( );
            var keyIndex = source.Text.IndexOf( key, StringComparison.Ordinal );
            var valueIndex = source.Text.IndexOf( rawValue, Math.Max( 0, keyIndex + key.Length ),
                StringComparison.Ordinal );
            var value = ReadLongString( lines, ref index, rawValue );
            value = StripFieldSeparator( StripLuaComment( value ) );
            if ( value.Length == 0 )
            {
                continue;
            }

            var dot = key.LastIndexOf( '.' );
            entries.Add( StructuredConfigParserSupport.Entry(
                key,
                StripLuaString( value ),
                dot > 0 ? key[..dot] : null,
                source,
                keyIndex,
                valueIndex,
                rawValue.StartsWith( "[[", StringComparison.Ordinal ) ||
                    rawValue.StartsWith( "[=", StringComparison.Ordinal )
                        ? null
                        : source.Trimmed ) );
        }

        return entries;
    }

    private static bool IsLuaIdentifierPath( string value ) =>
        value.Split( '.' ).All( static segment =>
            segment.Length > 0 &&
            (char.IsLetter( segment[0] ) || segment[0] == '_') &&
            segment.Skip( 1 ).All( static character =>
                char.IsLetterOrDigit( character ) || character == '_' ) );

    private static string ReadLongString(
        ConfigSourceLine[] lines,
        ref int index,
        string initial )
    {
        if ( !initial.StartsWith( "[[", StringComparison.Ordinal ) ||
            initial[2..].Contains( "]]", StringComparison.Ordinal ) )
        {
            return initial;
        }

        List<string> values = [initial];
        while ( ++index < lines.Length )
        {
            values.Add( lines[index].Text );
            if ( lines[index].Text.Contains( "]]", StringComparison.Ordinal ) )
            {
                break;
            }
        }

        return string.Join( '\n', values );
    }

    private static string StripLuaComment( string value )
    {
        var comment = value.IndexOf( " --", StringComparison.Ordinal );
        return comment < 0 ? value.TrimEnd( ) : value[..comment].TrimEnd( );
    }

    private static string StripFieldSeparator( string value )
    {
        if ( value.Length < 2 || value[^1] is not (FieldSeparator or AlternateFieldSeparator) )
        {
            return value;
        }

        return value[..^1].TrimEnd( );
    }

    private static string StripLuaString( string value )
    {
        var trimmed = value.Trim( );
        if ( trimmed.StartsWith( "[[", StringComparison.Ordinal ) &&
            trimmed.EndsWith( "]]", StringComparison.Ordinal ) && trimmed.Length >= 4 )
        {
            return trimmed[2..^2].Trim( '\r', '\n' );
        }

        return StructuredConfigParserSupport.StripQuoted( trimmed );
    }
}
