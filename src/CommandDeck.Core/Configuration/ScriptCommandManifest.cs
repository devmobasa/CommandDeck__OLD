namespace CommandDeck;

public sealed class ScriptCommandManifest
{
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public required string Executable { get; init; }
    public List<string> Arguments { get; init; } = [];
    public List<ActionInput> Inputs { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}
