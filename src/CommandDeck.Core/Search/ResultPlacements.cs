namespace CommandDeck;

/// <summary>
/// Where a row goes outside ranking. A trailing row, such as "Search HEY for
/// …", follows every ranked row: it never takes a Top Match or a first-page
/// slot, and it does not count as a result when deciding on the web fallback.
/// </summary>
public static class ResultPlacements
{
    public const string MetadataKey = "placement";
    public const string Trailing = "trailing";

    // At most this many trailing rows follow one search.
    public const int MaximumTrailing = 2;

    public static bool IsTrailing( SearchResult result ) =>
        result.Metadata.TryGetValue( MetadataKey, out var placement ) && placement == Trailing;
}
