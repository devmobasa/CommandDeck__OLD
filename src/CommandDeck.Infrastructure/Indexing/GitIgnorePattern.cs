using System.Text;
using System.Text.RegularExpressions;

namespace CommandDeck;

// Git's wildmatch operates on bytes, not Unicode code points. Keep that property
// for '?' and bracket expressions while using a bounded, non-backtracking matcher.
internal sealed partial class GitIgnorePattern
{
    private readonly Regex _expression;
    private readonly bool _basenameOnly;
    private readonly bool _directoryOnly;

    private GitIgnorePattern( string text, int line, bool negated, bool basenameOnly,
        bool directoryOnly, Regex expression )
    {
        Text = text;
        Line = line;
        Negated = negated;
        _basenameOnly = basenameOnly;
        _directoryOnly = directoryOnly;
        _expression = expression;
    }

    public string Text { get; }
    public int Line { get; }
    public bool Negated { get; }

    public bool Matches( string relativePath, bool directory ) =>
        (!_directoryOnly || directory) && _expression.IsMatch( AsBytes(
            _basenameOnly ? Path.GetFileName( relativePath ) : relativePath ) );

    public static GitIgnorePattern? Parse( string text, int line )
    {
        var pattern = TrimTrailingSpaces( text );
        if ( pattern.Length == 0 || pattern[0] == '#' )
        {
            return null;
        }
        var original = pattern;
        var negated = pattern[0] == '!';
        if ( negated )
        {
            pattern = pattern[1..];
        }
        if ( pattern.Length == 0 )
        {
            return null;
        }
        var directoryOnly = pattern.EndsWith( '/' );
        if ( directoryOnly )
        {
            pattern = pattern[..^1];
        }
        var basenameOnly = !pattern.Contains( '/' );
        if ( pattern.StartsWith( '/' ) )
        {
            pattern = pattern[1..];
        }
        if ( pattern.Length == 0 )
        {
            return null;
        }
        var expression = Translate( AsBytes( pattern ) );
        if ( expression is null )
        {
            return null;
        }
        try
        {
            return new GitIgnorePattern( original, line, negated, basenameOnly, directoryOnly,
                new Regex( expression, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                    TimeSpan.FromMilliseconds( 100 ) ) );
        }
        catch ( ArgumentException )
        {
            // Invalid wildmatch ranges and incomplete escapes never match in Git.
            return null;
        }
    }

    private static string? Translate( string pattern )
    {
        StringBuilder result = new( "\\A" );
        for ( var index = 0; index < pattern.Length; index++ )
        {
            var character = pattern[index];
            switch ( character )
            {
                case '*':
                    AppendStars( result, pattern, ref index );
                    break;
                case '?':
                    result.Append( "[^/]" );
                    break;
                case '[':
                    if ( !AppendClass( result, pattern, ref index ) )
                    {
                        return null;
                    }
                    break;
                case '\\':
                    if ( ++index == pattern.Length )
                    {
                        return null;
                    }
                    result.Append( Regex.Escape( pattern[index].ToString( ) ) );
                    break;
                default:
                    result.Append( Regex.Escape( character.ToString( ) ) );
                    break;
            }
        }
        return result.Append( "\\z" ).ToString( );
    }

    private static void AppendStars( StringBuilder result, string pattern, ref int index )
    {
        var start = index;
        while ( index + 1 < pattern.Length && pattern[index + 1] == '*' )
        {
            index++;
        }
        var recursive = index > start && (start == 0 || pattern[start - 1] == '/') &&
                    (index + 1 == pattern.Length || pattern[index + 1] == '/');
        if ( recursive && index + 1 < pattern.Length )
        {
            result.Append( "(?:[^/]+/)*" );
            index++;
        }
        else
        {
            result.Append( recursive ? "[\\s\\S]*" : "[^/]*" );
        }
    }

    private static string TrimTrailingSpaces( string value )
    {
        var end = value.Length;
        while ( end > 0 && value[end - 1] == ' ' )
        {
            var escapes = 0;
            for ( var index = end - 2; index >= 0 && value[index] == '\\'; index-- )
            {
                escapes++;
            }
            if ( escapes % 2 != 0 )
            {
                break;
            }
            end--;
        }
        return value[..end];
    }

    private static string AsBytes( string value ) => Encoding.Latin1.GetString( Encoding.UTF8.GetBytes( value ) );
}
