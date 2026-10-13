namespace CommandDeck.Specs;

public static partial class Program
{
    private static CommandDeckOptions DeveloperIndexOptions( string database, string root,
        bool generated = true, bool incremental = true, List<string>? include = null ) => new( )
        {
            Storage = new StorageOptions { DatabasePath = database },
            Privacy = new PrivacyOptions { NeverIndex = [] },
            Indexing = new IndexingOptions
            {
                Roots = [new IndexRootOptions
            {
                Path = root, IncludeHidden = true, IncludeContentInSearch = true,
                ExcludeGeneratedDirectories = generated, IncludeGlobs = include ?? []
            }],
                EnableIncrementalIndexing = incremental,
                EnableScheduledMaintenance = false,
                IndexDesktopApplications = false,
                IndexOmarchyCommands = false,
                IncrementalDebounceMilliseconds = 40,
                IncrementalMaximumLatencyMilliseconds = 150,
                IncrementalEventBatchLimit = 32
            }
        };

    private static async Task GeneratedFileBurstsDoNotReachTheJournalAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "project" );
        var generated = Path.Combine( root, "node_modules/library" );
        Directory.CreateDirectory( generated );
        await File.WriteAllTextAsync( Path.Combine( root, "source.txt" ), "source marker" );
        var options = DeveloperIndexOptions( Path.Combine( sandbox, "catalog.db" ), root );
        try
        {
            await using SqliteCatalog catalog = new( options.Storage );
            TrackingSearchCatalog tracking = new( catalog, catalog );
            await using IndexCoordinator indexer = new( tracking, tracking, options );
            await indexer.ReindexAsync( null, CancellationToken.None );
            var before = await indexer.GetIncrementalStatusAsync( CancellationToken.None );
            var state = await File.ReadAllTextAsync( options.Storage.DatabasePath + ".index-state" );
            var applies = tracking.ApplyPathChangesCount;
            for ( var index = 0; index < 300; index++ )
            {
                await File.WriteAllTextAsync( Path.Combine( generated, $"bundle-{index}.txt" ), "dependency marker" );
            }

            // The generated tree has no watch, so the burst never reaches the
            // daemon, let alone the journal.
            True( indexer.SnapshotWatchedDirectories( ).All( path =>
                !path.Contains( "node_modules", StringComparison.Ordinal ) ) );
            await Task.Delay( 500 );
            var after = await indexer.GetIncrementalStatusAsync( CancellationToken.None );
            Equal( 0L, after.PendingEventCount );
            True( !after.ReconciliationRequired );
            Equal( before.MaximumObservedBatchCount, after.MaximumObservedBatchCount );
            Equal( applies, tracking.ApplyPathChangesCount );
            Equal( state, await File.ReadAllTextAsync( options.Storage.DatabasePath + ".index-state" ) );
            Equal( 0, (await SearchAsync( catalog, "dependency marker" )).Count );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task RepositoryIgnoreEditsReconcileExistingRowsAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "project" );
        var output = Path.Combine( root, "generated" );
        Directory.CreateDirectory( output );
        var options = DeveloperIndexOptions( Path.Combine( sandbox, "catalog.db" ), root,
            include: ["generated/keep.txt"] );
        try
        {
            Equal( 0, (await RunIgnoreGitAsync( root, ["init", "--quiet"], null )).ExitCode );
            await File.WriteAllTextAsync( Path.Combine( output, "artifact.txt" ), "artifact marker" );
            await File.WriteAllTextAsync( Path.Combine( output, "keep.txt" ), "included marker" );
            await using SqliteCatalog catalog = new( options.Storage );
            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.ReindexAsync( null, CancellationToken.None );
            True( (await SearchAsync( catalog, "artifact marker" )).Count > 0 );
            var ignore = Path.Combine( root, ".gitignore" );
            await File.WriteAllTextAsync( ignore, "generated/\n" );
            True( await WaitUntilAsync( async ( ) => (await SearchAsync( catalog, "artifact marker" )).Count == 0,
                TimeSpan.FromSeconds( 3 ) ) );
            True( (await SearchAsync( catalog, "included marker" )).Count > 0 );
            var inspection = await indexer.InspectAsync( CancellationToken.None );
            True( inspection.ExclusionDiagnostics.Any( value => value.Reason == IndexExclusionReasons.RepositoryIgnore ) );
            await File.WriteAllTextAsync( Path.Combine( output, "later.txt" ), "later marker" );
            File.Delete( ignore );
            True( await WaitForCatalogResultAsync( catalog, "later marker", value => value.Title == "later.txt",
                TimeSpan.FromSeconds( 3 ) ) );
            True( (await SearchAsync( catalog, "artifact marker" )).Count > 0 );
            inspection = await indexer.InspectAsync( CancellationToken.None );
            True( inspection.ExclusionDiagnostics.All( value => value.Reason != IndexExclusionReasons.RepositoryIgnore ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task ProjectMarkerChangesAndExcludedRenamesConvergeAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "project" );
        Directory.CreateDirectory( Path.Combine( root, "bin" ) );
        Directory.CreateDirectory( Path.Combine( root, "node_modules" ) );
        var options = DeveloperIndexOptions( Path.Combine( sandbox, "catalog.db" ), root );
        try
        {
            await File.WriteAllTextAsync( Path.Combine( root, "bin/generated.txt" ), "buildoutput marker" );
            var source = Path.Combine( root, "source.txt" );
            await File.WriteAllTextAsync( source, "renamed marker" );
            await using SqliteCatalog catalog = new( options.Storage );
            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.ReindexAsync( null, CancellationToken.None );
            True( (await SearchAsync( catalog, "buildoutput marker" )).Count > 0 );
            var marker = Path.Combine( root, "App.csproj" );
            await File.WriteAllTextAsync( marker, "<Project />" );
            True( await WaitUntilAsync( async ( ) => (await SearchAsync( catalog, "buildoutput marker" )).Count == 0,
                TimeSpan.FromSeconds( 3 ) ) );
            File.Delete( marker );
            True( await WaitForCatalogResultAsync( catalog, "buildoutput marker", value => value.Title == "generated.txt",
                TimeSpan.FromSeconds( 3 ) ) );

            var ignored = Path.Combine( root, "node_modules/moved.txt" );
            File.Move( source, ignored );
            True( await WaitUntilAsync( async ( ) => (await SearchAsync( catalog, "renamed marker" )).Count == 0,
                TimeSpan.FromSeconds( 3 ) ) );
            var restored = Path.Combine( root, "restored.txt" );
            File.Move( ignored, restored );
            True( await WaitForCatalogResultAsync( catalog, "renamed marker", value => value.Title == "restored.txt",
                TimeSpan.FromSeconds( 3 ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }
}
