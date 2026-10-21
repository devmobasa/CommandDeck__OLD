namespace CommandDeck.Specs;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

public static partial class Program
{
    private static async Task<int> RunPhase3BenchmarkAsync( )
    {
        const int itemCount = 100_000;
        const int usageCount = 10_000;
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-phase3-benchmark-{Guid.NewGuid( ):N}.db" );
        try
        {
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync(
                PhaseThreeBenchmarkItems( itemCount ),
                null,
                CancellationToken.None );

            await using ( SqliteConnection connection = new( $"Data Source={database}" ) )
            {
                await connection.OpenAsync( );
                await using var seedUsage = connection.CreateCommand( );
                seedUsage.CommandText = """
                    INSERT INTO usage(stable_id, use_count, last_used_unix_ms)
                    SELECT stable_id,
                           ((ordinal * 7) % 25) + 1,
                           unixepoch('now') * 1000 - ((ordinal * 17) % 61) * 86400000
                    FROM (
                        SELECT stable_id,
                               ROW_NUMBER() OVER (ORDER BY stable_id) AS ordinal
                        FROM catalog_items
                        LIMIT 10000
                    );
                    """;
                await seedUsage.ExecuteNonQueryAsync( );
            }

            var aliasTarget = (await catalog.SearchAsync(
                QueryParser.Parse( "Benchmark 4242" ),
                50,
                CancellationToken.None )).First( value => value.Id == "benchmark:4242" );
            await catalog.SetAliasAsync( aliasTarget, "bench", CancellationToken.None );

            var directQuery = QueryParser.Parse( "Benchmark 4242" );
            var diagnosticCandidates = await catalog.SearchAsync(
                directQuery,
                160,
                CancellationToken.None );
            Console.WriteLine( "candidate sources: " + string.Join(
                ", ",
                diagnosticCandidates.GroupBy( static value => value.SourceId )
                    .Select( static group => $"{group.Key}={group.Count( )}" ) ) );
            SearchOptions benchmarkOptions = new( )
            {
                ProviderTimeoutMilliseconds = 5_000
            };
            SearchCoordinator baselineCoordinator = new(
                [new StaticSearchProvider( diagnosticCandidates )],
                benchmarkOptions );
            SearchCoordinator configuredCoordinator = new(
                [new StaticSearchProvider( diagnosticCandidates )],
                benchmarkOptions,
                preferences: catalog );

            var bootstrapP95 = await MeasureP95Async( async ( ) =>
                _ = await catalog.GetSnapshotAsync( false, CancellationToken.None ) );
            var aliasP95 = await MeasureP95Async( async ( ) =>
                _ = await catalog.FindAliasAsync( "bench", CancellationToken.None ) );
            var homeP95 = await MeasureP95Async( async ( ) =>
                _ = await catalog.GetHomeResultsAsync(
                    await catalog.GetPreferencesAsync( CancellationToken.None ),
                    20,
                    CancellationToken.None ) );
            var baselineP95 = await MeasureP95Async( async ( ) =>
                _ = await baselineCoordinator.SearchAsync(
                    new SearchRequest { Query = "Benchmark 4242", Limit = 50 },
                    CancellationToken.None ) );
            var configuredP95 = await MeasureP95Async( async ( ) =>
                _ = await configuredCoordinator.SearchAsync(
                    new SearchRequest { Query = "Benchmark 4242", Limit = 50 },
                    CancellationToken.None ) );
            var sourcePreferenceOverhead = await MeasurePairedDeltaP95Async(
                async ( ) => _ = await baselineCoordinator.SearchAsync(
                    new SearchRequest { Query = "Benchmark 4242", Limit = 50 },
                    CancellationToken.None ),
                async ( ) => _ = await configuredCoordinator.SearchAsync(
                    new SearchRequest { Query = "Benchmark 4242", Limit = 50 },
                    CancellationToken.None ) );

            var cpu = File.ReadLines( "/proc/cpuinfo" )
                .FirstOrDefault( static line => line.StartsWith( "model name", StringComparison.Ordinal ) )?
                .Split( ':', 2 )[1]
                .Trim( ) ?? "unknown";
            Console.WriteLine( $"fixture: {itemCount} catalog rows, {usageCount} usage rows" );
            Console.WriteLine( $"hardware: {cpu}; {Environment.ProcessorCount} logical CPUs; {RuntimeInformation.ProcessArchitecture}" );
            Console.WriteLine( "samples: 10 warmups + 100 warm samples per operation" );
            Console.WriteLine( $"bootstrap p95: {bootstrapP95:F3} ms (budget < 50 ms)" );
            Console.WriteLine( $"exact alias p95: {aliasP95:F3} ms (budget < 2 ms)" );
            Console.WriteLine( $"home query p95: {homeP95:F3} ms (budget < 20 ms)" );
            Console.WriteLine( $"baseline coordinator p95: {baselineP95:F3} ms" );
            Console.WriteLine( $"configured search p95: {configuredP95:F3} ms" );
            Console.WriteLine( $"paired source-preference delta p95: {sourcePreferenceOverhead:F3} ms (budget < 5 ms)" );

            var passed = bootstrapP95 < 50 &&
                         aliasP95 < 2 &&
                         homeP95 < 20 &&
                         sourcePreferenceOverhead < 5;
            Console.WriteLine( passed ? "Phase 3 performance budgets passed." : "Phase 3 performance budget failed." );
            await catalog.DisposeAsync( );
            return passed ? 0 : 1;
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task<double> MeasureP95Async( Func<Task> operation )
    {
        for ( var index = 0; index < 10; index++ )
        {
            await operation( );
        }

        var milliseconds = new double[100];
        for ( var index = 0; index < milliseconds.Length; index++ )
        {
            var started = Stopwatch.GetTimestamp( );
            await operation( );
            milliseconds[index] = Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
        }

        Array.Sort( milliseconds );
        return milliseconds[94];
    }

    private static async Task<double> MeasurePairedDeltaP95Async(
        Func<Task> baseline,
        Func<Task> configured )
    {
        for ( var index = 0; index < 10; index++ )
        {
            await baseline( );
            await configured( );
        }

        var deltas = new double[100];
        for ( var index = 0; index < deltas.Length; index++ )
        {
            var configuredFirst = index % 2 != 0;
            var firstStarted = Stopwatch.GetTimestamp( );
            await (configuredFirst ? configured( ) : baseline( ));
            var first = Stopwatch.GetElapsedTime( firstStarted ).TotalMilliseconds;
            var secondStarted = Stopwatch.GetTimestamp( );
            await (configuredFirst ? baseline( ) : configured( ));
            var second = Stopwatch.GetElapsedTime( secondStarted ).TotalMilliseconds;
            deltas[index] = configuredFirst ? first - second : second - first;
        }

        Array.Sort( deltas );
        return deltas[94];
    }

    private static async IAsyncEnumerable<IndexedItem> PhaseThreeBenchmarkItems( int count )
    {
        string[] sources =
        [
            SourceIds.OmarchyControl,
            SourceIds.Applications,
            SourceIds.Files,
            SourceIds.Configuration,
            SourceIds.CommandsQuicklinks
        ];
        for ( var index = 0; index < count; index++ )
        {
            var source = sources[index % sources.Length];
            var kind = source switch
            {
                SourceIds.Applications => ResultKinds.Application,
                SourceIds.Files => ResultKinds.File,
                SourceIds.Configuration => ResultKinds.Configuration,
                SourceIds.CommandsQuicklinks => ResultKinds.CustomCommand,
                _ => ResultKinds.Omarchy
            };
            yield return new IndexedItem
            {
                StableId = $"benchmark:{index}",
                Kind = kind,
                Title = $"Benchmark {index}",
                Subtitle = $"Phase 3 fixture row {index}",
                Path = source is SourceIds.Files or SourceIds.Configuration
                    ? $"/tmp/commanddeck-benchmark/{index}"
                    : null,
                ProviderId = "benchmark",
                SourceId = source
            };
            if ( index % 1_000 == 0 )
            {
                await Task.Yield( );
            }
        }
    }

}
