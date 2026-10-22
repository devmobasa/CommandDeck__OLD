namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task OfflineRootFailureDoesNotAbandonLaterRootsAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        try
        {
            var first = Directory.CreateDirectory( Path.Combine( root, "first" ) ).FullName;
            var second = Directory.CreateDirectory( Path.Combine( root, "second" ) ).FullName;
            var note = Path.Combine( second, "note.txt" );
            await File.WriteAllTextAsync( note, "offline old value" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), first, second );
            await using SqliteCatalog catalog = new( options.Storage );
            await using ( var initial = new IndexCoordinator( catalog, catalog, options ) )
            {
                await initial.StartAsync( default );
                await initial.ReindexAsync( null, default );
            }
            await File.WriteAllTextAsync( note, "offline replacement marker" );
            var failures = 0;
            TrackingSearchCatalog tracking = new( catalog, catalog )
            {
                IndexedFileStateFailure = path => path == first && Interlocked.Increment( ref failures ) == 1
                    ? new IOException( "fixture first-root failure" ) : null
            };
            System.Collections.Concurrent.ConcurrentQueue<IndexDiagnostic> diagnostics = new( );
            await using var restarted = new IndexCoordinator( tracking, tracking, options, diagnostic: diagnostics.Enqueue );
            await restarted.StartAsync( default );
            True( await WaitForCatalogResultAsync( catalog, "replacement marker",
                result => result.Title == "note.txt", TimeSpan.FromSeconds( 5 ) ) );
            True( failures > 0, "the first root must actually fail" );
            True( diagnostics.Any( entry => entry.Message.Contains( "offline_changes.root_failed", StringComparison.Ordinal ) ) );
            Equal( 0, tracking.ReplaceAllCount );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task RestartAppliesChangesMadeWhileStoppedAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        Directory.CreateDirectory( documents );
        var kept = Path.Combine( documents, "kept-note.txt" );
        var modified = Path.Combine( documents, "modified-note.txt" );
        var deleted = Path.Combine( documents, "deleted-note.txt" );
        var created = Path.Combine( documents, "nested", "created-note.txt" );
        await File.WriteAllTextAsync( kept, "stopped kept marker" );
        await File.WriteAllTextAsync( modified, "stopped before marker" );
        await File.WriteAllTextAsync( deleted, "stopped deleted marker" );

        try
        {
            var options = CreateIncrementalIndexOptions(
                Path.Combine( root, "catalog.db" ),
                documents );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await using ( var first = new IndexCoordinator( catalog, catalog, options ) )
            {
                await first.StartAsync( CancellationToken.None );
                await first.ReindexAsync( progress: null, CancellationToken.None );
            }

            Directory.CreateDirectory( Path.GetDirectoryName( created )! );
            await File.WriteAllTextAsync( created, "stopped created marker" );
            await File.WriteAllTextAsync( modified, "stopped after marker revised" );
            File.Delete( deleted );

            TrackingSearchCatalog tracking = new( catalog, catalog );
            await using ( var restarted = new IndexCoordinator( tracking, tracking, options ) )
            {
                await restarted.StartAsync( CancellationToken.None );

                True( await WaitForCatalogResultAsync(
                    catalog,
                    "stopped created",
                    result => result.Title == "created-note.txt",
                    TimeSpan.FromSeconds( 5 ) ) );
                True( await WaitForCatalogResultAsync(
                    catalog,
                    "stopped after",
                    result => result.Title == "modified-note.txt",
                    TimeSpan.FromSeconds( 5 ) ) );
                True( await WaitUntilAsync(
                    async ( ) => (await SearchAsync( catalog, "stopped before" )).Count == 0 &&
                        (await SearchAsync( catalog, "stopped deleted" )).Count == 0,
                    TimeSpan.FromSeconds( 5 ) ) );
                True( await WaitForCatalogResultAsync(
                    catalog,
                    "stopped kept",
                    result => result.Title == "kept-note.txt",
                    TimeSpan.FromSeconds( 1 ) ) );

                // Restart reconciles the difference instead of rebuilding the catalog.
                Equal( 0, tracking.ReplaceAllCount );
                True( tracking.LastPathChanges.All( change =>
                    !string.Equals( change.Path, kept, StringComparison.Ordinal ) ) );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task ResumeWatchesRootCreatedAfterStartupAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var watched = Path.Combine( root, "watched" );
        var late = Path.Combine( root, "late" );
        Directory.CreateDirectory( watched );

        try
        {
            var options = CreateIncrementalIndexOptions(
                Path.Combine( root, "catalog.db" ),
                watched,
                late );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await using var indexer = new IndexCoordinator( catalog, catalog, options );
            await indexer.StartAsync( CancellationToken.None );
            var lateRoot = (await indexer.GetIncrementalStatusAsync( CancellationToken.None ))
                .Roots.Single( value => string.Equals( value.Path, late, StringComparison.Ordinal ) );
            True( !lateRoot.Watched );

            Directory.CreateDirectory( late );
            await File.WriteAllTextAsync(
                Path.Combine( late, "existing-note.txt" ),
                "resumed existing marker" );
            await indexer.ResumeAsync( lateRoot.Id, CancellationToken.None );

            True( await WaitForCatalogResultAsync(
                catalog,
                "resumed existing",
                result => result.Title == "existing-note.txt",
                TimeSpan.FromSeconds( 1 ) ) );
            await AssertRootWatchedAsync( indexer, late, expectedWatchedRoots: 2 );

            await File.WriteAllTextAsync(
                Path.Combine( late, "watched-note.txt" ),
                "resumed watched marker" );
            True( await WaitForCatalogResultAsync(
                catalog,
                "resumed watched",
                result => result.Title == "watched-note.txt",
                TimeSpan.FromSeconds( 2 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task ReindexWatchesRootsCreatedAfterStartupAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var watched = Path.Combine( root, "watched" );
        var scoped = Path.Combine( root, "scoped" );
        var full = Path.Combine( root, "full" );
        Directory.CreateDirectory( watched );

        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Indexing = new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions { Path = watched, IncludeContentInSearch = true },
                        new IndexRootOptions { Path = scoped, IncludeContentInSearch = true },
                        new IndexRootOptions { Path = full, IncludeContentInSearch = true }
                    ],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false,
                    IncrementalDebounceMilliseconds = 40,
                    IncrementalMaximumLatencyMilliseconds = 300
                }
            };
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await using var indexer = new IndexCoordinator( catalog, catalog, options );
            await indexer.StartAsync( CancellationToken.None );
            // A fresh catalog reconciles once with a full scan that watches every
            // existing root. Finish it first, so only the reindexes below can
            // watch the roots created next.
            True( await WaitUntilAsync( async ( ) =>
                (await indexer.GetIncrementalStatusAsync( CancellationToken.None )) is { ReconciliationRequired: false, Reconciling: false },
                TimeSpan.FromSeconds( 5 ) ) );
            var scopedRoot = (await indexer.GetIncrementalStatusAsync( CancellationToken.None ))
                .Roots.Single( value => string.Equals( value.Path, scoped, StringComparison.Ordinal ) );

            Directory.CreateDirectory( scoped );
            Directory.CreateDirectory( full );
            await indexer.ReindexAsync(
                new IndexReindexScope { Kind = IndexReindexScopeKinds.Root, RootId = scopedRoot.Id },
                progress: null,
                CancellationToken.None );
            await AssertRootWatchedAsync( indexer, scoped, expectedWatchedRoots: 2 );
            True( (await indexer.GetIncrementalStatusAsync( CancellationToken.None ))
                .UnwatchedRoots.Contains( full, StringComparer.Ordinal ) );

            await File.WriteAllTextAsync(
                Path.Combine( scoped, "scoped-note.txt" ),
                "scoped watched marker" );
            True( await WaitForCatalogResultAsync(
                catalog,
                "scoped watched",
                result => result.Title == "scoped-note.txt",
                TimeSpan.FromSeconds( 2 ) ) );

            await indexer.ReindexAsync( progress: null, CancellationToken.None );
            await AssertRootWatchedAsync( indexer, full, expectedWatchedRoots: 3 );

            await File.WriteAllTextAsync(
                Path.Combine( full, "full-note.txt" ),
                "full watched marker" );
            True( await WaitForCatalogResultAsync(
                catalog,
                "full watched",
                result => result.Title == "full-note.txt",
                TimeSpan.FromSeconds( 2 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task AssertRootWatchedAsync(
        IndexCoordinator indexer,
        string rootPath,
        int expectedWatchedRoots )
    {
        var status = await indexer.GetIncrementalStatusAsync( CancellationToken.None );
        var root = status.Roots.Single( value =>
            string.Equals( value.Path, rootPath, StringComparison.Ordinal ) );

        True( root.Watched );
        True( root.LastError is null );
        Equal( expectedWatchedRoots, status.WatchedRootCount );
        True( !status.UnwatchedRoots.Contains( rootPath, StringComparer.Ordinal ) );
    }
}
