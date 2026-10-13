using System.Text.Json;

namespace CommandDeck;

public static class ExtensionSessionStatuses
{
    public const string Starting = "starting";
    public const string Running = "running";
    public const string Ready = "ready";
    public const string View = "view";
    public const string Confirmation = "confirmation";
    public const string Working = "working";
    public const string Failed = "failed";
    public const string Completed = "completed";
    public const string Closed = "closed";
}

public sealed record ExtensionSessionInput
{
    public string[] Features { get; init; } = [];
    public long Sequence { get; init; }
    public long Revision { get; init; }
    public string? Handle { get; init; }
    public string? ItemId { get; init; }
    public long FormRevision { get; init; }
    public Dictionary<string, JsonElement>? Values { get; init; }
    public string? ConfirmationId { get; init; }
    public string? SearchLease { get; init; }
    public bool Confirmed { get; init; }
}

public sealed record ExtensionSessionState
{
    public required string SessionId { get; init; }
    public required string PackageKey { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public long Epoch { get; init; }
    public long Revision { get; init; }
    public string? ViewId { get; init; }
    public JsonElement? Content { get; init; }
    public string? Error { get; init; }
    public ExtensionDebugState? Debug { get; init; }
    public ExtensionRemoteConfirmation? Confirmation { get; init; }
}
