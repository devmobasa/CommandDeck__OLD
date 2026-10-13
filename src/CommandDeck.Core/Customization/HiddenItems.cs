namespace CommandDeck;

// Hidden launcher items: a reversible display preference keyed by the same
// canonical identity as bookmarks and aliases. Hiding never deletes a file,
// uninstalls an application, or erases usage; restoring resumes ordinary
// ranking. See docs/search-and-ranking.md.
public static class HiddenItemLimits
{
    public const int MaximumHiddenItems = 512;
    public const int DefaultPageSize = 64;
    public const string IdPrefix = "hidden:";
}

public sealed class HiddenItemSummary
{
    public required string Id { get; init; }
    public required string StableId { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public required string SourceId { get; init; }
    public string? Icon { get; init; }
    public long HiddenUnixMs { get; init; }
    public bool IsAvailable { get; init; }
}

public sealed class HiddenItemsResult
{
    public List<HiddenItemSummary> Items { get; init; } = [];
    public int Total { get; init; }
    public bool Truncated { get; init; }
}
