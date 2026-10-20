namespace CommandDeck;

public enum LearnedQueryKind
{
    // The query typed now is one the user searched before accepting the result.
    Exact,

    // The query typed now begins a longer learned query.
    Extended
}

// A learned query: what was searched before the result was accepted, and when.
public readonly record struct LearnedQuery( LearnedQueryKind Kind, long LastUsedUnixMilliseconds );

// Queries the user typed and then accepted a result for, read from opt-in query
// history, so an abbreviation such as "ff" can keep meaning what it meant.
public static class LearnedQueries
{
    // An extended match needs at least this much typed, so one letter does not
    // claim every learned query that starts with it.
    public const int MinimumExtendedLength = 2;

    public static string Normalize( string? text ) =>
        string.Join( ' ', (text ?? string.Empty).Split( ( char[]? ) null, StringSplitOptions.RemoveEmptyEntries ) )
            .ToLowerInvariant( );

    // Both arguments are normalized.
    public static LearnedQueryKind? Match( string learned, string typed )
    {
        if ( learned.Length == 0 || typed.Length == 0 )
        {
            return null;
        }

        if ( string.Equals( learned, typed, StringComparison.Ordinal ) )
        {
            return LearnedQueryKind.Exact;
        }

        return typed.Length >= MinimumExtendedLength && learned.StartsWith( typed, StringComparison.Ordinal )
            ? LearnedQueryKind.Extended
            : null;
    }

    // An exact match outranks an extended one; between equals, the latest use.
    public static bool Outranks( LearnedQuery candidate, LearnedQuery existing ) =>
        candidate.Kind != existing.Kind
            ? candidate.Kind == LearnedQueryKind.Exact
            : candidate.LastUsedUnixMilliseconds > existing.LastUsedUnixMilliseconds;
}
