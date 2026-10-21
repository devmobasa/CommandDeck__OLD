namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly DateTimeOffset PromotionNow = new( 2026, 9, 7, 12, 0, 0, TimeSpan.Zero );

    private sealed class PromotionProvider( IReadOnlyList<SearchResult> results ) : ICommandProvider
    {
        public string Id => "catalog";
        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken token ) => ValueTask.FromResult( results );
    }

    private sealed class PromotionHistory : IUsageSignalStore
    {
        public Dictionary<string, UsageSignal> Signals { get; } = new( StringComparer.Ordinal );
        public List<int> BatchSizes { get; } = [];
        public bool Fail { get; set; }
        public ValueTask<IReadOnlyDictionary<string, UsageSignal>> GetUsageSignalsAsync( IReadOnlyList<string> ids, CancellationToken token )
        {
            BatchSizes.Add( ids.Count );
            if ( Fail )
            {
                throw new IOException( "unavailable fixture" );
            }
            return ValueTask.FromResult<IReadOnlyDictionary<string, UsageSignal>>( ids.Where( Signals.ContainsKey ).ToDictionary( id => id, id => Signals[id] ) );
        }
        public void Add( string id, long count = 2, double age = 0 ) => Signals[id] = new( count, PromotionNow.AddDays( -age ).ToUnixTimeMilliseconds( ) );
    }

    private static SearchResult PromotionRow( string id, string title, string kind = ResultKinds.Application,
        string source = SourceIds.Applications, double score = 1, Dictionary<string, string>? metadata = null ) => new( )
        {
            Id = id,
            Title = title,
            Kind = kind,
            SourceId = source,
            ProviderId = "static",
            Score = score,
            Metadata = metadata ?? new( StringComparer.Ordinal ),
            Actions = [new CommandAction { Id = "open", Title = "Open", Kind = ActionKinds.RunArgv }]
        };

    private static async Task LearnedPromotionGatesAsync( )
    {
        var row = PromotionRow( "signal", "Signal" );
        PromotionHistory history = new( );
        history.Add( row.Id );
        foreach ( var (query, title, expected) in new (string, string, bool)[]
        {
            ("sig", "Signal", true), ("SIG", "Signal", true), ("sig", "Open-Signal", true),
            (".gi", ".gitignore", true), ("e\u0301", "Éditeur", false), ("🚀", "🚀 Rocket", false),
            ("cafe", "Café Browser", true), ("sig mes", "Signal Messenger", true),
            ("\"signal mes\"", "Signal Messenger", true), ("\"signal mes\"", "Signal Private Messenger", false),
            ("sgn", "Signal", false), ("singal", "Signal", false), ("sig", "assignals", false),
            ("s", "Signal", false), ("", "Signal", false), ("-zzz", "Signal", false),
            ("app:", "Signal", false), ("app: sig", "Signal", true), ("-app: sig", "Signal", false),
            ("title:sig", "Signal", true), ("metadata:sig", "Signal", false),
            ("path:sig", "Signal", false), ("content:sig", "Signal", false),
            ("@catalog sig", "Signal", true)
        } )
        {
            var candidate = PromotionRow( row.Id, title, metadata: new( )
            {
                ["path"] = "/sig/other",
                ["description"] = "sig"
            } );
            SearchCoordinator coordinator = new( [new PromotionProvider( [candidate] )], new SearchOptions( ), usageHistory: history, timeProvider: new FixedTimeProvider( PromotionNow ) );
            var results = await coordinator.SearchAsync( new SearchRequest { Query = query }, default );
            True( expected == results.Any( value => value.Ranking.Placement?.Tier == "learned" && value.Section?.Id == "top-match" ), $"promotion for {query} / {title}" );
        }
        foreach ( var (count, age, expected) in new (int, double, bool)[] { (0, 0, false), (1, 0, false), (2, 21, true), (10, 21.001, false), (10, 0, true) } )
        {
            history.Add( row.Id, count, age );
            SearchCoordinator coordinator = new( [new PromotionProvider( [row] )], new SearchOptions( ), usageHistory: history, timeProvider: new FixedTimeProvider( PromotionNow ) );
            var results = await coordinator.SearchAsync( new SearchRequest { Query = "sig" }, default );
            Equal( expected, results[0].Section!.Id == "top-match" );
        }
    }

    private static async Task LearnedPromotionRejectsNavigationAndMissingSignalsAsync( )
    {
        PromotionHistory history = new( );
        history.Add( "row", 10 );
        var baseRow = PromotionRow( "row", "Signal" );
        SearchResult[] excluded =
        [
            new SearchResult( baseRow ) { Actions = [] },
            new SearchResult( baseRow ) { Actions = [new CommandAction { Id = "browse", Title = "Browse", Kind = ActionKinds.BrowseFolder }] },
            new SearchResult( baseRow ) { Actions = [new CommandAction { Id = "prompt", Title = "Ask", Kind = ActionKinds.AgentPrompt }] },
            new SearchResult( baseRow ) { Kind = ResultKinds.Status, SourceId = SourceIds.Status },
            new SearchResult( baseRow ) { ProviderId = "fallback", Kind = ResultKinds.Quicklink, SourceId = SourceIds.CommandsQuicklinks }
        ];
        foreach ( var row in excluded )
        {
            SearchCoordinator coordinator = new( [new PromotionProvider( [row] )], new SearchOptions( ), usageHistory: history, timeProvider: new FixedTimeProvider( PromotionNow ) );
            var results = await coordinator.SearchAsync( new SearchRequest { Query = "sig" }, default );
            True( results.All( result => result.Section!.Id != "top-match" ) );
        }
        SearchCoordinator zeroMultiplier = new( [new PromotionProvider( [baseRow] )],
            new SearchOptions { Ranking = new RankingOptions { TimeAware = new TimeAwareRankingOptions { ApplicationMultiplier = 0 } } }, usageHistory: history );
        True( (await zeroMultiplier.SearchAsync( new SearchRequest { Query = "sig" }, default )).All( result => result.Section!.Id != "top-match" ) );
        SearchCoordinator missing = new( [new PromotionProvider( [baseRow] )], new SearchOptions( ) );
        True( (await missing.SearchAsync( new SearchRequest { Query = "sig" }, default )).All( result => result.Section!.Id != "top-match" ) );
        Equal( "top-match", (await missing.SearchAsync( new SearchRequest { Query = "signal" }, default ))[0].Section!.Id );
        SearchCoordinator browsing = new( [new PromotionProvider( [baseRow] )], new SearchOptions( ), usageHistory: history, timeProvider: new FixedTimeProvider( PromotionNow ) );
        True( (await browsing.SearchAsync( new SearchRequest { Query = "sig", BrowseDirectory = "/fixture" }, default )).All( result => result.Section!.Id != "top-match" ) );
        var exactRows = Enumerable.Range( 0, 6 ).Select( index => PromotionRow( "exact:" + index, "sig" ) ).ToArray( );
        SearchCoordinator exact = new( [new PromotionProvider( exactRows )], new SearchOptions( ) );
        var capped = await exact.SearchAsync( new SearchRequest { Query = "sig", Limit = 9 }, default );
        Equal( 3, capped.Count( result => result.Section!.Id == "top-match" ) );
        Equal( 6, capped.Count );
        SearchCoordinator exactPage = new( [new PromotionProvider( [.. exactRows, PromotionRow( "file", "sig notes", ResultKinds.File, SourceIds.Files )] )],
            new SearchOptions { Ranking = new RankingOptions { FirstPageSize = 2, MinimumSourcesOnFirstPage = 2 } } );
        var page = await exactPage.SearchAsync( new SearchRequest { Query = "sig", Limit = 9 }, default );
        Equal( 7, page.Count );
        True( page.Take( 3 ).All( result => result.Section!.Id == "top-match" ) );
    }

    private static async Task LearnedPromotionCapsAndTiesAsync( )
    {
        PromotionHistory history = new( );
        var rows = Enumerable.Range( 0, 12 ).Select( index => PromotionRow( $"app:{index:D2}", "Signal " + index, score: 10 ) ).ToList( );
        rows.Add( PromotionRow( "omarchy", "Signal Installer", ResultKinds.Omarchy, SourceIds.OmarchyControl, 0.2 ) );
        rows.Add( PromotionRow( "file", "Signal Notes", ResultKinds.File, SourceIds.Files, 0.2 ) );
        foreach ( var row in rows.Where( value => value.SourceId == SourceIds.Applications ) )
        {
            history.Add( row.Id );
        }
        history.Add( "app:11", 10 );
        foreach ( var limit in new[] { 1, 2, 3, 9, 20 } )
        {
            foreach ( var sourceCap in new[] { 1, 50 } )
            {
                var options = new SearchOptions { Ranking = new RankingOptions { MaximumResultsPerSource = sourceCap, MinimumSourcesOnFirstPage = 3 } };
                SearchCoordinator coordinator = new( [new PromotionProvider( rows )], options, usageHistory: history, timeProvider: new FixedTimeProvider( PromotionNow ) );
                var result = await coordinator.SearchAsync( new SearchRequest { Query = "sig", Limit = limit }, default );
                True( result.Count <= limit );
                True( result.Count( value => value.Section!.Id == "top-match" ) <= 3 );
                True( result.GroupBy( value => value.SourceId ).All( group => group.Count( ) <= sourceCap ) );
                Equal( result.Count, result.Select( value => value.Id ).Distinct( ).Count( ) );
                Equal( Math.Min( limit, 3 ), result.Take( Math.Min( limit, 9 ) ).Select( value => value.SourceId ).Distinct( ).Count( ) );
                if ( limit >= 3 )
                {
                    Equal( "app:11", result[0].Id );
                }
                SearchCoordinator reversed = new( [new PromotionProvider( rows.AsEnumerable( ).Reverse( ).ToArray( ) )], options, usageHistory: history, timeProvider: new FixedTimeProvider( PromotionNow ) );
                var other = await reversed.SearchAsync( new SearchRequest { Query = "sig", Limit = limit }, default );
                Equal( string.Join( '|', result.Select( value => value.Id ) ), string.Join( '|', other.Select( value => value.Id ) ) );
                foreach ( var value in result )
                {
                    True( Math.Abs( value.Score - value.Ranking.Contributions.Sum( part => part.Value ) ) < 0.000001 );
                }
            }
        }
        rows.Add( PromotionRow( "exact", "sig", score: 0.1 ) );
        rows.Add( PromotionRow( "alias", "Alias", score: 0.1, metadata: new( )
        {
            ["exactAlias"] = "sig"
        } ) );
        SearchCoordinator intent = new( [new PromotionProvider( rows )], new SearchOptions( ), usageHistory: history, timeProvider: new FixedTimeProvider( PromotionNow ) );
        var exact = await intent.SearchAsync( new SearchRequest { Query = "sig", Limit = 3 }, default );
        Equal( "alias", exact[0].Id );
        Equal( "exact", exact[1].Id );
        Equal( "explicit", exact[0].Ranking.Placement!.Tier );
        Equal( "exact", exact[1].Ranking.Placement!.Tier );
    }

    private static async Task LearnedPromotionTogglesAndBatchesAsync( )
    {
        PromotionHistory history = new( );
        var rows = Enumerable.Range( 0, 1001 ).Select( index => PromotionRow( $"app:{index:D4}", "Signal " + index ) ).ToArray( );
        history.Add( rows[^1].Id, 10 );
        SearchCoordinator coordinator = new( [new PromotionProvider( rows )], new SearchOptions( ), usageHistory: history, timeProvider: new FixedTimeProvider( PromotionNow ) );
        var result = await coordinator.SearchAsync( new SearchRequest { Query = "sig", Limit = 1 }, default );
        Equal( rows[^1].Id, result[0].Id );
        Equal( "500|500|1", string.Join( '|', history.BatchSizes ) );
        history.Fail = true;
        result = await coordinator.SearchAsync( new SearchRequest { Query = "sig", Limit = 1 }, default );
        True( result.All( value => value.Section!.Id != "top-match" ) );
        history.Fail = false;
        foreach ( var learn in new[] { false, true } )
        {
            foreach ( var adaptive in new[] { false, true } )
            {
                foreach ( var time in new[] { false, true } )
                {
                    foreach ( var privacy in new[] { false, true } )
                    {
                        SearchOptions options = new( )
                        {
                            LearnFromInvocations = learn,
                            Ranking = new RankingOptions { Adaptive = new AdaptiveRankingOptions { Enabled = adaptive }, TimeAware = new TimeAwareRankingOptions { Enabled = time } }
                        };
                        SearchCoordinator gated = new( [new PromotionProvider( [rows[^1]] )], options, usageHistory: history, timeProvider: new FixedTimeProvider( PromotionNow ), privacy: new PrivacyOptions { StoreUsageHistory = privacy, StoreQueryHistory = true } );
                        result = await gated.SearchAsync( new SearchRequest { Query = "sig" }, default );
                        Equal( learn && adaptive && time && privacy, result[0].Section!.Id == "top-match" );
                    }
                }
            }
        }
        SearchCoordinator rollback = new( [new PromotionProvider( rows.Take( 5 ).Select( row => PromotionRow( row.Id, "Signal" ) ).ToArray( ) )], new SearchOptions { Ranking = new RankingOptions { TopMatches = new TopMatchesOptions { Enabled = false }, MaximumResultsPerSource = 1 } }, usageHistory: history );
        result = await rollback.SearchAsync( new SearchRequest { Query = "signal", Limit = 9 }, default );
        Equal( 1, result.Count );
        True( result.All( value => value.Section!.Id == SourceIds.Applications ) );
    }
}
