namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExclusionChangesWhileStoppedAreReconciledAsync( )
    {
        foreach ( var incremental in new[] { true, false } )
        {
            var sandbox = CreateIncrementalSpecRoot( );
            var root = Path.Combine( sandbox, "project" );
            Directory.CreateDirectory( Path.Combine( root, "node_modules" ) );
            var database = Path.Combine( sandbox, "catalog.db" );
            var original = DeveloperIndexOptions( database, root, generated: false, incremental: incremental );
            var updated = DeveloperIndexOptions( database, root, incremental: incremental );
            try
            {
                Equal( 0, (await RunIgnoreGitAsync( root, ["init", "--quiet"], null )).ExitCode );
                await File.WriteAllTextAsync( Path.Combine( root, "node_modules/dependency.txt" ), "dependency marker" );
                await File.WriteAllTextAsync( Path.Combine( root, "source.txt" ), "source marker" );
                await using SqliteCatalog catalog = new( original.Storage );
                await using ( IndexCoordinator first = new( catalog, catalog, original ) )
                {
                    await first.ReindexAsync( null, CancellationToken.None );
                }
                True( (await SearchAsync( catalog, "dependency marker" )).Count > 0 );
                TrackingSearchCatalog? tracked = null;
                (Task FirstItem, Action Release)? pausedReplacement = null;
                if ( !incremental )
                {
                    tracked = new TrackingSearchCatalog( catalog, catalog );
                    pausedReplacement = tracked.PauseNextReplacement( );
                }

                await using ( IndexCoordinator second = tracked is null
                    ? new IndexCoordinator( catalog, catalog, updated )
                    : new IndexCoordinator( tracked, tracked, updated ) )
                {
                    var start = second.StartAsync( CancellationToken.None ).AsTask( );
                    if ( pausedReplacement is { } paused )
                    {
                        await paused.FirstItem.WaitAsync( TimeSpan.FromSeconds( 3 ) );
                        var startupCompletedBeforeReconciliation = start.IsCompletedSuccessfully;
                        paused.Release( );
                        True( startupCompletedBeforeReconciliation );
                    }
                    await start;
                    True( await WaitUntilAsync( async ( ) => (await SearchAsync( catalog, "dependency marker" )).Count == 0,
                        TimeSpan.FromSeconds( 3 ) ) );
                }
                await File.WriteAllTextAsync( Path.Combine( root, ".gitignore" ), "source.txt\n" );
                await using ( IndexCoordinator third = new( catalog, catalog, updated ) )
                {
                    await third.StartAsync( CancellationToken.None );
                    True( await WaitUntilAsync( async ( ) => (await SearchAsync( catalog, "source marker" )).Count == 0,
                        TimeSpan.FromSeconds( 3 ) ) );
                }
                File.Delete( Path.Combine( root, ".gitignore" ) );
                await using ( IndexCoordinator fourth = new( catalog, catalog, updated ) )
                {
                    await fourth.StartAsync( CancellationToken.None );
                    True( await WaitForCatalogResultAsync( catalog, "source marker", value => value.Title == "source.txt",
                        TimeSpan.FromSeconds( 3 ) ) );
                }
            }
            finally
            {
                DeleteIncrementalSpecRoot( sandbox );
            }
        }
    }

    private static async Task AncestorIgnoreChangesAreWatchedOutsideTheIndexRootAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var repository = Path.Combine( sandbox, "repository" );
        var root = Path.Combine( repository, "src" );
        Directory.CreateDirectory( root );
        var options = DeveloperIndexOptions( Path.Combine( sandbox, "catalog.db" ), root );
        try
        {
            Equal( 0, (await RunIgnoreGitAsync( repository, ["init", "--quiet"], null )).ExitCode );
            await File.WriteAllTextAsync( Path.Combine( root, "source.txt" ), "ancestor marker" );
            await using SqliteCatalog catalog = new( options.Storage );
            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.ReindexAsync( null, CancellationToken.None );
            var ignore = Path.Combine( repository, ".gitignore" );
            await File.WriteAllTextAsync( ignore, "src/\n" );
            True( await WaitUntilAsync( async ( ) => (await SearchAsync( catalog, "ancestor marker" )).Count == 0,
                TimeSpan.FromSeconds( 3 ) ) );
            File.Delete( ignore );
            True( await WaitForCatalogResultAsync( catalog, "ancestor marker", value => value.Title == "source.txt",
                TimeSpan.FromSeconds( 3 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }
}
