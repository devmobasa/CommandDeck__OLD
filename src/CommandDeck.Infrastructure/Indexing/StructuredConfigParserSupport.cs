namespace CommandDeck;

internal interface IStructuredConfigParser
{
    IReadOnlyList<ConfigEntry> Parse( string text );
}

internal sealed class EmptyConfigParser : IStructuredConfigParser
{
    public static EmptyConfigParser Instance { get; } = new( );

    public IReadOnlyList<ConfigEntry> Parse( string text ) => [];
}

internal readonly record struct ConfigSourceLine( int Number, string Text )
{
    public string Trimmed => Text.Trim( );
}

internal static class StructuredConfigParserSupport
{
    public const int MaximumValueCharacters = 512;

    public static ConfigSourceLine[] Lines( string text ) =>
        text.Replace( "\r\n", "\n", StringComparison.Ordinal )
            .Replace( '\r', '\n' )
            .Split( '\n' )
            .Select( static ( line, index ) => new ConfigSourceLine( index + 1, line ) )
            .ToArray( );

    public static ConfigEntry Entry(
        string key,
        string value,
        string? section,
        ConfigSourceLine line,
        int keyIndex,
        int valueIndex,
        string? assignment = null ) => new( )
        {
            Key = key,
            Value = Truncate( value.Trim( ) ),
            Section = section,
            KeyLine = line.Number,
            KeyColumn = Math.Max( 1, keyIndex + 1 ),
            ValueLine = line.Number,
            ValueColumn = Math.Max( 1, valueIndex + 1 ),
            Assignment = assignment
        };

    public static string Truncate( string value ) =>
        value.Length <= MaximumValueCharacters
            ? value
            : value[..MaximumValueCharacters] + "…";

    public static string StripQuoted( string value )
    {
        var trimmed = value.Trim( );
        return trimmed.Length >= 2 &&
            (trimmed[0] == '"' && trimmed[^1] == '"' ||
             trimmed[0] == '\'' && trimmed[^1] == '\'') &&
            !trimmed[1..^1].Contains( trimmed[0] )
                ? trimmed[1..^1]
                : trimmed;
    }

    public static int FindUnquoted( string text, char separator )
    {
        var quote = '\0';
        var escaped = false;
        for ( var index = 0; index < text.Length; index++ )
        {
            var current = text[index];
            if ( escaped )
            {
                escaped = false;
                continue;
            }

            if ( current == '\\' && quote != '\'' )
            {
                escaped = true;
                continue;
            }

            if ( current is '"' or '\'' )
            {
                quote = quote == '\0' ? current : quote == current ? '\0' : quote;
                continue;
            }

            if ( quote == '\0' && current == separator )
            {
                return index;
            }
        }

        return -1;
    }

    public static string StripInlineComment( string value, params char[] markers )
    {
        var quote = '\0';
        var escaped = false;
        for ( var index = 0; index < value.Length; index++ )
        {
            var current = value[index];
            if ( escaped )
            {
                escaped = false;
                continue;
            }

            if ( current == '\\' && quote != '\'' )
            {
                escaped = true;
                continue;
            }

            if ( current is '"' or '\'' )
            {
                quote = quote == '\0' ? current : quote == current ? '\0' : quote;
                continue;
            }

            if ( quote == '\0' && markers.Contains( current ) &&
                (index == 0 || char.IsWhiteSpace( value[index - 1] )) )
            {
                return value[..index].TrimEnd( );
            }
        }

        return value.TrimEnd( );
    }
}
