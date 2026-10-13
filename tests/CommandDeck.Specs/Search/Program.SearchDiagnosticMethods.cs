namespace CommandDeck.Specs;

using System.Collections.Concurrent;
using System.Diagnostics;

public static partial class Program
{
    private static readonly SearchResult BraveApplication = new( )
    {
        Id = "app:brave.desktop",
        Kind = ResultKinds.Application,
        Title = "Brave",
        ProviderId = "applications",
        SourceId = SourceIds.Applications
    };

    // A provider that ignores cancellation must not hold the response past the
    // deadline, and its late result must be dropped and reported exactly once.
    private static async Task SearchReportsProviderDeadlinesAsync( )
    {
        RecordingApplicationCatalog catalog = new( BraveApplication );
        UncooperativeProvider blocking = new( );
        ConcurrentQueue<SearchProviderDiagnostic> diagnostics = [];
        SearchCoordinator coordinator = new(
            [blocking, new ApplicationSearchProvider( catalog )],
            new SearchOptions { ProviderTimeoutMilliseconds = 25 },
            providerDiagnostic: diagnostics.Enqueue );

        var elapsed = Stopwatch.StartNew( );
        var results = await coordinator.SearchAsync(
            new SearchRequest { Query = "browser", Limit = 10 },
            CancellationToken.None );
        elapsed.Stop( );

        True( elapsed.Elapsed < TimeSpan.FromMilliseconds( 300 ) );
        True( results.Any( result => result.Id == BraveApplication.Id ) );
        True( results.All( result => result.Id != UncooperativeProvider.LateResultId ) );
        Equal( SourceIds.Applications, catalog.LastSourceIds.Single( ) );
        True( diagnostics.Any( diagnostic =>
            diagnostic.ProviderId == SearchCoordinator.ApplicationFastPathProviderId &&
            diagnostic.Outcome == SearchProviderOutcomes.Completed &&
            diagnostic.ResultCount == 1 ) );
        Equal( 1, diagnostics.Count( diagnostic => diagnostic.ProviderId == blocking.Id ) );
        Equal(
            SearchProviderOutcomes.TimedOut,
            diagnostics.Single( diagnostic => diagnostic.ProviderId == blocking.Id ).Outcome );

        await blocking.Finished.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
        await Task.Delay( 50 );

        Equal( 1, diagnostics.Count( diagnostic => diagnostic.ProviderId == blocking.Id ) );
    }

    private static async Task SearchSkipsApplicationFastPathWhenFilteredOutAsync( )
    {
        RecordingApplicationCatalog catalog = new( BraveApplication );
        SearchCoordinator coordinator = new(
            [new ApplicationSearchProvider( catalog )],
            new SearchOptions( ) );

        foreach ( var query in new[] { "-app: brave", "file: brave", "> brave" } )
        {
            await coordinator.SearchAsync(
                new SearchRequest { Query = query, Limit = 10 },
                CancellationToken.None );
        }

        Equal( 0, catalog.SourceSearchCount );

        var results = await coordinator.SearchAsync(
            new SearchRequest { Query = "brave", Limit = 10 },
            CancellationToken.None );

        Equal( 1, catalog.SourceSearchCount );
        True( results.Any( result => result.Id == BraveApplication.Id ) );
    }

    // The application-only path and the full catalog path return the same stable
    // ID. A clamped score can tie the fixed exact-alias score; the alias copy wins.
    private static async Task SearchKeepsExactAliasCopyOnTiedScoreAsync( )
    {
        SearchResult clamped = new( BraveApplication )
        {
            Score = 1.5
        };
        SearchResult aliased = new( BraveApplication )
        {
            Score = 0.3,
            Metadata = new Dictionary<string, string>( StringComparer.Ordinal )
            {
                ["exactAlias"] = "brave"
            }
        };
        SearchCoordinator coordinator = new(
            [new StaticSearchProvider( [clamped] ), new StaticSearchProvider( [aliased] )],
            new SearchOptions( ) );

        var results = await coordinator.SearchAsync(
            new SearchRequest { Query = "brave", Limit = 10 },
            CancellationToken.None );

        var survivor = results.Single( result => result.Id == BraveApplication.Id );
        Equal( 1.5, survivor.Score );
        True( survivor.Metadata.ContainsKey( "exactAlias" ) );
    }

    private static void SearchLoggingChoosesLevels( )
    {
        List<(string Level, string Message)> lines = [];
        var verbose = AppLog.Verbose;
        AppLog.Sink = ( level, message ) => lines.Add( (level, message) );
        AppLog.Verbose = true;

        try
        {
            var oneSecondAgo = Stopwatch.GetTimestamp( ) - Stopwatch.Frequency;
            SearchLogging.LogSearchCompletion( 1, 5, oneSecondAgo, 2, SearchLogging.CompletedStatus );
            SearchLogging.LogSearchCompletion( 2, 5, Stopwatch.GetTimestamp( ), 2, SearchLogging.CompletedStatus );
            SearchLogging.LogSearchCompletion( 3, 5, Stopwatch.GetTimestamp( ), 0, SearchLogging.CancelledStatus );
            SearchLogging.LogSearchCompletion(
                4,
                5,
                Stopwatch.GetTimestamp( ),
                0,
                SearchLogging.FailedStatus,
                nameof( IOException ) );
            SearchLogging.LogProviderDiagnostic( ProviderDiagnostic( SearchProviderOutcomes.TimedOut, 25 ) );
            SearchLogging.LogProviderDiagnostic( ProviderDiagnostic( SearchProviderOutcomes.Failed, 3, "IOException" ) );
            SearchLogging.LogProviderDiagnostic( ProviderDiagnostic( SearchProviderOutcomes.Completed, 150 ) );
            SearchLogging.LogProviderDiagnostic( ProviderDiagnostic( SearchProviderOutcomes.Completed, 5 ) );
            SearchLogging.LogProviderDiagnostic( ProviderDiagnostic( SearchProviderOutcomes.Cancelled, 5 ) );
        }
        finally
        {
            AppLog.Sink = null;
            AppLog.Verbose = verbose;
        }

        Equal( 9, lines.Count );
        Equal( "info", lines[0].Level );
        True( lines[0].Message.StartsWith( "event=search.complete request_id=1 query_chars=5 duration_ms=", StringComparison.Ordinal ) );
        True( lines[0].Message.EndsWith( " result_count=2 status=completed", StringComparison.Ordinal ) );
        Equal( "debug", lines[1].Level );
        Equal( "debug", lines[2].Level );
        True( lines[2].Message.EndsWith( " status=cancelled", StringComparison.Ordinal ) );
        Equal( "warning", lines[3].Level );
        True( lines[3].Message.EndsWith( " status=failed error_type=IOException", StringComparison.Ordinal ) );
        Equal( "warning", lines[4].Level );
        Equal( "event=provider.timeout provider=catalog duration_ms=25 result_count=0", lines[4].Message );
        Equal( "warning", lines[5].Level );
        Equal( "event=provider.failure provider=catalog duration_ms=3 result_count=0 error_type=IOException", lines[5].Message );
        Equal( "info", lines[6].Level );
        Equal( "debug", lines[7].Level );
        Equal( "debug", lines[8].Level );
    }

    private static SearchProviderDiagnostic ProviderDiagnostic(
        string outcome,
        long durationMilliseconds,
        string? errorType = null ) => new( )
        {
            ProviderId = "catalog",
            Outcome = outcome,
            DurationMilliseconds = durationMilliseconds,
            ResultCount = 0,
            ErrorType = errorType
        };

    private sealed class UncooperativeProvider : ICommandProvider
    {
        public const string LateResultId = "file:late-result";

        public TaskCompletionSource Finished { get; } =
            new( TaskCreationOptions.RunContinuationsAsynchronously );

        public string Id => "blocking";

        public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            SearchRequest request,
            CancellationToken cancellationToken )
        {
            await Task.Delay( 400, CancellationToken.None );
            Finished.TrySetResult( );

            return
            [
                new SearchResult
                {
                    Id = LateResultId,
                    Kind = ResultKinds.File,
                    Title = "late",
                    ProviderId = "catalog",
                    SourceId = SourceIds.Files
                }
            ];
        }
    }

    private sealed class RecordingApplicationCatalog( SearchResult result ) : ICatalogQueryStore
    {
        public IReadOnlyList<string> LastSourceIds { get; private set; } = [];
        public int SourceSearchCount { get; private set; }

        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
            ParsedQuery query,
            int limit,
            CancellationToken cancellationToken ) =>
            ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );

        public ValueTask<IReadOnlyList<SearchResult>> SearchSourcesAsync(
            ParsedQuery query,
            IReadOnlyList<string> sourceIds,
            int limit,
            CancellationToken cancellationToken )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            LastSourceIds = sourceIds;
            SourceSearchCount++;

            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [result] );
        }

        public ValueTask<IReadOnlyList<SearchResult>> GetHomeResultsAsync(
            LauncherPreferences preferences,
            int limit,
            CancellationToken cancellationToken ) =>
            ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
    }
}
