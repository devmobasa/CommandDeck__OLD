namespace CommandDeck;

/// <summary>Results and a privacy-safe summary of provider completion.</summary>
public sealed record SearchOutcome( IReadOnlyList<SearchResult> Results, int IncompleteProviderCount )
{
    public bool IsComplete => IncompleteProviderCount == 0;
}
