namespace CommandDeck;

public static class SearchProviderOutcomes
{
    public const string Saturated = "saturated";
    public const string Completed = "complete";
    public const string TimedOut = "timeout";
    public const string Cancelled = "cancelled";
    public const string Failed = "failure";
}

public sealed class SearchProviderDiagnostic
{
    public required string ProviderId { get; init; }
    public required string Outcome { get; init; }
    public long DurationMilliseconds { get; init; }
    public int ResultCount { get; init; }
    public string? ErrorType { get; init; }
}
