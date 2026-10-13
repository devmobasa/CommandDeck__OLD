namespace CommandDeck;

public class AiOperationException(
    string category,
    int? retryAfterSeconds = null,
    string? safeSummary = null ) : InvalidOperationException( safeSummary )
{
    public string Category { get; } = category;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
    public string SafeSummary { get; } = safeSummary ?? "The AI operation failed.";
}
