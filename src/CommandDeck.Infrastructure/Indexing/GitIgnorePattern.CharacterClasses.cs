using System.Text;

namespace CommandDeck;

internal sealed partial class GitIgnorePattern
{
    private static bool AppendClass( StringBuilder result, string pattern, ref int index )
    {
        var cursor = index + 1;
        if ( cursor >= pattern.Length )
        {
            return false;
        }
        var negate = pattern[cursor] is '!' or '^';
        if ( negate )
        {
            cursor++;
        }
        StringBuilder characters = new( );
        if ( cursor < pattern.Length && pattern[cursor] == ']' )
        {
            characters.Append( "\\]" );
            cursor++;
        }
        while ( cursor < pattern.Length && pattern[cursor] != ']' )
        {
            if ( !AppendClassCharacter( characters, pattern, ref cursor ) )
            {
                return false;
            }
        }
        if ( cursor >= pattern.Length || characters.Length == 0 )
        {
            return false;
        }
        index = cursor;
        if ( negate )
        {
            result.Append( "[^/" ).Append( characters ).Append( ']' );
        }
        else
        {
            result.Append( '[' ).Append( characters ).Append( "-[/]]" );
        }
        return true;
    }

    private static bool AppendClassCharacter( StringBuilder result, string pattern, ref int cursor )
    {
        var character = pattern[cursor++];
        if ( character == '[' && cursor < pattern.Length && pattern[cursor] == ':' )
        {
            var end = pattern.IndexOf( ":]", cursor, StringComparison.Ordinal );
            if ( end < 0 )
            {
                return false;
            }
            var characters = PosixClass( pattern[(cursor + 1)..end] );
            if ( characters is null )
            {
                return false;
            }
            result.Append( characters );
            cursor = end + 2;
            return true;
        }
        if ( character == '\\' )
        {
            if ( cursor == pattern.Length )
            {
                return false;
            }
            character = pattern[cursor++];
            if ( character is '-' or ']' or '[' or '^' or '\\' )
            {
                result.Append( '\\' );
            }
        }
        else if ( character is '[' or '^' )
        {
            result.Append( '\\' );
        }
        result.Append( character );
        return true;
    }

    private static string? PosixClass( string name ) => name switch
    {
        "alnum" => "a-zA-Z0-9",
        "alpha" => "a-zA-Z",
        "blank" => " \\t",
        "cntrl" => "\\x00-\\x1f\\x7f",
        "digit" => "0-9",
        "graph" => "\\x21-\\x7e",
        "lower" => "a-z",
        "print" => "\\x20-\\x7e",
        "punct" => "\\x21-\\x2f\\x3a-\\x40\\x5b-\\x60\\x7b-\\x7e",
        "space" => " \\t\\r\\n\\v\\f",
        "upper" => "A-Z",
        "xdigit" => "0-9a-fA-F",
        _ => null
    };
}
