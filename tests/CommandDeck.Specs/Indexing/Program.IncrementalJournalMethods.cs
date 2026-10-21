using System.Collections.Concurrent;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly string[] JournalFileSuffixes = [".index-events", ".index-state", ".index-window"];

    private static void IncrementalJournalGroupCommitsBursts( )
    {
        var root = CreateIncrementalSpecRoot( );
        Directory.CreateDirectory( root );
        var database = Path.Combine( root, "catalog.db" );

        try
        {
            var flushes = 0;
            using ( var journal = CreateCountingJournal( database, "boot-a", ( ) => flushes++ ) )
            {
                True( journal.Initialize( ) );
                var initialFlushes = flushes;

                for ( var index = 0; index < 2_000; index++ )
                {
                    journal.Append( IncrementalEventKind.Upsert, $"/burst/file-{index}.txt", null );
                }

                // Only the window marker is durable before the window closes.
                True( flushes - initialFlushes <= 1 );

                journal.Commit( );
                True( flushes - initialFlushes <= 2 );
                journal.Commit( );
                True( flushes - initialFlushes <= 2 );

                journal.Append( IncrementalEventKind.Delete, "/burst/file-0.txt", null );
                journal.Append( IncrementalEventKind.Rename, "/burst/renamed.txt", "/burst/file-1.txt" );
                journal.Commit( );
                True( flushes - initialFlushes <= 4 );
                AssertBurstOrder( journal.ReadPending( ) );
            }

            using ( var reopened = CreateCountingJournal( database, "boot-a", static ( ) => { } ) )
            {
                True( reopened.Initialize( ) );
                True( !reopened.IsCorrupted );
                True( !reopened.UnsyncedWindowLost );
                AssertBurstOrder( reopened.ReadPending( ) );
                Equal( 2_002L, reopened.LastSequence );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }

        static void AssertBurstOrder( IReadOnlyList<IncrementalJournalEvent> pending )
        {
            Equal( 2_002, pending.Count );
            for ( var index = 0; index < 2_000; index++ )
            {
                Equal( index + 1L, pending[index].Sequence );
                Equal( $"/burst/file-{index}.txt", pending[index].Path );
            }

            Equal( IncrementalEventKind.Delete, pending[2_000].Kind );
            Equal( IncrementalEventKind.Rename, pending[2_001].Kind );
            Equal( "/burst/file-1.txt", pending[2_001].OldPath );
        }
    }

    private static void IncrementalJournalWindowRecoversAfterCrashOrReboot( )
    {
        var root = CreateIncrementalSpecRoot( );
        var live = Path.Combine( root, "live" );
        Directory.CreateDirectory( live );
        var database = Path.Combine( live, "catalog.db" );

        try
        {
            using var running = CreateCountingJournal( database, "boot-a", static ( ) => { } );
            True( running.Initialize( ) );
            running.Append( IncrementalEventKind.Upsert, "/window/a.txt", null );
            running.Append( IncrementalEventKind.Upsert, "/window/b.txt", null );
            running.Append( IncrementalEventKind.Delete, "/window/c.txt", null );

            // A process crash keeps written pages: the same boot replays every event.
            var afterProcessCrash = CopyJournalFiles( database, Path.Combine( root, "process-crash" ) );
            using ( var restarted = CreateCountingJournal( afterProcessCrash, "boot-a", static ( ) => { } ) )
            {
                True( restarted.Initialize( ) );
                True( !restarted.IsCorrupted );
                True( !restarted.UnsyncedWindowLost );
                Equal( 3, restarted.ReadPending( ).Count );
            }

            using ( var again = CreateCountingJournal( afterProcessCrash, "boot-b", static ( ) => { } ) )
            {
                True( again.Initialize( ) );
                True( !again.UnsyncedWindowLost );
            }

            // An operating-system crash may drop the unflushed window. The next boot
            // cannot trust the journal and keeps that evidence until a reconcile.
            var afterReboot = CopyJournalFiles( database, Path.Combine( root, "reboot" ) );
            using ( var rebooted = CreateCountingJournal( afterReboot, "boot-b", static ( ) => { } ) )
            {
                True( rebooted.Initialize( ) );
                True( rebooted.UnsyncedWindowLost );

                rebooted.Append( IncrementalEventKind.Upsert, "/window/d.txt", null );
                rebooted.Commit( );
                var beforeReconcile = CopyJournalFiles( afterReboot, Path.Combine( root, "reboot-again" ) );
                using ( var crashedAgain = CreateCountingJournal( beforeReconcile, "boot-b", static ( ) => { } ) )
                {
                    True( crashedAgain.Initialize( ) );
                    True( crashedAgain.UnsyncedWindowLost );
                }

                rebooted.AdvanceCheckpoint( rebooted.LastSequence, ContentExtractor.ExtractorVersion );
                True( !rebooted.UnsyncedWindowLost );
            }

            using ( var reconciled = CreateCountingJournal( afterReboot, "boot-c", static ( ) => { } ) )
            {
                True( reconciled.Initialize( ) );
                True( !reconciled.UnsyncedWindowLost );
                Equal( 0L, reconciled.PendingCount );
            }

            // A torn tail still marks the journal corrupt, as before group commit.
            var torn = CopyJournalFiles( database, Path.Combine( root, "torn" ) );
            using ( var events = new FileStream( torn + ".index-events", FileMode.Open, FileAccess.Write ) )
            {
                events.SetLength( events.Length - 3 );
            }

            using ( var tornJournal = CreateCountingJournal( torn, "boot-a", static ( ) => { } ) )
            {
                True( tornJournal.Initialize( ) );
                True( tornJournal.IsCorrupted );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task IncrementalEngineFlushesOncePerDebounceWindowAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        Directory.CreateDirectory( documents );
        var database = Path.Combine( root, "catalog.db" );

        try
        {
            var options = CreateIncrementalIndexOptions( database, documents );
            var flushes = 0;
            var applyCalls = 0;
            ConcurrentDictionary<string, byte> applied = new( StringComparer.Ordinal );
            var journal = CreateCountingJournal( database, "boot-a", ( ) => Interlocked.Increment( ref flushes ) );
            await using IncrementalIndexEngine engine = new(
                options,
                new IndexExclusionPolicy( options.Indexing, options.Privacy, database ),
                ( paths, _ ) =>
                {
                    Interlocked.Increment( ref applyCalls );
                    foreach ( var request in paths )
                    {
                        applied[request.Path] = 0;
                    }

                    return ValueTask.FromResult( true );
                },
                static ( _, _ ) => ValueTask.FromResult( true ),
                journal );

            await engine.StartAsync( CancellationToken.None );
            True( await WaitUntilAsync(
                ( ) => Task.FromResult( !engine.GetStatus( ).ReconciliationRequired ),
                TimeSpan.FromSeconds( 2 ) ) );
            var flushesBefore = Volatile.Read( ref flushes );
            var appendsBefore = journal.LastSequence;
            var appliesBefore = Volatile.Read( ref applyCalls );

            const int fileCount = 300;
            for ( var index = 0; index < fileCount; index++ )
            {
                await File.WriteAllTextAsync( Path.Combine( documents, $"burst-{index:D3}.txt" ), "burst" );
            }

            True( await WaitUntilAsync(
                ( ) => Task.FromResult( Enumerable.Range( 0, fileCount ).All( index =>
                    applied.ContainsKey( Path.Combine( documents, $"burst-{index:D3}.txt" ) ) ) &&
                    engine.GetStatus( ).PendingEventCount == 0 ),
                TimeSpan.FromSeconds( 10 ) ) );

            var windows = Volatile.Read( ref applyCalls ) - appliesBefore;
            var appended = journal.LastSequence - appendsBefore;
            var flushed = Volatile.Read( ref flushes ) - flushesBefore;
            True( appended >= fileCount );
            True( windows < fileCount / 10 );
            // Each window: marker, commit, checkpoint state, and journal rewrite.
            True( flushed <= (4 * windows) + 4 );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task LostJournalWindowReconcilesAfterRebootAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        Directory.CreateDirectory( documents );
        var database = Path.Combine( root, "catalog.db" );
        await File.WriteAllTextAsync( Path.Combine( documents, "kept.txt" ), "lost window kept marker" );

        try
        {
            var options = CreateIncrementalIndexOptions( database, documents );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );

            await using ( var first = new IndexCoordinator( catalog, catalog, options ) )
            {
                await first.StartAsync( CancellationToken.None );
                await first.ReindexAsync( progress: null, CancellationToken.None );
            }

            // The machine lost power with a window open: the file changed, but its
            // event never reached the disk. Only the durable window marker survived.
            await File.WriteAllTextAsync( Path.Combine( documents, "lost.txt" ), "lost window recovered marker" );
            await File.WriteAllTextAsync( database + ".index-window", "open|previous-boot" );

            await using ( var restarted = new IndexCoordinator( catalog, catalog, options ) )
            {
                await restarted.StartAsync( CancellationToken.None );
                True( await WaitForCatalogResultAsync(
                    catalog,
                    "lost window recovered",
                    result => result.Title == "lost.txt",
                    TimeSpan.FromSeconds( 5 ) ) );
                True( await WaitUntilAsync(
                    async ( ) => !(await restarted.GetIncrementalStatusAsync( CancellationToken.None ))
                        .ReconciliationRequired,
                    TimeSpan.FromSeconds( 2 ) ) );
                Equal( 1, (await SearchAsync( catalog, "lost window kept" )).Count );
            }

            // The reconcile closed the window, so the next start does not repeat it.
            TrackingSearchCatalog tracking = new( catalog, catalog );
            await using ( var third = new IndexCoordinator( tracking, tracking, options ) )
            {
                await third.StartAsync( CancellationToken.None );
                await Task.Delay( 300 );
                True( !(await third.GetIncrementalStatusAsync( CancellationToken.None )).ReconciliationRequired );
                Equal( 0, tracking.ReplaceAllCount );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static IncrementalEventJournal CreateCountingJournal( string database, string bootId, Action flushed ) =>
        new( database, stream =>
        {
            flushed( );
            stream.Flush( flushToDisk: true );
        }, bootId );

    private static string CopyJournalFiles( string database, string directory )
    {
        Directory.CreateDirectory( directory );
        var copy = Path.Combine( directory, Path.GetFileName( database ) );
        foreach ( var suffix in JournalFileSuffixes )
        {
            if ( File.Exists( database + suffix ) )
            {
                File.Copy( database + suffix, copy + suffix );
            }
        }

        return copy;
    }
}
