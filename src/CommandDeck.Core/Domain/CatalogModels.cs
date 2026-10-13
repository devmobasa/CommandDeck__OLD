namespace CommandDeck;


public sealed class CatalogStats
{
    public long ItemCount { get; init; }
    public long FileCount { get; init; }
    public long ConfigCount { get; init; }
    public long ApplicationCount { get; init; }
    public long UsageEventCount { get; init; }
    public long DatabaseBytes { get; init; }
}

public sealed class CatalogDimensionCount
{
    public required string Id { get; init; }
    public long Count { get; init; }
}

public sealed class CatalogInspection
{
    public required CatalogStats Stats { get; init; }
    public List<CatalogDimensionCount> SourceCounts { get; init; } = [];
    public List<CatalogDimensionCount> KindCounts { get; init; } = [];
    public bool SourceCountsTruncated { get; init; }
    public bool KindCountsTruncated { get; init; }
    public bool RebuildRequired { get; init; }
    public long FailedItemCount { get; init; }
    public long StaleItemCount { get; init; }
    public long PendingItemCount { get; init; }
    public bool DetailedWorkStateAvailable { get; init; }
    public List<IndexWorkItem> WorkItems { get; init; } = [];
    public long TotalWorkItemCount { get; init; }
    public bool WorkItemCountsExact { get; init; } = true;
    public int WorkItemOffset { get; init; }
    public bool WorkItemsTruncated { get; init; }
    public List<IndexExclusionDiagnostic> ExclusionDiagnostics { get; init; } = [];
    public long TotalExclusionDiagnosticCount { get; init; }
    public bool ExclusionDiagnosticsTruncated { get; init; }
}

public static class IndexWorkStates
{
    public const string Failed = "failed";
    public const string Stale = "stale";
    public const string Pending = "pending";
}

public sealed class IndexWorkItem
{
    public required string Id { get; init; }
    public required string State { get; init; }
    public required string Root { get; init; }
    public required string RelativePath { get; init; }
    public required string Reason { get; init; }
    public long UpdatedUnixMilliseconds { get; init; }
}

public static class IndexExclusionReasons
{
    public const string IndexStorage = "index-storage";
    public const string PrivacyRule = "privacy-rule";
    public const string RootGlob = "root-glob";
    public const string GeneratedDirectory = "generated-directory";
    public const string RepositoryIgnore = "repository-ignore";
    public const string IgnoreRulesUnavailable = "ignore-rules-unavailable";
    public const string Hidden = "hidden";
    public const string SizeLimit = "size-limit";
    public const string PathBoundary = "path-boundary";
    public const string RootUnavailable = "root-unavailable";
}

public sealed class IndexExclusionDiagnostic
{
    public required string Root { get; init; }
    public required string RelativePath { get; init; }
    public required string Reason { get; init; }
    public string? Rule { get; init; }
}

public sealed class CatalogMaintenanceResult
{
    public required string Operation { get; init; }
    public bool Succeeded { get; init; }
    public long AffectedItemCount { get; init; }
    public long DatabaseBytesBefore { get; init; }
    public long DatabaseBytesAfter { get; init; }
    public bool RebuildRequired { get; init; }
    public List<string> Messages { get; init; } = [];
}

public sealed class PrivacyAuditResult
{
    public bool Succeeded { get; init; }
    public long CatalogPathCount { get; init; }
    public long ExclusionDiagnosticCount { get; init; }
    public long PolicyViolationCount { get; init; }
    public long UnsafeDiagnosticCount { get; init; }
    public List<string> Messages { get; init; } = [];
}
