namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task PerKindTimeAwareAndContextRankingAreControlledAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-time-context-{Guid.NewGuid( ):N}.db" );
        MutableTimeProvider clock = new(
            new DateTimeOffset( 2026, 1, 1, 12, 0, 0, TimeSpan.Zero ) );
        SearchOptions options = new( )
        {
            Ranking = new RankingOptions
            {
                TimeAware = new TimeAwareRankingOptions
                {
                    ApplicationMultiplier = 1,
                    FileMultiplier = 0.75,
                    WorkspaceMultiplier = 1.25,
                    ActionMultiplier = 0.5
                }
            }
        };
        PrivacyOptions privacy = new( )
        {
            StoreUsageHistory = true,
            NeverIndex = []
        };
        try
        {
            await using SqliteCatalog catalog = new(
                new StorageOptions { DatabasePath = database },
                clock,
                privacy,
                options );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( TimeContextItems( ), null, default );
            foreach ( var id in new[]
            {
                "app:time", "file:time", "command:time", "workspace:time"
            } )
            {
                await catalog.UsageHistory.RecordInvocationAsync( id, "open", null, default );
            }

            SearchCoordinator coordinator = new(
                [new CatalogSearchProvider( catalog ), new FixedWorkspaceProvider( )],
                options,
                catalog,
                preferences: null,
                usageHistory: catalog.UsageHistory,
                timeProvider: clock );
            var initial = await coordinator.SearchAsync(
                new SearchRequest { Query = "time target", Limit = 20 },
                default );
            foreach ( (var id, var category, var multiplier) in new[]
            {
                ("app:time", "application", 1d),
                ("file:time", "file", 0.75d),
                ("workspace:time", "workspace", 1.25d),
                ("command:time", "action", 0.5d)
            } )
            {
                var result = initial.Single( value => value.Id == id );
                var contribution = result.Ranking.Contributions.Single( value =>
                    value.Kind == RankingContributionKinds.Frecency );
                True( contribution.Label.Contains( category, StringComparison.Ordinal ) );
                True( contribution.Label.Contains( $"×{multiplier:0.##}", StringComparison.Ordinal ) );
                var expected = TimeAwareRankingPolicy.Score(
                    result.Kind,
                    1,
                    clock.GetUtcNow( ).ToUnixTimeMilliseconds( ),
                    clock.GetUtcNow( ),
                    options.Ranking.TimeAware ) * 0.82;
                True( Math.Abs( contribution.Value - expected ) < 0.000_001 );
                True( Math.Abs( result.Ranking.Contributions.Sum( static value => value.Value ) -
                    result.Score ) < 0.000_001 );
            }

            var beforeDecay = initial.Single( static value => value.Id == "workspace:time" )
                .Ranking.Contributions.Single( static value =>
                    value.Kind == RankingContributionKinds.Frecency ).Value;
            clock.Advance( TimeSpan.FromDays( 21 ) );
            var afterDecay = (await coordinator.SearchAsync(
                new SearchRequest { Query = "time target", Limit = 20 },
                default )).Single( static value => value.Id == "workspace:time" )
                .Ranking.Contributions.Single( static value =>
                    value.Kind == RankingContributionKinds.Frecency ).Value;
            True( afterDecay < beforeDecay );

            await catalog.UsageHistory.ResetRankingLearningAsync( default );
            var reset = await coordinator.SearchAsync(
                new SearchRequest { Query = "time target", Limit = 20 },
                default );
            True( reset.All( static result => result.Ranking.Contributions.All( static value =>
                value.Kind != RankingContributionKinds.Frecency ) ) );

            await VerifyContextSwitchAsync( catalog, clock );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task VerifyContextSwitchAsync( SqliteCatalog catalog, TimeProvider clock )
    {
        SearchOptions enabledOptions = new( );
        SearchCoordinator enabled = new(
            [new CatalogSearchProvider( catalog )],
            enabledOptions,
            timeProvider: clock );
        var contextual = await enabled.SearchAsync(
            new SearchRequest
            {
                Query = "context report",
                Limit = 20,
                CurrentDirectory = "/projects/b",
                SelectedProject = "/projects/b"
            },
            default );
        Equal( "file:context-b", contextual[0].Id );
        True( contextual[0].Ranking.Contributions.Count( value =>
            value.Kind == RankingContributionKinds.Context ) == 2 );

        SearchOptions disabledOptions = new( )
        {
            Ranking = new RankingOptions
            {
                Context = new ContextRankingOptions { Enabled = false }
            }
        };
        ContextCaptureProvider capture = new( );
        SearchCoordinator disabled = new(
            [new CatalogSearchProvider( catalog ), capture],
            disabledOptions,
            timeProvider: clock );
        var withoutContext = (await disabled.SearchAsync(
            new SearchRequest { Query = "context report", Limit = 20 },
            default )).Select( static value => value.Id ).ToArray( );
        var ignoredContext = (await disabled.SearchAsync(
            new SearchRequest
            {
                Query = "context report",
                Limit = 20,
                ActiveApplication = "untrusted-app",
                ActiveWorkspace = "99",
                CurrentDirectory = "/projects/b",
                SelectedProject = "/projects/b"
            },
            default )).Select( static value => value.Id ).ToArray( );
        Equal( string.Join( '|', withoutContext ), string.Join( '|', ignoredContext ) );
        True( capture.LastRequest is not null );
        Equal( null, capture.LastRequest!.ActiveApplication );
        Equal( null, capture.LastRequest.ActiveWorkspace );
        Equal( null, capture.LastRequest.CurrentDirectory );
        Equal( null, capture.LastRequest.SelectedProject );
        True( (await disabled.SearchAsync(
            new SearchRequest
            {
                Query = "context report",
                Limit = 20,
                SelectedProject = "/projects/b"
            },
            default )).All( static result => result.Ranking.Contributions.All( static value =>
                value.Kind != RankingContributionKinds.Context ) ) );
    }

    private static async IAsyncEnumerable<IndexedItem> TimeContextItems( )
    {
        yield return TimeItem( "app:time", ResultKinds.Application, "Time Target App", null,
            SourceIds.Applications );
        yield return TimeItem( "file:time", ResultKinds.File, "Time Target File", "/projects/a/time.txt",
            SourceIds.Files );
        yield return TimeItem( "command:time", ResultKinds.CustomCommand, "Time Target Action", null,
            SourceIds.CommandsQuicklinks );
        yield return TimeItem( "file:context-a", ResultKinds.File, "Context Report", "/projects/a/report.txt",
            SourceIds.Files );
        yield return TimeItem( "file:context-b", ResultKinds.File, "Context Report", "/projects/b/report.txt",
            SourceIds.Files );
        await Task.CompletedTask;
    }

    private static IndexedItem TimeItem(
        string id,
        string kind,
        string title,
        string? path,
        string sourceId ) => new( )
        {
            StableId = id,
            Kind = kind,
            Title = title,
            Path = path,
            ProviderId = "time-context-spec",
            SourceId = sourceId
        };

    private sealed class FixedWorkspaceProvider : ICommandProvider
    {
        public string Id => "time-context-live";

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken ) =>
            ValueTask.FromResult<IReadOnlyList<SearchResult>>
            ( [
                new SearchResult
                {
                    Id = "workspace:time",
                    Kind = ResultKinds.Workspace,
                    Title = "Time Target Workspace",
                    ProviderId = Id,
                    SourceId = SourceIds.WindowsWorkspaces,
                    Score = 0.7,
                    Actions = [new CommandAction { Id = "focus", Title = "Focus", Kind = "focus" }]
                }
            ] );
    }

    private sealed class ContextCaptureProvider : ICommandProvider
    {
        public string Id => "context-capture";
        public SearchRequest? LastRequest { get; private set; }

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken )
        {
            LastRequest = request;
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }
    }

    // A ranking control outside its range is reported by its configuration name
    // and value, not as one generic "outside supported bounds" for about twenty.
    private static void RankingValidationNamesTheSettingOutOfRange( )
    {
        static string Rejection( RankingOptions ranking )
        {
            try
            {
                ConfigurationValidator.Validate( new CommandDeckOptions { Search = new SearchOptions { Ranking = ranking } } );
            }
            catch ( InvalidOperationException exception )
            {
                return exception.Message;
            }

            return "accepted";
        }

        Equal( "search.ranking.titleWeight must be between 0.01 and 100; it is 250.",
            Rejection( new RankingOptions { TitleWeight = 250 } ) );
        Equal( "search.ranking.context.selectedProjectBoost must be between 0 and 0.5; it is 0.9.",
            Rejection( new RankingOptions { Context = new ContextRankingOptions { SelectedProjectBoost = 0.9 } } ) );
        Equal( "search.ranking.adaptive.retentionDays must be between 1 and 3650; it is 0.",
            Rejection( new RankingOptions { Adaptive = new AdaptiveRankingOptions { RetentionDays = 0 } } ) );
        Equal( "search.ranking.minimumSourcesOnFirstPage must be between 1 and 4; it is 5.",
            Rejection( new RankingOptions { FirstPageSize = 4, MinimumSourcesOnFirstPage = 5 } ) );
        Equal( "search.ranking.sourceWeights.files must be between 0.1 and 5; it is 9.",
            Rejection( new RankingOptions { SourceWeights = new( StringComparer.Ordinal ) { ["files"] = 9 } } ) );
        Equal( "search.ranking.titleWeight must be between 0.01 and 100; it is NaN.",
            Rejection( new RankingOptions { TitleWeight = double.NaN } ) );
        Equal( "search.ranking.sourceWeights.files must be between 0.1 and 5; it is NaN.",
            Rejection( new RankingOptions { SourceWeights = new( StringComparer.Ordinal ) { ["files"] = double.NaN } } ) );
        Equal( "accepted", Rejection( new RankingOptions( ) ) );
    }
}
