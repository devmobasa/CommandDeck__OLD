namespace CommandDeck;

/// <summary>
/// Where a provider places a row apart from its score. A trailing row, such as
/// "Search HEY for …", follows every ranked row: it never takes a Top Match or
/// a first-page slot, and it does not count as a result when deciding on the
/// web fallback. An explicit row, such as a typed URL, is what the query itself
/// names: it joins exact aliases and calculator results in the explicit Top
/// Match tier, where an exact alias still comes first.
/// </summary>
public static class ResultPlacements
{
    public const string MetadataKey = "placement";
    public const string Trailing = "trailing";
    public const string Explicit = "explicit";

    // At most this many trailing rows follow one search.
    public const int MaximumTrailing = 2;

    public static bool IsTrailing( SearchResult result ) =>
        result.Metadata.TryGetValue( MetadataKey, out var placement ) && placement == Trailing;

    public static bool IsExplicit( SearchResult result ) =>
        result.Metadata.TryGetValue( MetadataKey, out var placement ) && placement == Explicit;
}
