namespace CommandDeck;

public sealed class ProjectOptions
{
    public required string Title { get; init; }
    public required string Path { get; init; }
    public string Editor { get; init; } = "rider";
    public List<string> EditorArguments { get; init; } = ["{path}"];
}
