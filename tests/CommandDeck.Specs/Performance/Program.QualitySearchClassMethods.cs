using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private const int QualitySearchSamples = 40;
    private const int QualityAllocationSamples = 20;

    // Provisional budgets from the reviewed donor; these classes are reported
    // separately until measured here. Existing point-query gates remain enforced.
    private static readonly QualitySearchClass[] QualitySearchClasses =
    [
        new( "one-letter prefix", "s", P95BudgetMilliseconds: 300, AllocationBudgetBytes: 4_200_000 ),
        new( "two-letter prefix", "sy", P95BudgetMilliseconds: 80, AllocationBudgetBytes: 3_300_000 ),
        new( "word prefix", "sys", P95BudgetMilliseconds: 60, AllocationBudgetBytes: 3_300_000 ),
        new( "typo", "sytsem", P95BudgetMilliseconds: 80, AllocationBudgetBytes: 600_000 ),
        new( "no match", "xzqv", P95BudgetMilliseconds: 20, AllocationBudgetBytes: 250_000 )
    ];

    private sealed record QualitySearchClass(
        string Name,
        string Query,
        double P95BudgetMilliseconds,
        double AllocationBudgetBytes );

    private sealed record QualitySearchMeasurement(
        string Name,
        string Query,
        int Rows,
        double P95Milliseconds,
        double P95BudgetMilliseconds,
        double AllocatedBytes,
        double AllocationBudgetBytes );

    // The runtime's search path without live desktop providers: the application
    // fast path and catalog provider wired as CommandDeckRuntime wires them,
    // source preferences, usage, the web fallback, and Open With detection.
    private static async Task<IReadOnlyList<QualitySearchMeasurement>> MeasureQualitySearchClassesAsync(
        SqliteCatalog catalog,
        CommandDeckOptions options )
    {
        SearchCoordinator coordinator = new(
            [new ApplicationSearchProvider( catalog ), new CatalogSearchProvider( catalog, catalog )],
            options.Search,
            catalog,
            catalog,
            catalog.UsageHistory );
        await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, new AcceptanceExecutor( ), coordinator );

        List<QualitySearchMeasurement> measurements = [];
        foreach ( var searchClass in QualitySearchClasses )
        {
            SearchRequest request = new( )
            {
                Query = searchClass.Query,
                Limit = options.Search.DefaultLimit
            };
            async Task SearchAsync( ) => _ = await runtime.SearchWithOutcomeAsync( request, CancellationToken.None );

            var rows = (await runtime.SearchWithOutcomeAsync( request, CancellationToken.None )).Results.Count;
            var p95 = await MeasureQualityP95Async( SearchAsync );
            var allocated = await MeasureAllocatedBytesPerCallAsync( SearchAsync );

            measurements.Add( new QualitySearchMeasurement(
                searchClass.Name,
                searchClass.Query,
                rows,
                p95,
                searchClass.P95BudgetMilliseconds,
                allocated,
                searchClass.AllocationBudgetBytes ) );
        }

        return measurements;
    }

    private static async Task<double> MeasureQualityP95Async( Func<Task> operation )
    {
        for ( var index = 0; index < 5; index++ )
        {
            await operation( );
        }

        var milliseconds = new double[QualitySearchSamples];
        for ( var index = 0; index < milliseconds.Length; index++ )
        {
            var started = Stopwatch.GetTimestamp( );
            await operation( );
            milliseconds[index] = Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
        }

        Array.Sort( milliseconds );
        return milliseconds[( int ) Math.Ceiling( milliseconds.Length * 0.95 ) - 1];
    }

    private static async Task<double> MeasureAllocatedBytesPerCallAsync( Func<Task> operation )
    {
        GC.Collect( );
        GC.WaitForPendingFinalizers( );
        var start = GC.GetTotalAllocatedBytes( precise: true );
        for ( var index = 0; index < QualityAllocationSamples; index++ )
        {
            await operation( );
        }

        return (GC.GetTotalAllocatedBytes( precise: true ) - start) / ( double ) QualityAllocationSamples;
    }
}
