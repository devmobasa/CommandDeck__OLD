namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FileIndexingEnforcesCanonicalRootContainmentAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var sandbox = SpecTemporaryFiles.PathFor( $"commanddeck-canonical-index-spec-{Guid.NewGuid( ):N}" );
        var root = Path.Combine( sandbox, "root" );
        var inside = Path.Combine( root, "inside" );
        var outside = Path.Combine( sandbox, "outside" );
        Directory.CreateDirectory( inside );
        Directory.CreateDirectory( outside );

        try
        {
            var insideFile = Path.Combine( inside, "allowed.txt" );
            var outsideFile = Path.Combine( outside, "secret.txt" );
            await File.WriteAllTextAsync( insideFile, "canonical allowed marker" );
            await File.WriteAllTextAsync( outsideFile, "canonical outside secret" );
            var insideLink = Path.Combine( root, "inside-link" );
            var escapeLink = Path.Combine( root, "escape-link" );
            var loopLink = Path.Combine( root, "loop" );
            Directory.CreateSymbolicLink( insideLink, inside );
            Directory.CreateSymbolicLink( escapeLink, outside );
            Directory.CreateSymbolicLink( loopLink, root );

            FileIndexSource source = new(
                new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions
                        {
                            Path = root,
                            IncludeContentInSearch = true,
                            FollowSymbolicLinks = true
                        }
                    ]
                },
                new PrivacyOptions { NeverIndex = [] } );

            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 2 ) );
            List<IndexedItem> items = [];
            await foreach ( var item in source.ScanAsync( null, timeout.Token ) )
            {
                items.Add( item );
            }

            var allowed = items.Single( item => item.Kind == ResultKinds.File );
            Equal( Path.GetFullPath( insideFile ), allowed.Path );
            True( allowed.Body?.Contains( "canonical allowed marker", StringComparison.Ordinal )
                is true );
            True( items.All( item => item.Body?.Contains(
                "canonical outside secret",
                StringComparison.Ordinal ) is not true ) );
            Equal(
                0,
                (await source.IndexPathAsync(
                    Path.Combine( escapeLink, "secret.txt" ),
                    CancellationToken.None )).Count );
            Equal(
                0,
                (await source.IndexDirectoryAsync(
                    escapeLink,
                    CancellationToken.None )).Count );

            Directory.Delete( insideLink );
            Directory.CreateSymbolicLink( insideLink, outside );
            Equal(
                0,
                (await source.IndexPathAsync(
                    Path.Combine( insideLink, "secret.txt" ),
                    CancellationToken.None )).Count );

            FileIndexSource mountBoundarySource = new(
                new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions
                        {
                            Path = "/proc",
                            IncludeContentInSearch = false,
                            FollowSymbolicLinks = true
                        }
                    ]
                },
                new PrivacyOptions { NeverIndex = [] } );
            Equal(
                0,
                (await mountBoundarySource.IndexPathAsync(
                    "/proc/sys/fs/binfmt_misc/status",
                    CancellationToken.None )).Count );
        }
        finally
        {
            Directory.Delete( sandbox, recursive: true );
        }
    }

    private static async Task IncrementalIndexUsesCanonicalSymlinkRootAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var sandbox = SpecTemporaryFiles.PathFor( $"commanddeck-symlink-root-spec-{Guid.NewGuid( ):N}" );
        var canonicalRoot = Path.Combine( sandbox, "canonical" );
        var lexicalRoot = Path.Combine( sandbox, "configured" );
        Directory.CreateDirectory( canonicalRoot );
        Directory.CreateSymbolicLink( lexicalRoot, canonicalRoot );

        try
        {
            var options = CreateIncrementalIndexOptions(
                Path.Combine( sandbox, "catalog.db" ),
                lexicalRoot );
            options.Indexing.Roots[0] = new IndexRootOptions
            {
                Path = lexicalRoot,
                IncludeContentInSearch = true,
                FollowSymbolicLinks = true
            };
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            await using var indexer = new IndexCoordinator( catalog, catalog, options );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );

            var lexicalFile = Path.Combine( lexicalRoot, "linked-note.txt" );
            var canonicalFile = Path.Combine( canonicalRoot, "linked-note.txt" );
            await File.WriteAllTextAsync( lexicalFile, "linked root first marker" );
            True( await WaitForCatalogResultAsync(
                catalog,
                "linked root first",
                result => string.Equals(
                    result.Metadata.GetValueOrDefault( "path" ),
                    canonicalFile,
                    StringComparison.Ordinal ),
                TimeSpan.FromSeconds( 2 ) ) );

            await File.WriteAllTextAsync( lexicalFile, "linked root final marker" );
            True( await WaitForCatalogResultAsync(
                catalog,
                "linked root final",
                result => string.Equals(
                    result.Metadata.GetValueOrDefault( "path" ),
                    canonicalFile,
                    StringComparison.Ordinal ),
                TimeSpan.FromSeconds( 2 ) ) );
            File.Delete( lexicalFile );
            True( await WaitUntilAsync(
                async ( ) => (await SearchAsync( catalog, "linked root final" )).Count == 0,
                TimeSpan.FromSeconds( 2 ) ) );
        }
        finally
        {
            Directory.Delete( sandbox, recursive: true );
        }
    }

    private static async Task CanonicalRootPolicyIsConsistentAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var sandbox = SpecTemporaryFiles.PathFor( $"commanddeck-canonical-policy-spec-{Guid.NewGuid( ):N}" );
        var root = Path.Combine( sandbox, "root" );
        var target = Path.Combine( root, "target" );
        var hiddenAlias = Path.Combine( root, ".hidden-link" );
        var nested = Path.Combine( root, "nested" );
        Directory.CreateDirectory( target );
        Directory.CreateDirectory( nested );
        Directory.CreateSymbolicLink( hiddenAlias, target );
        await File.WriteAllTextAsync( Path.Combine( target, "alias.txt" ), "alias marker" );
        await File.WriteAllTextAsync( Path.Combine( nested, ".private.txt" ), "private marker" );
        await File.WriteAllTextAsync( Path.Combine( nested, "public.txt" ), "public marker" );

        try
        {
            FileIndexSource aliasSource = new(
                new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions
                        {
                            Path = root,
                            IncludeContentInSearch = true,
                            IncludeHidden = false,
                            FollowSymbolicLinks = true
                        }
                    ]
                },
                new PrivacyOptions { NeverIndex = [] } );
            Equal( 0, (await aliasSource.IndexDirectoryAsync(
                hiddenAlias,
                CancellationToken.None )).Count );
            Equal( 0, (await aliasSource.IndexPathAsync(
                Path.Combine( hiddenAlias, "alias.txt" ),
                CancellationToken.None )).Count );

            FileIndexSource nestedSource = new(
                new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions { Path = root, IncludeContentInSearch = true, IncludeHidden = true },
                        new IndexRootOptions { Path = nested, IncludeContentInSearch = true, IncludeHidden = false }
                    ]
                },
                new PrivacyOptions { NeverIndex = [] } );
            List<IndexedItem> items = [];
            await foreach ( var item in nestedSource.ScanAsync(
                progress: null,
                CancellationToken.None ) )
            {
                items.Add( item );
            }

            True( items.Any( item => item.Title == "public.txt" ) );
            True( items.All( item => item.Title != ".private.txt" ) );
        }
        finally
        {
            Directory.Delete( sandbox, recursive: true );
        }
    }
}
