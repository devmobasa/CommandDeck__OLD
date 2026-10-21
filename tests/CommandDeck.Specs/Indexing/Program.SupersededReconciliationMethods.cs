namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SupersededReconciliationPreservesScanWindowAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Directory.CreateDirectory( Path.Combine( root, "documents" ) ).FullName;
        var database = Path.Combine( root, "catalog.db" );
        var path = Path.Combine( documents, "window-note.txt" );

        try
        {
            var options = CreateIncrementalIndexOptions( database, documents );
            IncrementalEventJournal journal = new( database );
            True( journal.Initialize( ) );
            var checkpoint = journal.LastSequence;
            journal.Append( IncrementalEventKind.Upsert, path, oldPath: null );
            TaskCompletionSource reconciling = new( TaskCreationOptions.RunContinuationsAsynchronously );
            TaskCompletionSource scanPublished = new( TaskCreationOptions.RunContinuationsAsynchronously );
            TaskCompletionSource applied = new( TaskCreationOptions.RunContinuationsAsynchronously );
            await using IncrementalIndexEngine engine = new( options,
                new IndexExclusionPolicy( options.Indexing, options.Privacy, database ),
                ( paths, _ ) =>
                {
                    True( paths.Any( request => request.Path == path ), "the edit after the full scan checkpoint is applied" );
                    applied.TrySetResult( );
                    return ValueTask.FromResult( true );
                },
                async ( _, token ) =>
                {
                    reconciling.TrySetResult( );
                    await scanPublished.Task.WaitAsync( token );
                    return false;
                }, journal );
            await engine.StartAsync( default );
            await reconciling.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );

            // A full scan wins the catalog lock while this reconciliation is queued.
            // Its checkpoint predates the edit in the reconciliation's snapshot.
            engine.CompleteFullScan( checkpoint );
            scanPublished.SetResult( );

            await applied.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            True( await WaitUntilAsync( ( ) => Task.FromResult( engine.GetStatus( ).PendingEventCount == 0 ),
                TimeSpan.FromSeconds( 5 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task SupersededReconciliationClearsActiveStatusAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Directory.CreateDirectory( Path.Combine( root, "documents" ) ).FullName;
        var database = Path.Combine( root, "catalog.db" );
        var path = Path.Combine( documents, "ordinary-note.txt" );

        try
        {
            var options = CreateIncrementalIndexOptions( database, documents );
            IncrementalEventJournal journal = new( database );
            True( journal.Initialize( ) );
            var reconciliations = 0;
            var applied = 0;
            using CancellationTokenSource lifetime = new( );
            await using IncrementalIndexEngine engine = new( options,
                new IndexExclusionPolicy( options.Indexing, options.Privacy, database ),
                ( paths, _ ) =>
                {
                    Equal( path, paths.Single( ).Path );
                    applied++;
                    return ValueTask.FromResult( true );
                },
                ( _, _ ) =>
                {
                    reconciliations++;
                    return ValueTask.FromResult( false );
                }, journal );
            engine.ReportWatcherOverflow( documents );
            var batch = engine.CapturePendingBatch( );
            if ( batch is null )
            {
                throw new InvalidOperationException( "An overflow must produce a reconciliation batch." );
            }

            True( batch.MustReconcile );
            True( !engine.GetStatus( ).Reconciling );

            // The worker captured mustReconcile, but has not entered reconciliation.
            // Publish the full scan now, then dispatch that stale decision.
            engine.CompleteFullScan( journal.LastSequence );
            journal.Append( IncrementalEventKind.Upsert, path, oldPath: null );
            await engine.ProcessPendingAsync( batch, lifetime.Token );

            Equal( 1, reconciliations );
            var status = engine.GetStatus( );
            True( !status.Reconciling, "skipped reconciliation leaves the worker idle" );
            True( !status.ReconciliationRequired );
            Equal( 1L, status.PendingEventCount );
            Equal( path, journal.ReadPending( ).Single( ).Path );

            var ordinary = engine.CapturePendingBatch( );
            if ( ordinary is null )
            {
                throw new InvalidOperationException( "The edit after the scan checkpoint must remain pending." );
            }

            await engine.ProcessPendingAsync( ordinary, lifetime.Token );

            Equal( 1, applied );
            Equal( 1, reconciliations );
            Equal( 0L, engine.GetStatus( ).PendingEventCount );
            True( !engine.GetStatus( ).Reconciling );
            lifetime.Cancel( );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }
}
