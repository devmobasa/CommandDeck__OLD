namespace CommandDeck;

public static class RankingContributionKinds
{
    public const string Lexical = "lexical";
    public const string Field = "field";
    public const string SourceWeight = "source-weight";
    public const string KindPrior = "kind-prior";
    public const string Frecency = "frecency";
    public const string Context = "context";
    public const string ExactAlias = "exact-alias";
    public const string Adaptive = "adaptive";
    public const string Policy = "policy";
}

public static class RankingFieldNames
{
    public const string Title = "title";
    public const string Subtitle = "subtitle";
    public const string Path = "path";
    public const string Metadata = "metadata";
    public const string Content = "content";
    public const string Config = "config";
}

public sealed record RankingContribution( string Kind, string Label, double Value );

public sealed class RankingFieldMatch
{
    public required string Field { get; init; }
    public required string Value { get; init; }
    public List<MatchedRange> Ranges { get; init; } = [];
}

public sealed class IndexFieldPolicy
{
    public required string Field { get; init; }
    public bool Included { get; init; }
    public required string Reason { get; init; }
    public string? Rule { get; init; }
}

public sealed class RankingPlacement
{
    public required string Tier { get; init; }
    public required string Reason { get; init; }
    public bool Promoted { get; init; }
    public string? UsageId { get; init; }
    public double? LastUseAgeDays { get; init; }
}

public sealed class RankingExplanation
{
    public RankingPlacement? Placement { get; init; }
    public double FinalScore { get; init; }
    public List<RankingContribution> Contributions { get; init; } = [];
    public List<RankingFieldMatch> Matches { get; init; } = [];
    public List<IndexFieldPolicy> Policies { get; init; } = [];
}

public sealed class RankingLearningEvent
{
    public required string StableId { get; init; }
    public required string ActionId { get; init; }
    public string? Query { get; init; }
    public long InvokedUnixMilliseconds { get; init; }
}

public sealed class RankingLearningSnapshot
{
    public bool Enabled { get; init; }
    public int RetentionDays { get; init; }
    public int MaximumEvents { get; init; }
    public long TotalEvents { get; init; }
    public bool Truncated { get; init; }
    public List<RankingLearningEvent> Events { get; init; } = [];
}

public sealed class QueryHistoryEntry
{
    public long Id { get; init; }
    public required string Query { get; init; }
    public required string StableId { get; init; }
    public required string ActionId { get; init; }
    public long InvokedUnixMilliseconds { get; init; }
}

public sealed class QueryHistorySnapshot
{
    public bool Enabled { get; init; }
    public int RetentionDays { get; init; }
    public int MaximumEntries { get; init; }
    public long TotalEntries { get; init; }
    public bool Truncated { get; init; }
    public List<QueryHistoryEntry> Entries { get; init; } = [];
}

public readonly record struct UsageSignal( long UseCount, long LastUsedUnixMilliseconds );
