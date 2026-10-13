namespace CommandDeck;


public static class HerdrTargetKinds
{
    public const string Workspace = "workspace";
    public const string Tab = "tab";
}

public enum HerdrAvailability
{
    NotInstalled,
    Stopped,
    Available,
    Incompatible,
    Failed
}

public sealed class HerdrStatus
{
    public required HerdrAvailability Availability { get; init; }
    public string? Version { get; init; }
    public long? Protocol { get; init; }
    public string? SocketPath { get; init; }
    public required string Detail { get; init; }
}

public sealed record HerdrWorkspace(
    string Id,
    long Number,
    string Label,
    bool Focused,
    long PaneCount,
    long TabCount,
    string AgentStatus );

public sealed record HerdrTab(
    string Id,
    string WorkspaceId,
    long Number,
    string Label,
    bool Focused,
    long PaneCount,
    string AgentStatus );

public sealed class HerdrInventory
{
    public required IReadOnlyList<HerdrWorkspace> Workspaces { get; init; }
    public required IReadOnlyList<HerdrTab> Tabs { get; init; }
}

public sealed class HerdrFocusOutcome
{
    public required bool Succeeded { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
}
