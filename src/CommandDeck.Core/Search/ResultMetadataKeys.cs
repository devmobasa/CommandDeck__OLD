namespace CommandDeck;

/// <summary>Result metadata the launcher reads, beyond presentation fields.</summary>
public static class ResultMetadataKeys
{
    public const string CatalogResultId = "catalogResultId";
    public const string ExactAlias = "exactAlias";

    // The text Tab puts in the search field for this row, instead of its title:
    // a completion row names the whole query with its value filled in.
    public const string CompletionText = "completionText";
}
