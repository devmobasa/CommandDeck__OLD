using System.Text;

namespace CommandDeck;

internal sealed record SnippetPlaceholderSyntax(
    string Source,
    string Name,
    IReadOnlyList<KeyValuePair<string, string>> Options,
    IReadOnlyList<string> Modifiers );

internal readonly record struct SnippetToken( string? Literal, SnippetPlaceholderSyntax? Placeholder );

// Syntax only: {name key=value key="quoted" | modifier}. A brace that does not
// open a complete placeholder on one line stays literal text, so {{name}} keeps
// its outer braces and JSON or code braces pass through unchanged.
internal static class SnippetTemplateParser
{
    public const int MaximumPlaceholderLength = 1024;
    private const int MaximumNameLength = 64;
    private const int MaximumKeyLength = 32;

    public static List<SnippetToken> Parse( string text )
    {
        ArgumentNullException.ThrowIfNull( text );

        List<SnippetToken> tokens = [];
        StringBuilder literal = new( );
        var index = 0;
        while ( index < text.Length )
        {
            var open = text.IndexOf( '{', index );
            if ( open < 0 )
            {
                literal.Append( text, index, text.Length - index );
                break;
            }

            literal.Append( text, index, open - index );
            if ( !TryParsePlaceholder( text, open, out var placeholder, out var end ) )
            {
                literal.Append( '{' );
                index = open + 1;
                continue;
            }

            Flush( literal, tokens );
            tokens.Add( new( null, placeholder ) );
            index = end;
        }

        Flush( literal, tokens );
        return tokens;
    }

    private static bool TryParsePlaceholder( string text, int start, out SnippetPlaceholderSyntax? placeholder, out int end )
    {
        placeholder = null;
        end = start;
        var limit = Math.Min( text.Length, start + MaximumPlaceholderLength );
        var index = start + 1;
        if ( !TryReadIdentifier( text, ref index, limit, MaximumNameLength, out var name ) )
        {
            return false;
        }

        List<KeyValuePair<string, string>> options = [];
        List<string> modifiers = [];
        while ( true )
        {
            var spaced = SkipSpaces( text, ref index, limit );
            if ( index >= limit )
            {
                return false;
            }

            var current = text[index];
            if ( current == '}' )
            {
                end = index + 1;
                placeholder = new( text[start..end], name, options, modifiers );
                return true;
            }

            if ( current == '|' )
            {
                index++;
                SkipSpaces( text, ref index, limit );
                if ( !TryReadIdentifier( text, ref index, limit, MaximumKeyLength, out var modifier ) )
                {
                    return false;
                }

                modifiers.Add( modifier );
                continue;
            }

            // Options come before modifiers and are separated from the name by whitespace.
            if ( !spaced || modifiers.Count > 0 ||
                !TryReadIdentifier( text, ref index, limit, MaximumKeyLength, out var key ) ||
                index >= limit || text[index] != '=' )
            {
                return false;
            }

            index++;
            if ( !TryReadValue( text, ref index, limit, out var value ) )
            {
                return false;
            }

            options.Add( new( key, value ) );
        }
    }

    private static bool TryReadIdentifier( string text, ref int index, int limit, int maximumLength, out string identifier )
    {
        identifier = string.Empty;
        var start = index;
        if ( index >= limit || !char.IsAsciiLetter( text[index] ) )
        {
            return false;
        }

        index++;
        while ( index < limit && (char.IsAsciiLetterOrDigit( text[index] ) || text[index] is '_' or '-') )
        {
            index++;
        }

        if ( index - start > maximumLength )
        {
            return false;
        }

        identifier = text[start..index];
        return true;
    }

    // Quoted values accept "…" or '…'; a backslash makes the next character literal.
    // A line break anywhere, even quoted, abandons the placeholder.
    private static bool TryReadValue( string text, ref int index, int limit, out string value )
    {
        value = string.Empty;
        if ( index >= limit )
        {
            return false;
        }

        var quote = text[index];
        if ( quote is not ('"' or '\'') )
        {
            var start = index;
            while ( index < limit && !IsBareValueStop( text[index] ) )
            {
                index++;
            }

            value = text[start..index];
            return index > start;
        }

        StringBuilder builder = new( );
        index++;
        while ( index < limit )
        {
            var current = text[index++];
            if ( current is '\n' or '\r' )
            {
                return false;
            }

            if ( current == quote )
            {
                value = builder.ToString( );
                return true;
            }

            if ( current == '\\' )
            {
                if ( index >= limit || text[index] is '\n' or '\r' )
                {
                    return false;
                }

                current = text[index++];
            }

            builder.Append( current );
        }

        return false;
    }

    private static bool IsBareValueStop( char value ) =>
        char.IsWhiteSpace( value ) || value is '"' or '\'' or '\\' or '{' or '}' or '|';

    private static bool SkipSpaces( string text, ref int index, int limit )
    {
        var start = index;
        while ( index < limit && text[index] is ' ' or '\t' )
        {
            index++;
        }

        return index > start;
    }

    private static void Flush( StringBuilder literal, List<SnippetToken> tokens )
    {
        if ( literal.Length == 0 )
        {
            return;
        }

        tokens.Add( new( literal.ToString( ), null ) );
        literal.Clear( );
    }
}
