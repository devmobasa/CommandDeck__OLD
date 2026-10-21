using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly JsonSerializerOptions QualityJsonOptions = new( )
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private const int QualityFixtureItems = 100_000;
    private const double SearchLatencyBudgetMilliseconds = 20;
    private const double SearchAllocationBudgetBytes = 2_000_000;
    private const double StartupBudgetMilliseconds = 2_000;
    private const double DatabaseBytesPerItemBudget = 2_048;

    private static async Task<int> RunQualityBenchmarkAsync( string? outputPath, bool observeLatency )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-quality-benchmark-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            var database = Path.Combine( root, "catalog.db" );
            SearchOptions searchOptions = new( )
            {
                ProviderTimeoutMilliseconds = 5_000
            };
            await using SqliteCatalog catalog = new(
                new StorageOptions { DatabasePath = database },
                searchOptions: searchOptions );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync(
                PhaseThreeBenchmarkItems( QualityFixtureItems ),
                null,
                CancellationToken.None );
            SearchCoordinator coordinator = new(
                [new CatalogSearchProvider( catalog )],
                searchOptions );
            SearchRequest request = new( )
            {
                Query = "Benchmark 4242",
                Limit = 20
            };

            var searchP95 = await MeasureP95Async( async ( ) =>
                _ = await coordinator.SearchAsync( request, CancellationToken.None ) );
            for ( var index = 0; index < 10; index++ )
            {
                _ = await coordinator.SearchAsync( request, CancellationToken.None );
            }
            GC.Collect( );
            GC.WaitForPendingFinalizers( );
            var allocationStart = GC.GetTotalAllocatedBytes( precise: true );
            const int allocationSamples = 50;
            for ( var index = 0; index < allocationSamples; index++ )
            {
                _ = await coordinator.SearchAsync( request, CancellationToken.None );
            }
            var allocatedBytesPerSearch =
                (GC.GetTotalAllocatedBytes( precise: true ) - allocationStart) /
                ( double ) allocationSamples;

            await CheckpointAsync( database );
            var databaseBytes = new FileInfo( database ).Length +
                                FileSize( database + "-wal" );
            var databaseBytesPerItem = databaseBytes / ( double ) QualityFixtureItems;

            var config = Path.Combine( root, "config.json" );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = database },
                Search = searchOptions,
                Indexing = new IndexingOptions
                {
                    Roots = [],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false,
                    EnableIncrementalIndexing = false,
                    EnableScheduledMaintenance = false
                },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            await File.WriteAllTextAsync(
                config,
                JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            var startupP95 = await MeasureProcessStartupP95Async( config );
            var sparseFuzzyP95 = await MeasureSparseFuzzyLatencyAsync( root );
            var ranking = await MeasureRankingQualityForGateAsync( );
            var relevance = await MeasureRelevanceForGateAsync( );
            var decision = EvaluateQualityGate( ranking.TopOneAccuracy, ranking.MeanReciprocalRank,
                searchP95, sparseFuzzyP95, allocatedBytesPerSearch, startupP95, databaseBytesPerItem, observeLatency );

            await using SqliteCatalog realistic = new(
                new StorageOptions { DatabasePath = Path.Combine( root, "realistic.db" ) },
                searchOptions: searchOptions );
            await realistic.InitializeAsync( CancellationToken.None );
            await realistic.ReplaceAllAsync( RealisticQualityItems( QualityFixtureItems ), null, CancellationToken.None );
            await SeedQualityUsageAsync( realistic.DatabasePath );
            var runtimeClasses = await MeasureQualitySearchClassesAsync( realistic,
                new CommandDeckOptions
                {
                    Storage = new StorageOptions { DatabasePath = realistic.DatabasePath },
                    Search = searchOptions,
                    Ai = new AiOptions { Enabled = false, Providers = [] }
                } );

            QualityGateReport report = new( )
            {
                Version = ProtocolLimits.CurrentVersion,
                RuntimeSearchClasses = runtimeClasses,
                FixtureItems = QualityFixtureItems,
                RankingCases = ranking.CaseCount,
                RankingTopOneAccuracy = ranking.TopOneAccuracy,
                RankingMeanReciprocalRank = ranking.MeanReciprocalRank,
                RelevanceCaseCount = relevance.CaseCount,
                RelevancePassed = relevance.Passed,
                RelevanceTopOneAccuracy = relevance.TopOneAccuracy,
                RelevanceMeanReciprocalRank = relevance.MeanReciprocalRank,
                RelevanceVisibleRecall = relevance.VisibleRecall,
                RankingMinimumTopOneAccuracy = 1,
                RankingMinimumMeanReciprocalRank = 1,
                SearchP95Milliseconds = searchP95,
                SearchP95BudgetMilliseconds = SearchLatencyBudgetMilliseconds,
                SparseFuzzyFixtureItems = 100_576,
                SparseFuzzyP95Milliseconds = sparseFuzzyP95,
                SparseFuzzyP95BudgetMilliseconds = SearchLatencyBudgetMilliseconds,
                SearchAllocatedBytes = allocatedBytesPerSearch,
                SearchAllocationBudgetBytes = SearchAllocationBudgetBytes,
                StartupP95Milliseconds = startupP95,
                StartupBudgetMilliseconds = StartupBudgetMilliseconds,
                DatabaseBytes = databaseBytes,
                DatabaseBytesPerItem = databaseBytesPerItem,
                DatabaseBytesPerItemBudget = DatabaseBytesPerItemBudget,
                RankingAndFootprintPassed = decision.RankingAndFootprintPassed,
                LatencyPassed = decision.LatencyPassed,
                LatencyEnforced = !observeLatency,
                Passed = decision.Passed
            };
            var json = JsonSerializer.Serialize( report, QualityJsonOptions );
            if ( !string.IsNullOrWhiteSpace( outputPath ) )
            {
                var fullPath = Path.GetFullPath( outputPath );
                Directory.CreateDirectory( Path.GetDirectoryName( fullPath )! );
                await File.WriteAllTextAsync( fullPath, json + Environment.NewLine );
            }
            Console.WriteLine( json );
            return report.Passed ? 0 : 1;
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private readonly record struct QualityGateDecision(
        bool RankingAndFootprintPassed,
        bool LatencyPassed,
        bool Passed );

    private static QualityGateDecision EvaluateQualityGate(
        double topOneAccuracy,
        double meanReciprocalRank,
        double searchP95,
        double sparseFuzzyP95,
        double allocatedBytesPerSearch,
        double startupP95,
        double databaseBytesPerItem,
        bool observeLatency )
    {
        var rankingAndFootprintPassed = topOneAccuracy >= 1 && meanReciprocalRank >= 1 &&
            allocatedBytesPerSearch < SearchAllocationBudgetBytes &&
            databaseBytesPerItem < DatabaseBytesPerItemBudget;
        var latencyPassed = searchP95 < SearchLatencyBudgetMilliseconds &&
            sparseFuzzyP95 < SearchLatencyBudgetMilliseconds &&
            startupP95 < StartupBudgetMilliseconds;

        return new QualityGateDecision( rankingAndFootprintPassed, latencyPassed,
            rankingAndFootprintPassed && (observeLatency || latencyPassed) );
    }

    private static void QualityGateKeepsHostedLatencyObservational( )
    {
        var enforced = EvaluateQualityGate( 1, 1, 40, 10, 280_000, 1_500, 550, observeLatency: false );
        True( enforced.RankingAndFootprintPassed );
        True( !enforced.LatencyPassed );
        True( !enforced.Passed );

        var observed = EvaluateQualityGate( 1, 1, 40, 10, 280_000, 1_500, 550, observeLatency: true );
        True( observed.Passed );
        True( !observed.LatencyPassed );

        True( !EvaluateQualityGate( 1, 1, 10, 40, 280_000, 1_500, 550, observeLatency: false ).Passed );
        var sparseObserved = EvaluateQualityGate( 1, 1, 10, 40, 280_000, 1_500, 550, observeLatency: true );
        True( sparseObserved.Passed && !sparseObserved.LatencyPassed );
        True( !EvaluateQualityGate( 0.9, 1, 10, 10, 280_000, 1_500, 550, observeLatency: true ).Passed );
        True( !EvaluateQualityGate( 1, 1, 10, 10, 2_000_000, 1_500, 550, observeLatency: true ).Passed );
        True( !EvaluateQualityGate( 1, 1, 10, 10, 280_000, 1_500, 2_048, observeLatency: true ).Passed );
    }

    private static async Task CheckpointAsync( string database )
    {
        await using SqliteConnection connection = new( $"Data Source={database}" );
        await connection.OpenAsync( );
        await using var command = connection.CreateCommand( );
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await command.ExecuteNonQueryAsync( );
    }

    private static long FileSize( string path ) => File.Exists( path ) ? new FileInfo( path ).Length : 0;

    private static async Task<double> MeasureProcessStartupP95Async( string config )
    {
        var samples = new double[5];
        for ( var index = 0; index < samples.Length; index++ )
        {
            ProcessStartInfo start = new( )
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add( typeof( CommandDeck.Program ).Assembly.Location );
            start.ArgumentList.Add( "status" );
            start.ArgumentList.Add( "--json" );
            start.ArgumentList.Add( "--config" );
            start.ArgumentList.Add( config );
            using Process process = new( )
            {
                StartInfo = start
            };
            var started = Stopwatch.GetTimestamp( );
            True( process.Start( ) );
            var output = process.StandardOutput.ReadToEndAsync( );
            var error = process.StandardError.ReadToEndAsync( );
            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 10 ) );
            await process.WaitForExitAsync( timeout.Token );
            _ = await output;
            _ = await error;
            Equal( 0, process.ExitCode );
            samples[index] = Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
        }
        Array.Sort( samples );
        return samples[4];
    }

    private sealed class QualityGateReport
    {
        public IReadOnlyList<QualitySearchMeasurement> RuntimeSearchClasses { get; init; } = [];
        public int Version { get; init; }
        public int FixtureItems { get; init; }
        public int RankingCases { get; init; }
        public double RankingTopOneAccuracy { get; init; }
        public double RankingMeanReciprocalRank { get; init; }
        public int RelevanceCaseCount { get; init; }
        public int RelevancePassed { get; init; }
        public double RelevanceTopOneAccuracy { get; init; }
        public double RelevanceMeanReciprocalRank { get; init; }
        public double RelevanceVisibleRecall { get; init; }
        public double RankingMinimumTopOneAccuracy { get; init; }
        public double RankingMinimumMeanReciprocalRank { get; init; }
        public double SearchP95Milliseconds { get; init; }
        public double SearchP95BudgetMilliseconds { get; init; }
        public int SparseFuzzyFixtureItems { get; init; }
        public double SparseFuzzyP95Milliseconds { get; init; }
        public double SparseFuzzyP95BudgetMilliseconds { get; init; }
        public double SearchAllocatedBytes { get; init; }
        public double SearchAllocationBudgetBytes { get; init; }
        public double StartupP95Milliseconds { get; init; }
        public double StartupBudgetMilliseconds { get; init; }
        public long DatabaseBytes { get; init; }
        public double DatabaseBytesPerItem { get; init; }
        public double DatabaseBytesPerItemBudget { get; init; }
        public bool Passed { get; init; }
        public bool RankingAndFootprintPassed { get; init; }
        public bool LatencyPassed { get; init; }
        public bool LatencyEnforced { get; init; }
    }
}
