namespace CommandDeck.Specs;

using System.Collections.Concurrent;

public static partial class Program
{
    // Background policy reconciliation runs after startup with incremental indexing
    // disabled. These specs cover its failure, its interaction with a user reindex,
    // and disposal while it is still running.
    private static async Task ReconciliationFailurePausesIndexingAndWarnsAsync( )
    {
        var sandbox = await CreateReconciliationSandboxAsync( );
        try
        {
            TrackingSearchCatalog tracked = new( sandbox.Catalog, sandbox.Catalog )
            {
                ReplacementFailure = new IOException( "disk full" )
            };
            ConcurrentQueue<IndexDiagnostic> diagnostics = [];

            await using IndexCoordinator coordinator = new(
                tracked,
                tracked,
                sandbox.Updated,
                diagnostic: diagnostics.Enqueue );
            await coordinator.StartAsync( CancellationToken.None );
            await coordinator.PolicyReconciliation.WaitAsync( TimeSpan.FromSeconds( 5 ) );

            var failure = diagnostics.Single( diagnostic =>
                diagnostic.Message.StartsWith( "event=index.reconciliation.failed ", StringComparison.Ordinal ) );
            Equal( IndexDiagnosticSeverity.Warning, failure.Severity );
            True( failure.Message.EndsWith( " error_type=IOException", StringComparison.Ordinal ) );

            var status = await coordinator.GetIncrementalStatusAsync( CancellationToken.None );
            Equal( "index-policy-reconciliation-failed", status.ReconciliationReason );
            True( status.ReconciliationRequired );
        }
        finally
        {
            await sandbox.DisposeAsync( );
        }
    }

    private static async Task FullReindexDoesNotQueueBackgroundReconciliationAsync( )
    {
        var sandbox = await CreateReconciliationSandboxAsync( );
        try
        {
            TrackingSearchCatalog tracked = new( sandbox.Catalog, sandbox.Catalog );

            await using IndexCoordinator coordinator = new( tracked, tracked, sandbox.Updated );
            await coordinator.ReindexAsync( null, CancellationToken.None );
            await coordinator.PolicyReconciliation.WaitAsync( TimeSpan.FromSeconds( 5 ) );

            Equal( 1, tracked.ReplaceAllCount );
            True( (await SearchAsync( sandbox.Catalog, "dependency marker" )).Count == 0 );
        }
        finally
        {
            await sandbox.DisposeAsync( );
        }
    }

    // A paused full reindex holds the index lock without queuing the background
    // task, so a later StartAsync leaves reconciliation blocked on the lock. Disposal
    // must cancel that wait and still complete.
    private static async Task DisposeWhileReconciliationWaitsForTheIndexLockAsync( )
    {
        var sandbox = await CreateReconciliationSandboxAsync( );
        try
        {
            TrackingSearchCatalog tracked = new( sandbox.Catalog, sandbox.Catalog );
            var paused = tracked.PauseNextReplacement( );
            IndexCoordinator coordinator = new( tracked, tracked, sandbox.Updated );

            var reindex = coordinator.ReindexAsync( null, CancellationToken.None ).AsTask( );
            await paused.FirstItem.WaitAsync( TimeSpan.FromSeconds( 3 ) );
            await coordinator.StartAsync( CancellationToken.None );
            await Task.Delay( 50 );
            True( !coordinator.PolicyReconciliation.IsCompleted );

            await coordinator.DisposeAsync( ).AsTask( ).WaitAsync( TimeSpan.FromSeconds( 5 ) );

            True( coordinator.PolicyReconciliation.IsCompletedSuccessfully );

            // The lock is disposed underneath the paused reindex; its outcome is not
            // part of this contract.
            paused.Release( );
            try
            {
                await reindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            }
            catch ( Exception )
            {
            }
        }
        finally
        {
            await sandbox.DisposeAsync( );
        }
    }

    // A catalog indexed without generated-directory exclusion, then options that
    // enable it: the next coordinator must reconcile the stale rows.
    private static async Task<ReconciliationSandbox> CreateReconciliationSandboxAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "project" );
        Directory.CreateDirectory( Path.Combine( root, "node_modules" ) );
        var database = Path.Combine( sandbox, "catalog.db" );
        var original = DeveloperIndexOptions( database, root, generated: false, incremental: false );
        var updated = DeveloperIndexOptions( database, root, incremental: false );

        Equal( 0, (await RunIgnoreGitAsync( root, ["init", "--quiet"], null )).ExitCode );
        await File.WriteAllTextAsync( Path.Combine( root, "node_modules/dependency.txt" ), "dependency marker" );
        await File.WriteAllTextAsync( Path.Combine( root, "source.txt" ), "source marker" );

        SqliteCatalog catalog = new( original.Storage );
        await using ( IndexCoordinator first = new( catalog, catalog, original ) )
        {
            await first.ReindexAsync( null, CancellationToken.None );
        }

        True( (await SearchAsync( catalog, "dependency marker" )).Count > 0 );
        return new ReconciliationSandbox( sandbox, catalog, updated );
    }

    private sealed record ReconciliationSandbox(
        string Root,
        SqliteCatalog Catalog,
        CommandDeckOptions Updated ) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync( )
        {
            await Catalog.DisposeAsync( );
            DeleteIncrementalSpecRoot( Root );
        }
    }
}
