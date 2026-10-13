namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExcludedTitlesCannotConsumeFuzzyCandidateBudgetAsync( )
    {
        var database = Path.Combine( Path.GetTempPath( ), $"commanddeck-fuzzy-policy-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.ReplaceAllAsync( ExcludedFuzzyItems( ), null, default );
            var matches = await catalog.SearchSourcesAsync( QueryParser.Parse( "sytem" ),
                [SourceIds.Files, SourceIds.Applications], 10, default );
            Equal( "file:visible", matches.Single( ).Id );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> ExcludedFuzzyItems( )
    {
        await Task.CompletedTask;
        for ( var index = 0; index < 512; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"file:excluded-{index}",
                Kind = ResultKinds.File,
                Title = $"System {index:D4}",
                SourceId = SourceIds.Files,
                IncludeTitleInSearch = false,
                IncludeMetadataInSearch = false
            };
        }
        yield return new IndexedItem
        {
            StableId = "file:visible",
            Kind = ResultKinds.File,
            Title = "System visible",
            SourceId = SourceIds.Files
        };
    }
}
