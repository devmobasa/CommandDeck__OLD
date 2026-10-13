using System.IO.Enumeration;

namespace CommandDeck;

public sealed class PathPolicy
{
    private readonly string[] _neverIndex;
    private readonly string?[] _simplePatterns;
    private readonly string[]?[] _starSegments;

    public PathPolicy( PrivacyOptions options )
    {
        _neverIndex = options.NeverIndex
            .Select( ExpandPattern )
            .Select( Normalize )
            .ToArray( );

        // Indexing checks every entry, so wildcard patterns are prepared once.
        _simplePatterns = _neverIndex
            .Select( static pattern => ContainsWildcards( pattern )
                ? pattern.Replace( "**", "*", StringComparison.Ordinal )
                : null )
            .ToArray( );
        _starSegments = _simplePatterns
            .Select( static pattern => pattern is null || pattern.Contains( '?', StringComparison.Ordinal )
                ? null
                : pattern.Split( '*' ) )
            .ToArray( );
    }

    public bool IsDenied( string path )
    {
        return TryGetDenial( path, out _ );
    }

    public bool TryGetDenial( string path, out string matchedRule )
    {
        var normalized = Normalize( Path.GetFullPath( path ) );
        for ( var index = 0; index < _neverIndex.Length; index++ )
        {
            var pattern = _neverIndex[index];
            var simplePattern = _simplePatterns[index];
            if ( simplePattern is null )
            {
                if ( normalized.Equals( pattern, StringComparison.Ordinal ) ||
                    (normalized.Length > pattern.Length &&
                    normalized[pattern.Length] == '/' &&
                    normalized.StartsWith( pattern, StringComparison.Ordinal )) )
                {
                    matchedRule = pattern;
                    return true;
                }

                continue;
            }

            var segments = _starSegments[index];
            if ( segments is not null
                ? MatchesStarPattern( normalized, segments )
                : FileSystemName.MatchesSimpleExpression( simplePattern, normalized, ignoreCase: false ) )
            {
                matchedRule = pattern;
                return true;
            }
        }

        matchedRule = string.Empty;
        return false;
    }

    // Matches a pattern whose only wildcard is '*' (any run of characters,
    // '/' included), split at its stars: the first piece must start the value,
    // the last must end it, and the others must appear in order between them.
    // This is what the runtime's simple expression matcher decides for such a
    // pattern, in one pass instead of a backtracking one.
    internal static bool MatchesStarPattern( string value, string[] segments )
    {
        // The runtime matcher never matches an empty value, not even with "*".
        if ( value.Length == 0 )
        {
            return false;
        }

        var prefix = segments[0];
        if ( segments.Length == 1 )
        {
            return string.Equals( value, prefix, StringComparison.Ordinal );
        }

        var suffix = segments[^1];
        if ( value.Length < prefix.Length + suffix.Length ||
            !value.StartsWith( prefix, StringComparison.Ordinal ) ||
            !value.EndsWith( suffix, StringComparison.Ordinal ) )
        {
            return false;
        }

        var position = prefix.Length;
        var end = value.Length - suffix.Length;
        for ( var index = 1; index < segments.Length - 1; index++ )
        {
            var segment = segments[index];
            if ( segment.Length == 0 )
            {
                continue;
            }

            var found = value.IndexOf( segment, position, end - position, StringComparison.Ordinal );
            if ( found < 0 )
            {
                return false;
            }

            position = found + segment.Length;
        }

        return true;
    }

    public static bool MatchesAny( string path, IEnumerable<string> patterns )
    {
        return TryMatch( path, patterns, out _ );
    }

    public static bool TryMatch(
        string path,
        IEnumerable<string> patterns,
        out string matchedPattern )
    {
        var normalized = Normalize( path );
        foreach ( var rawPattern in patterns )
        {
            var pattern = Normalize( ExpandPattern( rawPattern ) )
                .Replace( "**", "*", StringComparison.Ordinal );

            if ( FileSystemName.MatchesSimpleExpression( pattern, normalized, ignoreCase: false ) ||
                FileSystemName.MatchesSimpleExpression(
                    pattern,
                    Path.GetFileName( normalized ),
                    ignoreCase: false ) )
            {
                matchedPattern = rawPattern;
                return true;
            }
        }

        matchedPattern = string.Empty;
        return false;
    }

    private static string ExpandPattern( string pattern )
    {
        if ( pattern.StartsWith( '~' ) || pattern.StartsWith( '$' ) )
        {
            try
            {
                return PathExpansion.Expand( pattern );
            }
            catch ( ArgumentException )
            {
                return pattern;
            }
        }

        return pattern;
    }

    private static string Normalize( string value ) =>
        value.Replace( '\\', '/' ).TrimEnd( '/' );

    private static bool ContainsWildcards( string value ) =>
        value.Contains( '*', StringComparison.Ordinal ) ||
        value.Contains( '?', StringComparison.Ordinal );
}
