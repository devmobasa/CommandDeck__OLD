namespace CommandDeck;


public sealed class IndexProgress
{
    public string Stage { get; init; } = "idle";
    public long Discovered { get; init; }
    public long Indexed { get; init; }
    public long Skipped { get; init; }
    public long Failed { get; init; }
    public string? CurrentPath { get; init; }
    public string? RootId { get; init; }
    public string? RootPath { get; init; }
}

public sealed class IndexRootStatus
{
    public required string Id { get; init; }
    public required string Path { get; init; }
    public bool Watched { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int WatchedDirectoryCount { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public long UnwatchedDirectoryCount { get; init; }
    public bool Paused { get; init; }
    public long PendingEventCount { get; init; }
    public long IndexedItemCount { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public string? LastError { get; init; }
    public string? Remediation { get; init; }
}

public sealed class IncrementalIndexStatus
{
    public bool Started { get; init; }
    public bool Reconciling { get; init; }
    public bool ReconciliationRequired { get; init; }
    public string? ReconciliationReason { get; init; }
    public int WatchedRootCount { get; init; }
    public List<string> UnwatchedRoots { get; init; } = [];
    public long PendingEventCount { get; init; }
    public DateTimeOffset? LastEventAt { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public string? LastError { get; init; }
    public required string ExtractorVersion { get; init; }
    public List<IndexRootStatus> Roots { get; init; } = [];
    public long IgnoredEventCount { get; init; }
    public int MaximumObservedBatchCount { get; init; }
    public int LastReconciledRootCount { get; init; }
    public IndexMaintenanceStatus Maintenance { get; init; } = new( );
}

public sealed class IndexMaintenanceStatus
{
    public bool Enabled { get; init; }
    public bool Running { get; init; }
    public DateTimeOffset? LastStartedAt { get; init; }
    public DateTimeOffset? LastCompletedAt { get; init; }
    public DateTimeOffset? NextRunAt { get; init; }
    public string? LastError { get; init; }
    public long RemovedStalePathCount { get; init; }
}

public static class RuntimeHealthStates
{
    public const string Available = "available";
    public const string Disabled = "disabled";
    public const string Unavailable = "unavailable";
    public const string Degraded = "degraded";
    public const string Inactive = "inactive";
}

public sealed class ProviderHealthStatus
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string State { get; init; }
    public string? Detail { get; init; }
    public string? Remediation { get; init; }
}

public sealed class RuntimeStatusSnapshot
{
    public required CatalogStats Catalog { get; init; }
    public required IncrementalIndexStatus Indexing { get; init; }
    public required CatalogInspection Inspection { get; init; }
    public List<ProviderHealthStatus> Providers { get; init; } = [];
}

public sealed class ReindexReport
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public long Discovered { get; init; }
    public long Indexed { get; init; }
    public long Skipped { get; init; }
    public long Failed { get; init; }
    public string Scope { get; init; } = IndexReindexScopeKinds.Full;
    public string? Target { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public static class IndexReindexScopeKinds
{
    public const string Full = "full";
    public const string Root = "root";
    public const string Directory = "directory";
    public const string File = "file";
}

public sealed class IndexReindexScope
{
    public string Kind { get; init; } = IndexReindexScopeKinds.Full;
    public string? RootId { get; init; }
    public string? Path { get; init; }
}
