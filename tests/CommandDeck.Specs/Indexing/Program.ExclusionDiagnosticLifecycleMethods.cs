namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FullScanExclusionDiagnosticsSurviveIncrementalEditsAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        try
        {
            var root = Directory.CreateDirectory( Path.Combine( sandbox, "project" ) ).FullName;
            var hidden = Path.Combine( root, ".hidden.txt" );
            var allowed = Path.Combine( root, "allowed.txt" );
            await File.WriteAllTextAsync( hidden, "excluded marker" );
            await File.WriteAllTextAsync( allowed, "initial marker" );
            var options = DeveloperIndexOptions( Path.Combine( sandbox, "catalog.db" ), root );
            options.Indexing.Roots[0] = options.Indexing.Roots[0] with { IncludeHidden = false };
            await using SqliteCatalog catalog = new( options.Storage );
            await using IndexCoordinator indexer = new( catalog, catalog, options );

            await indexer.ReindexAsync( null, CancellationToken.None );
            await AssertHiddenAsync( ".hidden.txt" );
            await EditAllowedAsync( "firstincrementalmarker" );
            await AssertHiddenAsync( ".hidden.txt" );

            File.Delete( hidden );
            var nested = Directory.CreateDirectory( Path.Combine( root, "nested" ) ).FullName;
            await File.WriteAllTextAsync( Path.Combine( nested, ".later.txt" ), "later excluded marker" );
            await indexer.ReindexAsync( null, CancellationToken.None );
            await AssertHiddenAsync( "nested/.later.txt" );
            await EditAllowedAsync( "secondincrementalmarker" );
            await AssertHiddenAsync( "nested/.later.txt" );

            Directory.Delete( nested, recursive: true );
            True( await WaitUntilAsync( async ( ) =>
                (await indexer.InspectAsync( CancellationToken.None )).ExclusionDiagnostics.Count == 0,
                TimeSpan.FromSeconds( 3 ) ), "Deleting the subtree must retire its exclusion diagnostic." );
            await EditAllowedAsync( "thirdincrementalmarker" );
            Equal( 0, (await indexer.InspectAsync( CancellationToken.None )).ExclusionDiagnostics.Count );

            async Task EditAllowedAsync( string marker )
            {
                await File.WriteAllTextAsync( allowed, marker );
                True( await WaitForCatalogResultAsync( catalog, marker, row => row.Id == StableId.Create( ResultKinds.File, allowed ),
                    TimeSpan.FromSeconds( 3 ) ), "The allowed edit must reach the catalog through the watcher." );
            }

            async Task AssertHiddenAsync( string relativePath )
            {
                var inspection = await indexer.InspectAsync( CancellationToken.None );
                var diagnostic = inspection.ExclusionDiagnostics.Single( );
                Equal( root, diagnostic.Root );
                Equal( relativePath, diagnostic.RelativePath );
                Equal( IndexExclusionReasons.Hidden, diagnostic.Reason );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task ExerciseExclusionDiagnosticsRootChangeAsync( bool configured )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( sandbox, "retained" ) ).FullName;
            var revoked = Directory.CreateDirectory( Path.Combine( sandbox, "revoked" ) ).FullName;
            var allowed = Path.Combine( retained, "allowed.txt" );
            await File.WriteAllTextAsync( allowed, "initial marker" );
            await File.WriteAllTextAsync( Path.Combine( retained, ".retained.txt" ), "retained exclusion" );
            await File.WriteAllTextAsync( Path.Combine( revoked, ".revoked.txt" ), "revoked exclusion" );
            var options = DeveloperIndexOptions( Path.Combine( sandbox, "catalog.db" ), retained );
            options.Indexing.Roots[0] = options.Indexing.Roots[0] with { IncludeHidden = false };
            IndexRootOptions revokedOptions = new( ) { Path = revoked, IncludeHidden = false };
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );
            if ( configured )
            {
                options.Indexing.Roots.Add( revokedOptions );
            }
            else
            {
                await catalog.LauncherPreferences.ReplaceIndexRootsAsync( new IndexRootPreferences
                {
                    Added = [new AddedIndexRoot { Options = revokedOptions }]
                }, CancellationToken.None );
            }

            await using IndexCoordinator indexer = new( catalog, catalog, options, rootPreferences: catalog.LauncherPreferences );
            await indexer.ReindexAsync( null, CancellationToken.None );
            Equal( 2, (await indexer.InspectAsync( CancellationToken.None )).ExclusionDiagnostics.Count );

            await indexer.ManageRootsAsync( configured ? ProtocolOperations.IndexDisable : ProtocolOperations.IndexRemove,
                null, StableId.Create( "index-root", revoked ), null, CancellationToken.None );
            await indexer.WaitForBackgroundReindexAsync( CancellationToken.None ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
            var afterScan = (await indexer.InspectAsync( CancellationToken.None )).ExclusionDiagnostics.Single( );
            Equal( retained, afterScan.Root );

            await File.WriteAllTextAsync( allowed, "afterrootchangeincrementalmarker" );
            True( await WaitForCatalogResultAsync( catalog, "afterrootchangeincrementalmarker", row => row.Id == StableId.Create( ResultKinds.File, allowed ),
                TimeSpan.FromSeconds( 3 ) ) );
            var diagnostic = (await indexer.InspectAsync( CancellationToken.None )).ExclusionDiagnostics.Single( );
            Equal( retained, diagnostic.Root );
            Equal( ".retained.txt", diagnostic.RelativePath );
            Equal( IndexExclusionReasons.Hidden, diagnostic.Reason );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }
}
