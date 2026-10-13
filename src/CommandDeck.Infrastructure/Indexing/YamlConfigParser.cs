namespace CommandDeck;

internal sealed class YamlConfigParser : IStructuredConfigParser
{
    public static YamlConfigParser Instance { get; } = new( );

    public IReadOnlyList<ConfigEntry> Parse( string text )
    {
        var lines = StructuredConfigParserSupport.Lines( text );
        List<ConfigEntry> entries = [];
        List<(int Indent, string Key)> parents = [];
        for ( var index = 0; index < lines.Length; index++ )
        {
            var source = lines[index];
            var trimmed = source.Trimmed;
            if ( trimmed.Length == 0 || trimmed.StartsWith( '#' ) || trimmed == "---" ||
                trimmed == "..." )
            {
                continue;
            }

            var indent = CountIndent( source.Text );
            var candidate = trimmed.StartsWith( "- ", StringComparison.Ordinal )
                ? trimmed[2..].TrimStart( )
                : trimmed;
            var separator = StructuredConfigParserSupport.FindUnquoted( candidate, ':' );
            if ( separator <= 0 )
            {
                continue;
            }

            while ( parents.Count > 0 && parents[^1].Indent >= indent )
            {
                parents.RemoveAt( parents.Count - 1 );
            }

            var key = StructuredConfigParserSupport.StripQuoted( candidate[..separator].Trim( ) );
            var rawValue = candidate[(separator + 1)..].TrimStart( );
            if ( rawValue.Length == 0 )
            {
                parents.Add( (indent, key) );
                continue;
            }

            var keyIndex = source.Text.IndexOf( key, StringComparison.Ordinal );
            var valueIndex = source.Text.IndexOf( rawValue, Math.Max( 0, keyIndex + key.Length ),
                StringComparison.Ordinal );
            var value = rawValue is "|" or ">"
                ? ReadBlockScalar( lines, ref index, indent, rawValue == ">" )
                : StructuredConfigParserSupport.StripInlineComment( rawValue, '#' );
            var section = string.Join( '.', parents.Select( static parent => parent.Key ) );
            entries.Add( StructuredConfigParserSupport.Entry(
                key,
                StructuredConfigParserSupport.StripQuoted( value ),
                section,
                source,
                keyIndex,
                valueIndex,
                rawValue is "|" or ">" ? null : source.Trimmed ) );
        }

        return entries;
    }

    private static string ReadBlockScalar(
        ConfigSourceLine[] lines,
        ref int index,
        int parentIndent,
        bool folded )
    {
        List<string> values = [];
        while ( index + 1 < lines.Length )
        {
            var next = lines[index + 1];
            if ( next.Trimmed.Length > 0 && CountIndent( next.Text ) <= parentIndent )
            {
                break;
            }

            index++;
            values.Add( next.Trimmed );
        }

        return string.Join( folded ? " " : "\n", values );
    }

    private static int CountIndent( string line )
    {
        var index = 0;
        while ( index < line.Length && line[index] == ' ' )
        {
            index++;
        }

        return index;
    }
}
