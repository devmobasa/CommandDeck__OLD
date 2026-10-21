using System.Collections.Concurrent;
using System.Globalization;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExcludedTreesCostNoInotifyWatchesAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var sandbox = CreateIncrementalSpecRoot( );
        var home = Path.Combine( sandbox, "home" );
        var documents = Path.Combine( home, "Documents" );
        Directory.CreateDirectory( Path.Combine( home, "notes", "sub" ) );
        Directory.CreateDirectory( Path.Combine( documents, "a" ) );
        Directory.CreateDirectory( Path.Combine( documents, "b" ) );
        await File.WriteAllTextAsync( Path.Combine( home, "notes", "sub", "plain.txt" ), "journaled marker" );
        await File.WriteAllTextAsync( Path.Combine( documents, "a", "nested.txt" ), "journaled marker" );
        await File.WriteAllTextAsync( Path.Combine( home, "web", "package.json" ).EnsureParent( ), "{}" );
        for ( var index = 0; index < 150; index++ )
        {
            Directory.CreateDirectory( Path.Combine( home, "web", "node_modules", $"pkg-{index}", "lib" ) );
        }

        for ( var index = 0; index < 50; index++ )
        {
            Directory.CreateDirectory( Path.Combine( home, ".git", "objects", $"{index:x2}" ) );
        }

        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( sandbox, "catalog.db" ) },
                Indexing = new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions { Path = home, IncludeHidden = true, IncludeContentInSearch = false },
                        new IndexRootOptions { Path = documents, IncludeHidden = true }
                    ],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false,
                    IncrementalDebounceMilliseconds = 500,
                    IncrementalMaximumLatencyMilliseconds = 900
                }
            };
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );

            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.StartAsync( CancellationToken.None );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );

            // home, notes, notes/sub, web, and Documents with a and b: seven
            // directories, each watched once. The generated and repository
            // trees (over 350 directories) cost nothing.
            var watches = CountTreeWatches( home );
            Equal( 7, watches.Watches );
            Equal( 0, watches.Duplicates );

            // A deletion is one kernel event per watch, so it journals once
            // inside the nested root as outside it: the outer root does not
            // report the nested root's changes again.
            Equal( 1L, await CountJournaledEventsAsync( indexer, Path.Combine( home, "notes", "sub", "plain.txt" ) ) );
            Equal( 1L, await CountJournaledEventsAsync( indexer, Path.Combine( documents, "a", "nested.txt" ) ) );
            True( await WaitUntilAsync(
                async ( ) => (await SearchAsync( catalog, "journaled" )).Count == 0,
                TimeSpan.FromSeconds( 3 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    // The default Omarchy and Hyprland roots sit below the hidden ~/.config,
    // and a dotfiles repository can sit above them. A hidden ancestor changes
    // nothing below a root; the repository's rules still apply there. Watching,
    // indexing, and journaling admit the same paths.
    private static async Task RootsBelowHiddenAndRepositoryAncestorsAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var sandbox = CreateIncrementalSpecRoot( );
        var omarchy = Path.Combine( sandbox, ".config", "omarchy" );
        var hypr = Path.Combine( sandbox, ".config", "hypr" );
        Directory.CreateDirectory( Path.Combine( sandbox, ".git" ) );
        Directory.CreateDirectory( Path.Combine( sandbox, "state" ) );
        await File.WriteAllTextAsync( Path.Combine( sandbox, ".gitignore" ), "ignored-*.txt\nskipped/\n" );
        await File.WriteAllTextAsync( Path.Combine( omarchy, "web", "package.json" ).EnsureParent( ), "{}" );
        string[] admitted =
        [
            Path.Combine( omarchy, "themes", "current", "theme.txt" ),
            Path.Combine( omarchy, ".state", "session.txt" ),
            Path.Combine( hypr, "scripts", "run.txt" )
        ];
        string[] excluded =
        [
            Path.Combine( omarchy, "ignored-draft.txt" ),
            Path.Combine( omarchy, "skipped", "deep", "note.txt" ),
            Path.Combine( omarchy, "web", "dist", "bundle.txt" ),
            Path.Combine( omarchy, "web", "node_modules", "pkg", "index.txt" ),
            Path.Combine( hypr, ".private", "note.txt" )
        ];
        foreach ( var path in admitted.Concat( excluded ) )
        {
            await File.WriteAllTextAsync( path.EnsureParent( ), "ancestral marker" );
        }

        for ( var index = 0; index < 20; index++ )
        {
            Directory.CreateDirectory( Path.Combine( omarchy, "web", "node_modules", $"pkg-{index}", "lib" ) );
            Directory.CreateDirectory( Path.Combine( omarchy, "web", ".git", "objects", $"{index:x2}" ) );
        }

        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( sandbox, "state", "catalog.db" ) },
                Indexing = new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions { Path = omarchy, IncludeHidden = true },
                        new IndexRootOptions { Path = hypr }
                    ],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false,
                    IncrementalDebounceMilliseconds = 500,
                    IncrementalMaximumLatencyMilliseconds = 900
                }
            };
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );

            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.StartAsync( CancellationToken.None );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );

            // Each root watches what its own settings admit: the hidden .state
            // where hidden entries are included, but not the hidden .private
            // where they are not, nor the generated, nested-repository, and
            // ignored trees. The repository's rule files above the roots are
            // watched too, outside the roots' trees.
            Equal(
                ".config/hypr, .config/hypr/scripts, .config/omarchy, .config/omarchy/.state, " +
                ".config/omarchy/themes, .config/omarchy/themes/current, .config/omarchy/web",
                WatchedBelow( indexer, sandbox ) );
            var watches = CountTreeWatches( omarchy, hypr );
            Equal( 7, watches.Watches );
            Equal( 0, watches.Duplicates );

            Equal( "run.txt, session.txt, theme.txt", await SearchTitlesAsync( catalog, "ancestral" ) );

            // Events for paths the policy excludes are dropped before the
            // journal; an admitted path is journaled and indexed.
            True( await WaitUntilAsync(
                async ( ) => (await indexer.GetIncrementalStatusAsync( CancellationToken.None )).PendingEventCount == 0,
                TimeSpan.FromSeconds( 5 ) ) );
            var ignored = (await indexer.GetIncrementalStatusAsync( CancellationToken.None )).IgnoredEventCount;
            await File.WriteAllTextAsync( Path.Combine( omarchy, "ignored-late.txt" ), "ancestral marker" );
            await File.WriteAllTextAsync( Path.Combine( hypr, ".late.txt" ), "ancestral marker" );
            True( await WaitUntilAsync(
                async ( ) => (await indexer.GetIncrementalStatusAsync( CancellationToken.None )).IgnoredEventCount - ignored >= 4,
                TimeSpan.FromSeconds( 3 ) ) );
            Equal( 0L, (await indexer.GetIncrementalStatusAsync( CancellationToken.None )).PendingEventCount );

            await File.WriteAllTextAsync( Path.Combine( omarchy, "themes", "late.txt" ), "ancestral marker" );
            True( await WaitForCatalogResultAsync( catalog, "ancestral", result => result.Title == "late.txt", TimeSpan.FromSeconds( 3 ) ) );
            Equal( "late.txt, run.txt, session.txt, theme.txt", await SearchTitlesAsync( catalog, "ancestral" ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }

        static string WatchedBelow( IndexCoordinator indexer, string sandbox ) => string.Join( ", ", indexer
            .SnapshotWatchedDirectories( )
            .Select( path => Path.GetRelativePath( sandbox, path ) )
            .Order( StringComparer.Ordinal ) );

        static async Task<string> SearchTitlesAsync( ISearchCatalog catalog, string query ) => string.Join( ", ",
            (await SearchAsync( catalog, query )).Select( static result => result.Title ).Order( StringComparer.Ordinal ) );
    }

    private static async Task WatchLimitLeavesAVisibleUnwatchedSubtreeAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );
        for ( var branch = 0; branch < 10; branch++ )
        {
            for ( var leaf = 0; leaf < 3; leaf++ )
            {
                Directory.CreateDirectory( Path.Combine( root, $"branch-{branch}", $"leaf-{leaf}" ) );
            }
        }

        try
        {
            var options = CreateIncrementalIndexOptions( Path.Combine( sandbox, "catalog.db" ), root );
            ConcurrentQueue<string> applied = new( );
            ConcurrentQueue<string> reconciled = new( );
            LimitedInotifySystem inotify = new( limit: 12 );
            await using IncrementalIndexEngine engine = new(
                options,
                new IndexExclusionPolicy( options.Indexing, options.Privacy, options.Storage.DatabasePath ),
                ( paths, _ ) =>
                {
                    foreach ( var request in paths )
                    {
                        applied.Enqueue( request.Path );
                    }

                    return ValueTask.FromResult( true );
                },
                ( roots, _ ) =>
                {
                    reconciled.Enqueue( string.Join( ",", roots ) );
                    return ValueTask.FromResult( true );
                },
                inotify: inotify );
            await engine.StartAsync( CancellationToken.None );
            True( await WaitUntilAsync(
                ( ) => Task.FromResult( !engine.GetStatus( ).ReconciliationRequired ),
                TimeSpan.FromSeconds( 2 ) ) );

            // The limit leaves most branches unwatched, and says so, without
            // treating the refusal as lost events.
            var limited = engine.GetStatus( );
            var rootStatus = limited.Roots.Single( );
            True( rootStatus.Watched );
            Equal( 12, rootStatus.WatchedDirectoryCount );
            True( rootStatus.UnwatchedDirectoryCount > 0 );
            True( rootStatus.LastError?.Contains( "watch limit", StringComparison.Ordinal ) == true );
            True( rootStatus.Remediation?.Contains( "fs.inotify.max_user_watches", StringComparison.Ordinal ) == true );
            True( limited.LastError?.Contains( "watch limit", StringComparison.Ordinal ) == true );
            (var doctorPassed, var doctorDetail) = Doctor.DescribeIndexRoot( rootStatus );
            True( !doctorPassed );
            True( doctorDetail.Contains( "fs.inotify.max_user_watches", StringComparison.Ordinal ) );

            var serialized = System.Text.Json.JsonSerializer.Serialize( rootStatus, CommandDeckJsonContext.Default.IndexRootStatus );
            var wireStatus = System.Text.Json.JsonSerializer.Deserialize( serialized, CommandDeckJsonContext.Default.IndexRootStatus );
            if ( wireStatus is null )
            {
                throw new InvalidOperationException( "Root status failed to round-trip." );
            }

            True( !Doctor.DescribeIndexRoot( wireStatus ).Passed );
            True( !serialized.Contains( "unwatchedDirectoryCount", StringComparison.Ordinal ) );

            var watchedFile = Path.Combine( root, "at-root.txt" );
            await File.WriteAllTextAsync( watchedFile, "limited" );
            True( await WaitUntilAsync( ( ) => Task.FromResult( applied.Contains( watchedFile ) ), TimeSpan.FromSeconds( 3 ) ) );
            await Task.Delay( 300 );
            True( reconciled.All( static roots => roots.Length == 0 ) );
            True( !engine.GetStatus( ).ReconciliationRequired );

            // After the limit is raised, a root rescan watches the rest.
            inotify.Limit = 1_000;
            engine.RetryUnwatchedDirectories( root, CancellationToken.None );
            engine.RecordRootScan( root, 0 );
            var recovered = engine.GetStatus( ).Roots.Single( );
            Equal( 41, recovered.WatchedDirectoryCount );
            Equal( 0L, recovered.UnwatchedDirectoryCount );
            True( recovered.LastError is null );
            True( Doctor.DescribeIndexRoot( recovered ).Passed );
            var deepFile = Path.Combine( root, "branch-9", "leaf-2", "deep.txt" );
            await File.WriteAllTextAsync( deepFile, "recovered" );
            True( await WaitUntilAsync( ( ) => Task.FromResult( applied.Contains( deepFile ) ), TimeSpan.FromSeconds( 3 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task NewAndMovedDirectoriesAreWatchedByPolicyAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );
        var notes = Path.Combine( root, "notes" );
        var web = Path.Combine( root, "web" );
        Directory.CreateDirectory( notes );
        Directory.CreateDirectory( Path.Combine( web, "dist" ) );
        Directory.CreateDirectory( Path.Combine( web, "node_modules" ) );
        await File.WriteAllTextAsync( Path.Combine( web, "package.json" ), "{}" );

        try
        {
            var options = CreateIncrementalIndexOptions( Path.Combine( sandbox, "catalog.db" ), root );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.StartAsync( CancellationToken.None );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );
            Equal( "notes, root, web", WatchedList( indexer, root ) );

            // A new admitted directory is watched from its creation on.
            var fresh = Path.Combine( notes, "fresh" );
            Directory.CreateDirectory( fresh );
            Directory.CreateDirectory( Path.Combine( web, "node_modules", "pkg" ) );
            True( await WaitUntilAsync(
                ( ) => Task.FromResult( WatchedList( indexer, root ) == "notes, notes/fresh, root, web" ),
                TimeSpan.FromSeconds( 3 ) ) );
            await File.WriteAllTextAsync( Path.Combine( fresh, "one.txt" ), "watchfresh marker" );
            True( await WaitForCatalogResultAsync( catalog, "watchfresh", result => result.Title == "one.txt", TimeSpan.FromSeconds( 3 ) ) );

            // Moved into a generated tree it is neither indexed nor watched;
            // moved back it is both again.
            var buried = Path.Combine( web, "dist", "fresh" );
            Directory.Move( fresh, buried );
            True( await WaitUntilAsync(
                async ( ) => (await SearchAsync( catalog, "watchfresh" )).Count == 0 &&
                    WatchedList( indexer, root ) == "notes, root, web",
                TimeSpan.FromSeconds( 3 ) ) );
            Directory.Move( buried, fresh );
            await File.WriteAllTextAsync( Path.Combine( fresh, "two.txt" ), "watchreturned marker" );
            True( await WaitForCatalogResultAsync( catalog, "watchreturned", result => result.Title == "two.txt", TimeSpan.FromSeconds( 3 ) ) );
            True( await WaitForCatalogResultAsync( catalog, "watchfresh", result => result.Title == "one.txt", TimeSpan.FromSeconds( 3 ) ) );
            Equal( "notes, notes/fresh, root, web", WatchedList( indexer, root ) );

            // Removing the project marker admits dist, which is then watched.
            File.Delete( Path.Combine( web, "package.json" ) );
            True( await WaitUntilAsync(
                ( ) => Task.FromResult( WatchedList( indexer, root ) == "notes, notes/fresh, root, web, web/dist" ),
                TimeSpan.FromSeconds( 3 ) ) );
            await File.WriteAllTextAsync( Path.Combine( web, "dist", "bundle.txt" ), "watchadmitted marker" );
            True( await WaitForCatalogResultAsync( catalog, "watchadmitted", result => result.Title == "bundle.txt", TimeSpan.FromSeconds( 3 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }

        static string WatchedList( IndexCoordinator indexer, string root ) => string.Join( ", ", indexer
            .SnapshotWatchedDirectories( )
            .Select( path => path == root ? "root" : Path.GetRelativePath( root, path ) )
            .Order( StringComparer.Ordinal )
            .Distinct( ) );
    }

    // Deletes one file once the journal is idle and counts the events that
    // adds before the half-second debounce lets them be applied.
    private static async Task<long> CountJournaledEventsAsync( IndexCoordinator indexer, string path )
    {
        True( await WaitUntilAsync(
            async ( ) => (await indexer.GetIncrementalStatusAsync( CancellationToken.None )).PendingEventCount == 0,
            TimeSpan.FromSeconds( 5 ) ) );
        File.Delete( path );
        await Task.Delay( 200 );
        return (await indexer.GetIncrementalStatusAsync( CancellationToken.None )).PendingEventCount;
    }

    // This process's inotify watches on directories in the given roots' trees,
    // admitted or not, and how many of them watch a directory that another of
    // them already covers. Watches outside the trees are not counted: the
    // rule-file directories of a repository above a root are watched too, and
    // the sandbox may or may not sit in one (a checkout, or a temporary
    // directory that briefly holds a .git mount point).
    private static (int Watches, int Duplicates) CountTreeWatches( params string[] roots )
    {
        HashSet<ulong> tree = [];
        EnumerationOptions everything = new( )
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0
        };
        foreach ( var directory in roots.Concat( roots.SelectMany( root => Directory.EnumerateDirectories( root, "*", everything ) ) ) )
        {
            True( LinuxMountIdentity.TryGetDirectory( directory, followSymbolicLinks: false, out var identity ) );
            tree.Add( identity.Inode );
        }

        var watches = 0;
        var duplicates = 0;
        HashSet<ulong> watched = [];
        foreach ( var descriptor in Directory.EnumerateFiles( "/proc/self/fdinfo" ) )
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines( descriptor );
            }
            catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
            {
                continue;
            }

            // "inotify wd:1 ino:2c sdev:2f mask:..." with the inode in hex.
            foreach ( var line in lines.Where( static line => line.StartsWith( "inotify wd:", StringComparison.Ordinal ) ) )
            {
                var field = line.Split( ' ', StringSplitOptions.RemoveEmptyEntries )
                    .Single( static value => value.StartsWith( "ino:", StringComparison.Ordinal ) );
                var inode = ulong.Parse( field["ino:".Length..], NumberStyles.HexNumber, CultureInfo.InvariantCulture );
                if ( !tree.Contains( inode ) )
                {
                    continue;
                }

                watches++;
                if ( !watched.Add( inode ) )
                {
                    duplicates++;
                }
            }
        }

        return (watches, duplicates);
    }

    private static string EnsureParent( this string path )
    {
        Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        return path;
    }
}
