namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task OverflowRecoveryDetectsRetargetedRetainedRootAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux inotify." );
        }

        var sandbox = CreateIncrementalSpecRoot( );
        try
        {
            var original = Directory.CreateDirectory( Path.Combine( sandbox, "original" ) ).FullName;
            var replacement = Directory.CreateDirectory( Path.Combine( sandbox, "replacement" ) ).FullName;
            var link = Path.Combine( sandbox, "link" );
            Directory.CreateSymbolicLink( link, original );
            var other = Directory.CreateDirectory( Path.Combine( sandbox, "other" ) ).FullName;
            var options = CreateIncrementalIndexOptions( Path.Combine( sandbox, "catalog.db" ), link, other );
            TaskCompletionSource<string[]> reconciled = new( TaskCreationOptions.RunContinuationsAsynchronously );
            TaskCompletionSource applied = new( TaskCreationOptions.RunContinuationsAsynchronously );
            var recovering = 0;
            var file = Path.Combine( link, "after.txt" );
            await using IncrementalIndexEngine engine = new( options,
                new IndexExclusionPolicy( options.Indexing, options.Privacy, options.Storage.DatabasePath ),
                ( paths, _ ) =>
                {
                    if ( paths.Any( path => path.Path == file ) )
                    {
                        applied.TrySetResult( );
                    }

                    return ValueTask.FromResult( true );
                },
                ( roots, _ ) =>
                {
                    if ( Volatile.Read( ref recovering ) != 0 )
                    {
                        reconciled.TrySetResult( roots.ToArray( ) );
                    }

                    return ValueTask.CompletedTask;
                } );
            await engine.StartAsync( default );
            True( await WaitUntilAsync( ( ) => Task.FromResult( !engine.GetStatus( ).ReconciliationRequired ), TimeSpan.FromSeconds( 5 ) ) );

            Directory.Delete( link );
            Directory.CreateSymbolicLink( link, replacement );
            Volatile.Write( ref recovering, 1 );
            engine.ReportWatcherOverflow( other );

            var scanned = await reconciled.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( 0, scanned.Length ); // A full scan removes rows under the old canonical target.
            Equal( 0, CountTreeWatches( original ).Watches );
            True( CountTreeWatches( replacement ).Watches > 0 );
            await File.WriteAllTextAsync( file, "new target" );
            await applied.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static Task OverflowReconciliationRestoresMissingWatchesAsync( ) =>
        OverflowReconciliationRestoresWatchesAsync( replaceExisting: false, nestedRoot: false );

    private static Task OverflowReconciliationRemovesStaleWatchesAsync( ) =>
        OverflowReconciliationRestoresWatchesAsync( replaceExisting: true, nestedRoot: false );

    private static Task OverflowReconciliationCoversOverlappingRootsAsync( ) =>
        OverflowReconciliationRestoresWatchesAsync( replaceExisting: false, nestedRoot: true );

    private static async Task OverflowReconciliationRestoresWatchesAsync( bool replaceExisting, bool nestedRoot )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux inotify." );
        }

        var sandbox = CreateIncrementalSpecRoot( );
        try
        {
            var root = Directory.CreateDirectory( Path.Combine( sandbox, "root" ) ).FullName;
            var inner = nestedRoot ? Directory.CreateDirectory( Path.Combine( root, "inner" ) ).FullName : root;
            var outside = Path.Combine( sandbox, "outside" );
            var file = Path.Combine( inner, "missed", "deep", "after.txt" );
            if ( replaceExisting )
            {
                Directory.CreateDirectory( Path.GetDirectoryName( file )! );
            }

            var options = CreateIncrementalIndexOptions( Path.Combine( sandbox, "catalog.db" ), root,
                nestedRoot ? inner : null );
            RestoreInotifySystem system = new( );
            TaskCompletionSource<string[]> reconciled = new( TaskCreationOptions.RunContinuationsAsynchronously );
            TaskCompletionSource applied = new( TaskCreationOptions.RunContinuationsAsynchronously );
            var lost = 0;
            var recoveryScans = 0;
            using ManualResetEventSlim setupComplete = new( );
            IncrementalIndexEngine? owner = null;
            await using IncrementalIndexEngine engine = new( options,
                new IndexExclusionPolicy( options.Indexing, options.Privacy, options.Storage.DatabasePath ),
                ( paths, _ ) =>
                {
                    if ( paths.Any( path => path.Path == file ) )
                    {
                        applied.TrySetResult( );
                    }

                    return ValueTask.FromResult( true );
                },
                ( roots, _ ) =>
                {
                    // The coordinator reports each root after publishing its scan.
                    foreach ( var path in roots )
                    {
                        owner!.RecordRootScan( path, 0 );
                    }

                    if ( Volatile.Read( ref lost ) != 0 )
                    {
                        Interlocked.Increment( ref recoveryScans );
                        reconciled.TrySetResult( roots.ToArray( ) );
                    }

                    return ValueTask.CompletedTask;
                }, inotify: system );
            owner = engine;
            await engine.StartAsync( default );
            True( await WaitUntilAsync( ( ) => Task.FromResult( !engine.GetStatus( ).ReconciliationRequired ), TimeSpan.FromSeconds( 5 ) ) );
            system.ReplaceEvents = bytes =>
            {
                if ( System.Text.Encoding.UTF8.GetString( bytes ).Contains( "missed\0", StringComparison.Ordinal ) &&
                    Interlocked.Exchange( ref lost, 1 ) == 0 )
                {
                    // CREATE or MOVED_FROM can arrive before the test thread has
                    // finished building the replacement subtree.
                    if ( !setupComplete.Wait( TimeSpan.FromSeconds( 5 ) ) )
                    {
                        return [];
                    }

                    return InotifyEvents( (-1, InotifyQueueOverflow, 0u, "") );
                }

                if ( Volatile.Read( ref lost ) != 0 && !reconciled.Task.IsCompleted )
                {
                    return [];
                }

                return null;
            };

            if ( replaceExisting )
            {
                Directory.Move( Path.Combine( inner, "missed" ), outside );
            }

            Directory.CreateDirectory( Path.GetDirectoryName( file )! );
            setupComplete.Set( );
            var scanned = await reconciled.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            system.ReplaceEvents = null;

            if ( replaceExisting )
            {
                Equal( 0, CountTreeWatches( outside ).Watches );
            }

            if ( nestedRoot )
            {
                True( scanned.Contains( root ), "The interrupted parent tree must be covered by this scan." );
                True( scanned.Contains( inner ), "The overflowing nested root must be covered by this scan." );
            }

            True( engine.SnapshotWatchedDirectories( ).Contains( Path.GetDirectoryName( file )! ),
                "A directory whose creation was lost must be watched after reconciliation." );

            await File.WriteAllTextAsync( file, "after overflow" );
            await applied.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            True( await WaitUntilAsync( ( ) => Task.FromResult( !engine.GetStatus( ).ReconciliationRequired &&
                engine.SnapshotPendingEvents( ).Count == 0 ), TimeSpan.FromSeconds( 5 ) ) );
            await Task.Delay( options.Indexing.IncrementalMaximumLatencyMilliseconds * 2 );
            Equal( 1, Volatile.Read( ref recoveryScans ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static void ReplacedParentCannotCreateOutsideWatches( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux inotify." );
        }

        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );
        var parent = Path.Combine( root, "notes" );
        var outside = Path.Combine( sandbox, "outside" );
        InotifyTreeWatcher? watcher = null;
        try
        {
            foreach ( var name in new[] { "one", "two", "three" } )
            {
                Directory.CreateDirectory( Path.Combine( parent, name ) );
                Directory.CreateDirectory( Path.Combine( outside, name ) );
            }

            IndexRootOptions options = new( ) { Path = root };
            IndexExclusionPolicy policy = new( new IndexingOptions { Roots = [options] }, new PrivacyOptions( ) );
            True( CanonicalPathBoundary.TryCreate( root, out var boundary ) );
            RestoreInotifySystem system = new( );
            system.AfterAdd = path =>
            {
                if ( Path.GetDirectoryName( path ) == parent )
                {
                    system.AfterAdd = null;
                    Directory.Move( parent, Path.Combine( sandbox, "moved" ) );
                    Directory.CreateSymbolicLink( parent, outside );
                }
            };
            watcher = new( system, policy, boundary!, options, root, new HashSet<string>( ), new RecordingWatchSink( ) );
            True( watcher.Start( ) );

            watcher.WatchTree( default );

            True( Directory.Exists( Path.Combine( sandbox, "moved" ) ), "The parent changed during the watch walk." );
            Equal( 0, CountTreeWatches( outside ).Watches );
        }
        finally
        {
            watcher?.Close( );
            DeleteIncrementalSpecRoot( sandbox );
        }
    }
}
