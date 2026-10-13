namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly string[] TraversalFixtureAdmittedFiles =
    [
        "notes.txt",
        "a/b/c/report.txt",
        "a/b/c/d/e/f/deep.txt",
        "web/package.json",
        "web/src/app.js",
        "py/pyproject.toml",
        "repo/keep.log",
        "repo/src/main.cs",
        "repo/nested/app.log",
        "globs/manual/package.json",
        "globs/manual/dist/manual.pdf"
    ];

    private static async Task TraversalPolicyAgreesWithFullPolicyAtEveryDepthAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );

        try
        {
            CreateTraversalPolicyFixture( root );
            (var indexing, var privacy) = TraversalPolicyOptions( root );
            IndexExclusionPolicy policy = new( indexing, privacy );
            True( CanonicalPathBoundary.TryCreate( root, out var boundary ) );
            var start = policy.EnterRoot( boundary!, indexing.Roots[0], out var rootExclusion );
            True( start is not null && rootExclusion is null );

            // Every entry the traversal reaches gets the verdict the full policy
            // gives the same path; everything below an excluded directory stays
            // excluded however deep it is.
            var compared = CompareTraversalWithFullPolicy( policy, start!, boundary!, indexing.Roots[0] );
            True( compared > 30 );

            using FileIndexSource source = new( indexing, privacy );
            List<string> scanned = [];
            await foreach ( var item in source.ScanAsync( null, CancellationToken.None ) )
            {
                scanned.Add( Path.GetRelativePath( root, item.Path! ).Replace( '\\', '/' ) );
            }

            scanned.Sort( StringComparer.Ordinal );
            var expected = TraversalFixtureAdmittedFiles.Order( StringComparer.Ordinal ).ToArray( );
            Equal( string.Join( ", ", expected ), string.Join( ", ", scanned ) );

            var scoped = await source.IndexDirectoryAsync( Path.Combine( root, "a" ), CancellationToken.None );
            Equal( "a/b/c/d/e/f/deep.txt, a/b/c/report.txt", string.Join( ", ", scoped
                .Select( item => Path.GetRelativePath( root, item.Path! ) )
                .Order( StringComparer.Ordinal ) ) );
            True( source.SnapshotDiagnostics( ).Any( value =>
                value.Reason == IndexExclusionReasons.PrivacyRule && value.RelativePath == "[redacted]" ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task SymlinkEscapesStayOutsideAtEveryDepthAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );
        var outside = Path.Combine( sandbox, "outside" );
        var deep = Path.Combine( root, "a", "b", "c" );
        Directory.CreateDirectory( deep );
        Directory.CreateDirectory( outside );
        Directory.CreateDirectory( Path.Combine( root, "a", "credentials" ) );

        try
        {
            await File.WriteAllTextAsync( Path.Combine( deep, "inside.txt" ), "escape inside marker" );
            await File.WriteAllTextAsync( Path.Combine( outside, "outside.txt" ), "escape outside marker" );
            await File.WriteAllTextAsync( Path.Combine( root, "a", "credentials", "token.txt" ), "escape denied marker" );
            Directory.CreateSymbolicLink( Path.Combine( deep, "escape-dir" ), outside );
            File.CreateSymbolicLink( Path.Combine( deep, "escape-file.txt" ), Path.Combine( outside, "outside.txt" ) );
            Directory.CreateSymbolicLink( Path.Combine( deep, "loop" ), root );
            Directory.CreateSymbolicLink( Path.Combine( root, "a", "public-notes" ), Path.Combine( root, "a", "credentials" ) );
            Directory.CreateSymbolicLink( Path.Combine( root, "alias" ), deep );

            foreach ( var follow in new[] { false, true } )
            {
                IndexingOptions indexing = new( )
                {
                    Roots = [new IndexRootOptions { Path = root, IncludeContentInSearch = true, FollowSymbolicLinks = follow }]
                };
                using FileIndexSource source = new( indexing, new PrivacyOptions( ) );
                using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 5 ) );
                List<IndexedItem> items = [];
                await foreach ( var item in source.ScanAsync( null, timeout.Token ) )
                {
                    items.Add( item );
                }

                // Links never add a path outside the root or behind a privacy rule,
                // and a followed alias resolves to the one canonical file.
                var inside = items.Single( item => item.Kind == ResultKinds.File );
                Equal( Path.Combine( deep, "inside.txt" ), inside.Path );
                True( items.All( item => item.Body?.Contains( "escape outside", StringComparison.Ordinal ) is not true ) );
                True( items.All( item => item.Body?.Contains( "escape denied", StringComparison.Ordinal ) is not true ) );
                True( source.SnapshotDiagnostics( ).Any( value => value.Reason == IndexExclusionReasons.PathBoundary ) );
                Equal( 0, (await source.IndexDirectoryAsync( Path.Combine( deep, "escape-dir" ), CancellationToken.None )).Count );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task CachedRootBoundaryFollowsARetargetedRootAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var sandbox = CreateIncrementalSpecRoot( );
        var first = Path.Combine( sandbox, "first" );
        var second = Path.Combine( sandbox, "second" );
        var configured = Path.Combine( sandbox, "configured" );
        Directory.CreateDirectory( first );
        Directory.CreateDirectory( second );

        try
        {
            await File.WriteAllTextAsync( Path.Combine( first, "first.txt" ), "retarget first marker" );
            await File.WriteAllTextAsync( Path.Combine( second, "second.txt" ), "retarget second marker" );
            Directory.CreateSymbolicLink( configured, first );
            IndexingOptions indexing = new( )
            {
                Roots = [new IndexRootOptions { Path = configured, IncludeContentInSearch = true }]
            };
            using FileIndexSource source = new( indexing, new PrivacyOptions( ) );
            Equal( Path.Combine( first, "first.txt" ),
                (await source.IndexPathAsync( Path.Combine( first, "first.txt" ), CancellationToken.None )).Single( ).Path );

            // The configured root now names another directory. The old target
            // must stop being authorized even though its boundary was cached.
            Directory.Delete( configured );
            Directory.CreateSymbolicLink( configured, second );
            Equal( 0, (await source.IndexPathAsync( Path.Combine( first, "first.txt" ), CancellationToken.None )).Count );
            Equal( 0, (await source.IndexPathAsync( Path.Combine( configured, "first.txt" ), CancellationToken.None )).Count );
            Equal( Path.Combine( second, "second.txt" ),
                (await source.IndexPathAsync( Path.Combine( configured, "second.txt" ), CancellationToken.None )).Single( ).Path );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task ReplacedDirectoryDuringScanEmitsNothingOutsideAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );
        var directory = Path.Combine( root, "a" );
        var outside = Path.Combine( sandbox, "outside" );
        Directory.CreateDirectory( directory );
        Directory.CreateDirectory( outside );

        try
        {
            foreach ( var name in new[] { "one.txt", "two.txt", "three.txt" } )
            {
                await File.WriteAllTextAsync( Path.Combine( directory, name ), "replaced inside marker" );
                await File.WriteAllTextAsync( Path.Combine( outside, name ), "replaced outside marker" );
            }

            IndexingOptions indexing = new( )
            {
                Roots = [new IndexRootOptions { Path = root, IncludeContentInSearch = true }]
            };
            using FileIndexSource source = new( indexing, new PrivacyOptions( ) );
            var scan = source.ScanAsync( null, CancellationToken.None ).GetAsyncEnumerator( );
            List<IndexedItem> items = [];
            True( await scan.MoveNextAsync( ) );
            items.Add( scan.Current );

            // The directory being enumerated is swapped for a link out of the
            // root between two entries. Its remaining names now resolve outside.
            Directory.Move( directory, Path.Combine( root, "a-moved" ) );
            Directory.CreateSymbolicLink( directory, outside );
            while ( await scan.MoveNextAsync( ) )
            {
                items.Add( scan.Current );
            }

            await scan.DisposeAsync( );
            Equal( 1, items.Count );
            True( items.All( item => item.Body?.Contains( "replaced outside", StringComparison.Ordinal ) is not true ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task MovedDirectoriesTakeTheirDestinationPolicyAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );
        var notes = Path.Combine( root, "notes" );
        var web = Path.Combine( root, "web" );
        Directory.CreateDirectory( Path.Combine( notes, "deeper" ) );
        Directory.CreateDirectory( Path.Combine( web, "dist" ) );
        Directory.CreateDirectory( Path.Combine( root, "x-secret-stash" ) );
        await File.WriteAllTextAsync( Path.Combine( web, "package.json" ), "{}" );
        await File.WriteAllTextAsync( Path.Combine( notes, "deeper", "plan.txt" ), "movedpolicy marker" );

        try
        {
            var options = CreateIncrementalIndexOptions( Path.Combine( sandbox, "catalog.db" ), root );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.StartAsync( CancellationToken.None );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );
            Equal( 1, (await SearchAsync( catalog, "movedpolicy marker" )).Count );

            // Into a privacy-denied directory, a generated directory, and a
            // hidden directory: the rows leave. Out again: they return.
            string[] destinations =
            [
                Path.Combine( root, "x-secret-stash", "notes" ),
                Path.Combine( web, "dist", "notes" ),
                Path.Combine( root, ".notes" )
            ];
            foreach ( var destination in destinations )
            {
                Directory.Move( notes, destination );
                True( await WaitUntilAsync(
                    async ( ) => (await SearchAsync( catalog, "movedpolicy marker" )).Count == 0,
                    TimeSpan.FromSeconds( 3 ) ) );

                Directory.Move( destination, notes );
                True( await WaitForCatalogResultAsync(
                    catalog,
                    "movedpolicy marker",
                    result => result.Id == StableId.Create( ResultKinds.File, Path.Combine( notes, "deeper", "plan.txt" ) ),
                    TimeSpan.FromSeconds( 3 ) ) );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task ScanAllocationDoesNotGrowWithAncestorCountAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var current = Path.Combine( sandbox, "root" );
        Directory.CreateDirectory( current );
        var root = current;

        try
        {
            const int depth = 16;
            const int filesPerLevel = 4;
            for ( var level = 0; level < depth; level++ )
            {
                current = Path.Combine( current, $"level-{level:D2}" );
                Directory.CreateDirectory( current );
                for ( var file = 0; file < filesPerLevel; file++ )
                {
                    await File.WriteAllTextAsync( Path.Combine( current, $"note-{file}.txt" ), "deep" );
                }
            }

            IndexingOptions indexing = new( )
            {
                Roots = [new IndexRootOptions { Path = root, IncludeContentInSearch = false }]
            };
            using FileIndexSource source = new( indexing, new PrivacyOptions( ) );
            Equal( depth * filesPerLevel, ScanSynchronously( source ) );

            // Metadata-only scans complete synchronously, so this thread's
            // allocation counter covers the whole traversal. Re-checking every
            // ancestor for every entry allocated about 70 KiB per entry here on
            // every pass; the best of three passes ignores one-off runtime work.
            var perEntry = long.MaxValue;
            for ( var pass = 0; pass < 3; pass++ )
            {
                var before = GC.GetAllocatedBytesForCurrentThread( );
                Equal( depth * filesPerLevel, ScanSynchronously( source ) );
                var allocated = GC.GetAllocatedBytesForCurrentThread( ) - before;
                perEntry = Math.Min( perEntry, allocated / (depth * (filesPerLevel + 1)) );
            }

            True( perEntry < 16 * 1024 );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }

        static int ScanSynchronously( FileIndexSource source )
        {
            var count = 0;
            var scan = source.ScanAsync( null, CancellationToken.None ).GetAsyncEnumerator( );
            while ( scan.MoveNextAsync( ).AsTask( ).GetAwaiter( ).GetResult( ) )
            {
                count++;
            }

            scan.DisposeAsync( ).AsTask( ).GetAwaiter( ).GetResult( );
            return count;
        }
    }
}
