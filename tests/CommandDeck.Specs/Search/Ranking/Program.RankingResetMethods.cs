namespace CommandDeck.Specs;

public static partial class Program
{
    // A result ranked by accepted use offers Reset Ranking. Invoking it forgets
    // that use under the canonical ID alone, records no use itself, and never
    // reaches the Linux executor. Results without use, and assistant searches,
    // do not offer it.
    private static async Task ResetRankingForgetsOneResultsUseAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-ranking-reset-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            var used = PromotionRow( "variant", "Signal", metadata: new( ) { ["catalogResultId"] = "app:signal" } );
            used = new SearchResult( used )
            {
                Ranking = new RankingExplanation
                {
                    Contributions = [new( RankingContributionKinds.Frecency, "Recent application use", 0.12 )]
                }
            };
            var unused = PromotionRow( "app:signature", "Signature" );
            AcceptedUsageHistory history = new( catalog.UsageHistory );
            AcceptanceExecutor executor = new( );
            SearchCoordinator search = new( [new StaticSearchProvider( [used, unused] )], options.Search );
            await using var runtime = InvocationFixture( options, catalog, history, executor, search );
            await catalog.UsageHistory.RecordInvocationAsync( "app:signal", "open", null, default );
            await catalog.UsageHistory.RecordInvocationAsync( "app:signature", "open", null, default );

            var results = await runtime.SearchAsync( new SearchRequest { Query = "sig" }, default );
            var offered = results.Single( value => value.Id == used.Id ).Actions;
            Equal( ActionKinds.ResetRanking, offered[^1].Kind );
            Equal( "Reset Ranking", offered[^1].Title );
            True( results.Single( value => value.Id == unused.Id ).Actions
                .All( static action => action.Kind != ActionKinds.ResetRanking ) );

            var outcome = await runtime.InvokeAsync( used.Id, SearchCoordinator.ResetRankingActionId, false, default );
            True( outcome.Succeeded );
            Equal( "Ranking reset for Signal.", outcome.Message );
            var signals = await catalog.UsageHistory.GetUsageSignalsAsync( ["app:signal", "app:signature"], default );
            True( !signals.ContainsKey( "app:signal" ) );
            True( signals.ContainsKey( "app:signature" ) );
            Equal( 0, history.Writes );
            Equal( 0, executor.Calls );

            outcome = await runtime.InvokeAsync( used.Id, SearchCoordinator.ResetRankingActionId, false, default );
            True( outcome.Succeeded );
            Equal( "Signal has no ranking history.", outcome.Message );

            var assistant = await search.SearchWithOutcomeAsync(
                new SearchRequest { Query = "sig" }, SearchPurpose.AssistantTool, default );
            True( assistant.Results.SelectMany( static value => value.Actions )
                .All( static action => action.Kind != ActionKinds.ResetRanking ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
