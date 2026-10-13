namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task HerdrCoordinatorBrowsesWithExclusionsAsync( )
    {
        HerdrFake fake = new( );
        HerdrProvider provider = new( new HerdrClient( fake.Runner, static ( ) => true ) );
        SearchCoordinator coordinator = new( [provider], new SearchOptions( ) );

        foreach ( var query in new[] { "herdr: -htab:", "hspace: -file:" } )
        {
            var results = await coordinator.SearchAsync( new SearchRequest { Query = query, Limit = 20 }, CancellationToken.None );
            Equal( "herdr:workspace:w1|herdr:workspace:w2|herdr:workspace:w3",
                string.Join( '|', results.Select( static result => result.Id ) ) );
        }

        var tabs = await coordinator.SearchAsync( new SearchRequest { Query = "herdr: -hspace:", Limit = 20 }, CancellationToken.None );
        Equal( 5, tabs.Count );
        True( tabs.All( static result => result.Kind == ResultKinds.Tab ) );

        var calls = fake.Calls.Count;
        foreach ( var query in new[] { "-htab:", "-file:", "herdr: -hspace: -htab:", "hspace: -herdr:" } )
        {
            var results = await coordinator.SearchAsync( new SearchRequest { Query = query, Limit = 20 }, CancellationToken.None );
            Equal( 0, results.Count );
        }

        Equal( calls, fake.Calls.Count );
    }

    private static async Task HerdrCoordinatorPreservesBrowseOrderWithUsageAsync( )
    {
        var database = Path.Combine( Path.GetTempPath( ), $"commanddeck-herdr-browse-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.UsageHistory.RecordInvocationAsync( "herdr:workspace:w3", "switch-space", null, CancellationToken.None );
            await catalog.UsageHistory.RecordInvocationAsync( "herdr:tab:w2:t2", "switch-tab", null, CancellationToken.None );
            HerdrFake fake = new( );
            HerdrProvider provider = new( new HerdrClient( fake.Runner, static ( ) => true ) );
            SearchCoordinator coordinator = new( [provider], new SearchOptions( ), catalog, catalog, usageHistory: catalog.UsageHistory );

            foreach ( var query in new[] { "herdr:", "hspace:", "htab:", "herdr: -htab:" } )
            {
                var expected = await SearchHerdrAsync( query );
                var results = await coordinator.SearchAsync( new SearchRequest { Query = query, Limit = 20 }, CancellationToken.None );
                Equal( string.Join( '|', expected.Select( static result => result.Id ) ),
                    string.Join( '|', results.Select( static result => result.Id ) ) );
                True( results.All( static result => !result.Ranking.Contributions.Any(
                    static contribution => contribution.Kind == RankingContributionKinds.Frecency ) ) );
            }

            // Text search still learns from accepted invocations.
            var typed = await coordinator.SearchAsync( new SearchRequest { Query = "hspace: notes", Limit = 20 }, CancellationToken.None );
            Equal( "herdr:workspace:w3", typed[0].Id );
            True( typed[0].Ranking.Contributions.Any( static contribution => contribution.Kind == RankingContributionKinds.Frecency ) );

            await SetHerdrPreferenceAsync( catalog, enabledInSearch: true, enabledOnHome: true );
            var home = await coordinator.SearchAsync( new SearchRequest { Query = "", Limit = 20 }, CancellationToken.None );
            var section = home.Where( static result => result.Section?.Id == "herdr" ).ToArray( );
            Equal( "herdr:workspace:w1|herdr:tab:w1:t1|herdr:workspace:w2|herdr:workspace:w3",
                string.Join( '|', section.Select( static result => result.Id ) ) );
            True( section.All( static result => !result.Ranking.Contributions.Any(
                static contribution => contribution.Kind == RankingContributionKinds.Frecency ) ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }
}
