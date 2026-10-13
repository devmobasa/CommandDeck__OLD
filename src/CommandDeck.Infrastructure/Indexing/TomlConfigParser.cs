namespace CommandDeck;

internal sealed class TomlConfigParser : IStructuredConfigParser
{
    public static TomlConfigParser Instance { get; } = new( );

    public IReadOnlyList<ConfigEntry> Parse( string text )
    {
        var lines = StructuredConfigParserSupport.Lines( text );
        List<ConfigEntry> entries = [];
        string? table = null;
        for ( var index = 0; index < lines.Length; index++ )
        {
            var source = lines[index];
            var trimmed = source.Trimmed;
            if ( trimmed.Length == 0 || trimmed.StartsWith( '#' ) )
            {
                continue;
            }

            if ( TryReadTable( trimmed, out var parsedTable ) )
            {
                table = parsedTable;
                continue;
            }

            var separator = StructuredConfigParserSupport.FindUnquoted( trimmed, '=' );
            if ( separator <= 0 )
            {
                continue;
            }

            var key = trimmed[..separator].Trim( );
            var rawValue = trimmed[(separator + 1)..].TrimStart( );
            var keyIndex = source.Text.IndexOf( key, StringComparison.Ordinal );
            var valueIndex = source.Text.IndexOf( rawValue, Math.Max( 0, keyIndex + key.Length ),
                StringComparison.Ordinal );
            var value = ReadMultilineValue( lines, ref index, rawValue );
            value = StructuredConfigParserSupport.StripInlineComment( value, '#' );
            if ( key.Length > 0 && value.Length > 0 )
            {
                entries.Add( StructuredConfigParserSupport.Entry(
                    StructuredConfigParserSupport.StripQuoted( key ),
                    StripTomlString( value ),
                    table,
                    source,
                    keyIndex,
                    valueIndex,
                    rawValue.StartsWith( "\"\"\"", StringComparison.Ordinal ) ||
                        rawValue.StartsWith( "'''", StringComparison.Ordinal )
                            ? null
                            : source.Trimmed ) );
            }
        }

        return entries;
    }

    private static bool TryReadTable( string line, out string? table )
    {
        table = null;
        if ( line.StartsWith( "[[", StringComparison.Ordinal ) &&
            line.EndsWith( "]]", StringComparison.Ordinal ) && line.Length > 4 )
        {
            table = line[2..^2].Trim( );
            return true;
        }

        if ( line.StartsWith( '[' ) && line.EndsWith( ']' ) && line.Length > 2 )
        {
            table = line[1..^1].Trim( );
            return true;
        }

        return false;
    }

    private static string ReadMultilineValue(
        ConfigSourceLine[] lines,
        ref int index,
        string initial )
    {
        var delimiter = initial.StartsWith( "\"\"\"", StringComparison.Ordinal )
            ? "\"\"\""
            : initial.StartsWith( "'''", StringComparison.Ordinal ) ? "'''" : string.Empty;
        if ( delimiter.Length == 0 || initial[delimiter.Length..].Contains( delimiter,
                StringComparison.Ordinal ) )
        {
            return initial;
        }

        List<string> parts = [initial];
        while ( ++index < lines.Length )
        {
            parts.Add( lines[index].Text );
            if ( lines[index].Text.Contains( delimiter, StringComparison.Ordinal ) )
            {
                break;
            }
        }

        return string.Join( '\n', parts );
    }

    private static string StripTomlString( string value )
    {
        var trimmed = value.Trim( );
        foreach ( var delimiter in new[] { "\"\"\"", "'''" } )
        {
            if ( trimmed.StartsWith( delimiter, StringComparison.Ordinal ) &&
                trimmed.EndsWith( delimiter, StringComparison.Ordinal ) &&
                trimmed.Length >= delimiter.Length * 2 )
            {
                return trimmed[delimiter.Length..^delimiter.Length].Trim( '\r', '\n' );
            }
        }

        return StructuredConfigParserSupport.StripQuoted( trimmed );
    }
}
