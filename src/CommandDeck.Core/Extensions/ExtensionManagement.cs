using System.Text.Json;

namespace CommandDeck;

public static class ExtensionManagementActions
{
    public const string List = "list";
    public const string Inspect = "inspect";
    public const string Install = "install";
    public const string Attach = "attach";
    public const string Logs = "logs";
    public const string Debug = "debug";
    public const string DebugStop = "debug-stop";
    public const string Enable = "enable";
    public const string Disable = "disable";
    public const string Remove = "remove";
    public const string Rollback = "rollback";
    public const string Grants = "grants";
    public const string Configure = "configure";
    public const string Connections = "connections";
    public const string ConnectionSet = "connection-set";
    public const string ConnectionRemove = "connection-remove";
    public const string ConnectionLogin = "connection-login";
    public const string GlobalSearch = "global-search";
    public const string EffectsReset = "effects-reset";
}

public sealed partial class ExtensionManagementRequest
{
    public string Action { get; init; } = ExtensionManagementActions.List;
    public string? Key { get; init; }
    public string? ArchivePath { get; init; }
    public string? Digest { get; init; }
    public long ExpectedRevision { get; init; }
    public bool Confirmed { get; init; }
    public bool EraseSettings { get; init; }
    public string[]? Grants { get; init; }
    public Dictionary<string, JsonElement>? Settings { get; init; }
    public bool IncludeStderr { get; init; }
    public string? CommandId { get; init; }
    public int DebugSeconds { get; init; }
    public ExtensionConnectionSetup? Connection { get; init; }
    public string? ConnectionName { get; init; }
    public bool RemoteRevoke { get; init; }
    public bool GlobalSearchEnabled { get; init; }
    public bool IncludeLegacy { get; init; }

    public void Validate( ) => _ = ToCommand( );
}

public sealed class ExtensionInventory
{
    public long Revision { get; init; }
    public bool Enabled { get; init; }
    public string? Error { get; init; }
    public int Total { get; init; }
    public int Offset { get; init; }
    public List<ExtensionPackageSummary> Packages { get; init; } = [];
    public JsonElement? Manifest { get; init; }
    public string? ArchiveDigest { get; init; }
    public Dictionary<string, JsonElement>? Settings { get; init; }
    public ExtensionDiagnostic[] Diagnostics { get; init; } = [];
    public ExtensionDebugState? Debug { get; init; }
    public ExtensionConnectionSummary[] Connections { get; init; } = [];
    public string? ConnectionMessage { get; init; }
    public int EffectAttempts { get; init; }
}

public sealed class ExtensionPackageSummary
{
    public required string Key { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Origin { get; init; }
    public required string Digest { get; init; }
    public bool Enabled { get; init; }
    public bool Removed { get; init; }
    public bool GlobalSearchEnabled { get; init; }
    public string[] Grants { get; init; } = [];
    public string[] RetainedDigests { get; init; } = [];
    public ExtensionRetainedVersion[] RetainedVersions { get; init; } = [];
}

public sealed record ExtensionRetainedVersion( string Version, string Digest );
public sealed record ExtensionDiagnostic( DateTimeOffset At, string SessionId, string CommandId, string Digest, string Status, string? Stderr );
public sealed record ExtensionDebugState( string CommandId, string Digest, DateTimeOffset ExpiresAt, string SocketPath, bool Consumed );
