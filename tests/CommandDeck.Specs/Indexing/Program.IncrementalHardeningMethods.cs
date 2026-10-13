using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task IncrementalChangesPreserveOtherProvidersAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        Directory.CreateDirectory( root );
        var database = Path.Combine( root, "catalog.db" );
        var sharedPath = Path.Combine( root, "shared.desktop" );

        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync( InitialItems( ), null, CancellationToken.None );

            await catalog.ApplyPathChangesAsync(
            [
                new IndexedPathChange
                {
                    Path = sharedPath,
                    Items =
                    [
                        new IndexedItem
                        {
                            StableId = "file:shared-new",
                            Kind = ResultKinds.File,
                            Title = "shared.desktop",
                            Path = sharedPath,
                            Body = "replacement file marker",
                            ProviderId = "files",
                            SourceId = SourceIds.Files
                        }
                    ]
                }
            ],
            CancellationToken.None );

            var application = await SearchAsync(
                catalog,
                "provider application sentinel" );
            Equal( 1, application.Count );
            Equal( "app:shared", application[0].Id );

            var file = await SearchAsync( catalog, "replacement file marker" );
            Equal( 1, file.Count );
            Equal( "file:shared-new", file[0].Id );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }

        async IAsyncEnumerable<IndexedItem> InitialItems( )
        {
            yield return new IndexedItem
            {
                StableId = "app:shared",
                Kind = ResultKinds.Application,
                Title = "Provider application sentinel",
                Path = sharedPath,
                ProviderId = "applications",
                SourceId = SourceIds.Applications
            };
            yield return new IndexedItem
            {
                StableId = "file:shared-old",
                Kind = ResultKinds.File,
                Title = "shared.desktop",
                Path = sharedPath,
                Body = "old file marker",
                ProviderId = "files",
                SourceId = SourceIds.Files
            };
            await Task.CompletedTask;
        }
    }

    private static async Task IncrementalJournalHasOnePrivateRuntimeOwnerAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        Directory.CreateDirectory( documents );
        var database = Path.Combine( root, "catalog.db" );

        try
        {
            var options = CreateIncrementalIndexOptions( database, documents );
            await using SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( CancellationToken.None );

            await using ( var first = new IndexCoordinator( catalog, catalog, options ) )
            {
                await using ( var second = new IndexCoordinator( catalog, catalog, options ) )
                {
                    await first.StartAsync( CancellationToken.None );
                    await second.StartAsync( CancellationToken.None );

                    var firstStatus =
                        await first.GetIncrementalStatusAsync( CancellationToken.None );
                    var secondStatus =
                        await second.GetIncrementalStatusAsync( CancellationToken.None );
                    Equal( 1, firstStatus.WatchedRootCount );
                    Equal( 0, secondStatus.WatchedRootCount );
                    True( secondStatus.LastError?.Contains( "owned", StringComparison.OrdinalIgnoreCase )
                        is true );

                    await first.ReindexAsync( progress: null, CancellationToken.None );
                    AssertPrivateFile( database + ".index-owner" );
                    AssertPrivateFile( database + ".index-events" );
                    AssertPrivateFile( database + ".index-state" );
                    AssertPrivateFile( database + ".index-window" );
                }
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task CatalogMaintenanceSerializesWithIndexingAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        Directory.CreateDirectory( documents );
        var initialPath = Path.Combine( documents, "initial.txt" );
        await File.WriteAllTextAsync( initialPath, "maintenance initial marker" );

        try
        {
            var options = CreateIncrementalIndexOptions(
                Path.Combine( root, "catalog.db" ),
                documents );
            SqliteCatalog inner = new( options.Storage );
            await inner.InitializeAsync( CancellationToken.None );
            await using TrackingSearchCatalog catalog = new( inner, inner );
            await using var indexer = new IndexCoordinator( catalog, catalog, options );

            (var firstItem, var release) = catalog.PauseNextReplacement( );
            var reindex = indexer.ReindexAsync(
                progress: null,
                CancellationToken.None ).AsTask( );
            await firstItem.WaitAsync( TimeSpan.FromSeconds( 2 ) );

#pragma warning disable CA1859 // Exercise maintenance through the catalog-operations contract.
            ICatalogOperations operations = indexer;
#pragma warning restore CA1859
            var purge = operations.PurgeProjectionAsync(
                CancellationToken.None ).AsTask( );
            await Task.Delay( 50 );
            True( !purge.IsCompleted );

            release( );
            await reindex;
            var result = await purge;
            True( result.Succeeded );
            True( result.RebuildRequired );
            Equal( 0, (await SearchAsync( catalog, "maintenance initial" )).Count );

            await File.WriteAllTextAsync(
                Path.Combine( documents, "after-purge.txt" ),
                "maintenance after purge marker" );
            await Task.Delay( 500 );
            Equal( 0, (await SearchAsync( catalog, "maintenance after purge" )).Count );

            await indexer.ReindexAsync( progress: null, CancellationToken.None );
            Equal( 1, (await SearchAsync( catalog, "maintenance after purge" )).Count );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task CatalogRepairPausesIndexingUntilAFullScanAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        Directory.CreateDirectory( documents );
        await File.WriteAllTextAsync( Path.Combine( documents, "initial.txt" ), "repair initial marker" );

        try
        {
            var options = CreateIncrementalIndexOptions(
                Path.Combine( root, "catalog.db" ),
                documents );
            SqliteCatalog inner = new( options.Storage );
            await inner.InitializeAsync( CancellationToken.None );
            await using TrackingSearchCatalog catalog = new( inner, inner );
            await using var indexer = new IndexCoordinator( catalog, catalog, options );
            await indexer.ReindexAsync( progress: null, CancellationToken.None );

            await using ( SqliteConnection connection = new( $"Data Source={options.Storage.DatabasePath}" ) )
            {
                await connection.OpenAsync( );
                await using var command = connection.CreateCommand( );
                command.CommandText = "DELETE FROM catalog_fts WHERE rowid = (SELECT min(rowid) FROM catalog_fts);";
                await command.ExecuteNonQueryAsync( );
            }

#pragma warning disable CA1859 // Exercise maintenance through the catalog-operations contract.
            ICatalogOperations operations = indexer;
#pragma warning restore CA1859
            var repaired = await operations.RepairAsync( CancellationToken.None );
            True( repaired.Succeeded );
            True( repaired.RebuildRequired );

            await File.WriteAllTextAsync(
                Path.Combine( documents, "after-repair.txt" ),
                "repair after marker" );
            await Task.Delay( 500 );
            Equal( 0, (await SearchAsync( catalog, "repair after marker" )).Count );

            await indexer.ReindexAsync( progress: null, CancellationToken.None );
            Equal( 1, (await SearchAsync( catalog, "repair after marker" )).Count );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static async Task CatalogWritesRequireProcessOwnershipAsync( )
    {
        var root = CreateIncrementalSpecRoot( );
        var documents = Path.Combine( root, "documents" );
        var database = Path.Combine( root, "catalog.db" );
        var config = Path.Combine( root, "config.json" );
        Directory.CreateDirectory( documents );
        await File.WriteAllTextAsync(
            Path.Combine( documents, "ownership.txt" ),
            "ownership marker" );

        try
        {
            var options = CreateIncrementalIndexOptions( database, documents );
            await File.WriteAllTextAsync(
                config,
                System.Text.Json.JsonSerializer.Serialize(
                    options,
                    CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var owner = await CommandDeckRuntime.CreateAsync(
                config,
                CancellationToken.None );
            await owner.Indexer.ReindexAsync( progress: null, CancellationToken.None );
            // One user file and the query-free Clipboard History navigation command.
            Equal( 2L, (await owner.CatalogOperations.InspectAsync( CancellationToken.None ))
                .Stats.ItemCount );

            var purge = await RunCommandDeckProcessAsync(
                "catalog", "purge", "--yes", "--json", "--config", config );
            if ( purge.ExitCode != 1 )
            {
                throw new InvalidOperationException(
                    $"Expected owned purge rejection, got {purge.ExitCode}: " +
                    $"stdout={purge.StandardOutput}; stderr={purge.StandardError}" );
            }
            True( purge.StandardError.Contains( "owned", StringComparison.OrdinalIgnoreCase ) );

            var status = await RunCommandDeckProcessAsync( "index", "status", "--json", "--config", config );
            Equal( 0, status.ExitCode );

            var reindex = await RunCommandDeckProcessAsync(
                "reindex", "--quiet", "--config", config );
            if ( reindex.ExitCode != 1 )
            {
                throw new InvalidOperationException(
                    $"Expected owned reindex rejection, got {reindex.ExitCode}: " +
                    $"stdout={reindex.StandardOutput}; stderr={reindex.StandardError}" );
            }
            True( reindex.StandardError.Contains( "owned", StringComparison.OrdinalIgnoreCase ) );
            Equal( 2L, (await owner.CatalogOperations.InspectAsync( CancellationToken.None ))
                .Stats.ItemCount );

            var result = await owner.CatalogOperations.PurgeProjectionAsync(
                CancellationToken.None );
            True( result.Succeeded );
            Equal( 0L, (await owner.CatalogOperations.InspectAsync( CancellationToken.None ))
                .Stats.ItemCount );
        }
        finally
        {
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static void AssertPrivateFile( string path )
    {
        True( File.Exists( path ) );
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var mode = File.GetUnixFileMode( path );
        var exposed = mode & (UnixFileMode.GroupRead |
                              UnixFileMode.GroupWrite |
                              UnixFileMode.GroupExecute |
                              UnixFileMode.OtherRead |
                              UnixFileMode.OtherWrite |
                              UnixFileMode.OtherExecute);
        Equal( ( UnixFileMode ) 0, exposed );
    }
}
