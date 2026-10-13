namespace CommandDeck;

public sealed class ClipboardHistoryOptions
{
    public int MaximumEntries { get; init; } = 200;
    public int RetentionHours { get; init; } = 168;
}
