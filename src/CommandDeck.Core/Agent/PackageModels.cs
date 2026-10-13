namespace CommandDeck;

public sealed class PackageRecord
{
    public required string Name { get; init; }
    public string? Repository { get; init; }
    public string? Version { get; init; }
    public string? Description { get; init; }
    public bool Installed { get; init; }
    public string? InstalledVersion { get; init; }
    public long? InstalledBytes { get; init; }
    public long? DownloadBytes { get; init; }
}

public sealed class PackageSearchResult
{
    public required string Query { get; init; }
    public List<PackageRecord> Packages { get; init; } = [];
    public bool Truncated { get; init; }
    public bool HelperAvailable { get; init; }
    public string? Error { get; init; }
}

public sealed class PackageTransactionEntry
{
    public required string Name { get; init; }
    public string? Repository { get; init; }
    public string? CurrentVersion { get; init; }
    public string? TargetVersion { get; init; }
    public bool Requested { get; init; }
    public long? DownloadBytes { get; init; }
    public long? InstalledBytes { get; init; }
}

public sealed class PackageTransactionPreview
{
    public required string PreviewId { get; init; }
    public required string Operation { get; init; }
    public List<string> RequestedNames { get; init; } = [];
    public List<PackageTransactionEntry> Entries { get; init; } = [];
    public long DownloadBytes { get; init; }
    public long InstalledDeltaBytes { get; init; }
    public required string DatabaseIdentity { get; init; }
    public required string TransactionDigest { get; init; }
    public List<string> Route { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public bool Destructive { get; init; }
    public bool Commitable { get; init; }
    public string? Reason { get; init; }
    public long ExpiresUnixMilliseconds { get; init; }
    public string? Rationale { get; init; }
}

public sealed class PackageCommitResult
{
    public required string OperationId { get; init; }
    public required string State { get; init; }
    public string? Message { get; init; }
    public List<string> VerifiedNames { get; init; } = [];
    public List<string> FailedNames { get; init; } = [];
}
