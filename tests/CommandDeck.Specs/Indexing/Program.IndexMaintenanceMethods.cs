namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task WatcherOverflowSurvivesRestartAndReconcilesOnlyItsRootAsync( )
    {
        var temporary = SpecTemporaryFiles.PathFor( $"commanddeck-overflow-restart-{Guid.NewGuid( ):N}" );
        var affectedRoot = Path.Combine( temporary, "affected" );
        var quietRoot = Path.Combine( temporary, "quiet" );
        Directory.CreateDirectory( affectedRoot );
        Directory.CreateDirectory( quietRoot );
        var changed = Path.Combine( affectedRoot, "changed.txt" );
        var deleted = Path.Combine( affectedRoot, "deleted.txt" );
        var quiet = Path.Combine( quietRoot, "quiet.txt" );
        await File.WriteAllTextAsync( changed, "overflow-before" );
        await File.WriteAllTextAsync( deleted, "delete-before" );
        await File.WriteAllTextAsync( quiet, "quiet-before" );
        var options = CreateIndexOperationsOptions(
            temporary,
            true,
            2_048,
            500,
            affectedRoot,
            quietRoot );
        SqliteCatalog catalog = new( options.Storage );
        await catalog.InitializeAsync( CancellationToken.None );

        try
        {
            await using ( IndexCoordinator first = new( catalog, catalog, options ) )
            {
                await first.StartAsync( CancellationToken.None );
                await first.ReindexAsync( progress: null, CancellationToken.None );
                first.ReportWatcherOverflow( affectedRoot );
                await File.WriteAllTextAsync( changed, "overflow-after" );
                File.Delete( deleted );
            }

            await using ( IndexCoordinator restarted = new( catalog, catalog, options ) )
            {
                await restarted.StartAsync( CancellationToken.None );
                await WaitUntilAsync( async ( ) =>
                {
                    var status = await restarted.GetIncrementalStatusAsync(
                        CancellationToken.None );
                    return !status.ReconciliationRequired &&
                        status.PendingEventCount == 0 &&
                        (await catalog.SearchAsync(
                            QueryParser.Parse( "overflow-after" ),
                            10,
                            CancellationToken.None )).Count == 1;
                }, TimeSpan.FromSeconds( 5 ) );

                Equal( 0, (await catalog.SearchAsync(
                    QueryParser.Parse( "delete-before" ),
                    10,
                    CancellationToken.None )).Count );
                Equal( 1, (await catalog.SearchAsync(
                    QueryParser.Parse( "quiet-before" ),
                    10,
                    CancellationToken.None )).Count );
                Equal( 1, (await restarted.GetIncrementalStatusAsync(
                    CancellationToken.None )).LastReconciledRootCount );
            }
        }
        finally
        {
            await catalog.DisposeAsync( );
            Directory.Delete( temporary, recursive: true );
        }
    }

    private static async Task ScheduledMaintenanceIsDueOnceAndPersistsItsResultAsync( )
    {
        var temporary = SpecTemporaryFiles.PathFor( $"commanddeck-scheduled-maintenance-{Guid.NewGuid( ):N}" );
        var root = Path.Combine( temporary, "documents" );
        Directory.CreateDirectory( root );
        var stale = Path.Combine( root, "stale.txt" );
        await File.WriteAllTextAsync( stale, "scheduled-stale-marker" );
        var initialOptions = CreateIndexOperationsOptions(
            temporary,
            enableIncrementalIndexing: false,
            incrementalEventBatchLimit: 2_048,
            incrementalDebounceMilliseconds: 20,
            enableScheduledMaintenance: false,
            root );
        var options = CreateIndexOperationsOptions(
            temporary,
            enableIncrementalIndexing: false,
            incrementalEventBatchLimit: 2_048,
            incrementalDebounceMilliseconds: 20,
            enableScheduledMaintenance: true,
            root );
        AdjustableTimeProvider clock = new(
            new DateTimeOffset( 2035, 1, 2, 3, 4, 5, TimeSpan.Zero ) );
        SqliteCatalog catalog = new( options.Storage );
        await catalog.InitializeAsync( CancellationToken.None );

        try
        {
            await using ( IndexCoordinator initial = new( catalog, catalog, initialOptions, clock ) )
            {
                await initial.ReindexAsync( progress: null, CancellationToken.None );
            }

            File.Delete( stale );
            DateTimeOffset completed;
            await using ( IndexCoordinator indexer = new( catalog, catalog, options, clock ) )
            {
                True( !await indexer.RunMaintenanceIfDueAsync( CancellationToken.None ) );
                clock.Advance( TimeSpan.FromMinutes( 1 ) );

                Task<bool>[] attempts =
                [
                    indexer.RunMaintenanceIfDueAsync(CancellationToken.None).AsTask(),
                    indexer.RunMaintenanceIfDueAsync(CancellationToken.None).AsTask()
                ];
                var ran = await Task.WhenAll( attempts );
                var winnerCount = ran.Count( static value => value );
                if ( winnerCount != 1 )
                {
                    throw new InvalidOperationException(
                        $"Expected one maintenance winner, got {winnerCount}." );
                }

                var staleSearchCount = (await catalog.SearchAsync(
                    QueryParser.Parse( "scheduled-stale-marker" ),
                    10,
                    CancellationToken.None )).Count;
                if ( staleSearchCount != 0 )
                {
                    throw new InvalidOperationException(
                        $"Expected stale search removal, got {staleSearchCount} result(s)." );
                }

                var status = await indexer.GetIncrementalStatusAsync(
                    CancellationToken.None );
                True( status.Maintenance.Enabled );
                True( !status.Maintenance.Running );
                if ( status.Maintenance.RemovedStalePathCount != 1 )
                {
                    throw new InvalidOperationException(
                        "Expected one stale path removal, got " +
                        $"{status.Maintenance.RemovedStalePathCount}." );
                }
                True( status.Maintenance.LastCompletedAt is not null );
                True( status.Maintenance.NextRunAt > status.Maintenance.LastCompletedAt );
                True( !await indexer.RunMaintenanceIfDueAsync( CancellationToken.None ) );
            }

            clock.Advance( TimeSpan.FromMinutes( 1 ) );
            await using ( IndexCoordinator cancelledIndexer = new( catalog, catalog, options, clock ) )
            {
                using CancellationTokenSource cancelled = new( );
                cancelled.Cancel( );
                await ThrowsAsync<OperationCanceledException>( async ( ) =>
                {
                    await cancelledIndexer.RunMaintenanceIfDueAsync( cancelled.Token );
                } );
                True( !(await cancelledIndexer.GetIncrementalStatusAsync(
                    CancellationToken.None )).Maintenance.Running );
            }

            await using ( IndexCoordinator recovered = new( catalog, catalog, options, clock ) )
            {
                True( await recovered.RunMaintenanceIfDueAsync( CancellationToken.None ) );
                completed = (await recovered.GetIncrementalStatusAsync(
                    CancellationToken.None )).Maintenance.LastCompletedAt!.Value;
            }

            await using IndexCoordinator reopened = new( catalog, catalog, options, clock );
            var persisted = await reopened.GetIncrementalStatusAsync(
                CancellationToken.None );
            Equal( completed, persisted.Maintenance.LastCompletedAt );
            Equal( 0L, persisted.Maintenance.RemovedStalePathCount );
            True( persisted.Maintenance.NextRunAt > persisted.Maintenance.LastCompletedAt );
        }
        finally
        {
            await catalog.DisposeAsync( );
            Directory.Delete( temporary, recursive: true );
        }
    }

    private sealed class AdjustableTimeProvider( DateTimeOffset utcNow ) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow( ) => _utcNow;

        public void Advance( TimeSpan duration ) => _utcNow += duration;
    }
}
