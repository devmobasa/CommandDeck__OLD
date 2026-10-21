using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task IndexedFolderStorePersistsOptionsAndResetAsync( )
    {
        var root = CreateCliSpecRoot( );
        var database = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) };
        try
        {
            await using ( SqliteCatalog catalog = new( database ) )
            {
                await catalog.InitializeAsync( CancellationToken.None );
                await catalog.LauncherPreferences.ReplaceIndexRootsAsync( new IndexRootPreferences
                {
                    Added = [new AddedIndexRoot
                    {
                        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds( 1234 ),
                        Options = new IndexRootOptions
                        {
                            Path = root + "/./", IncludeHidden = true, FollowSymbolicLinks = true,
                            IncludeContentInSearch = false, IncludeMetadataInSearch = false,
                            IncludeFileNameInSearch = false, ExcludeGeneratedDirectories = false,
                            RespectGitIgnore = false, IncludeGlobs = ["*.md"], ExcludeGlobs = ["private"],
                            FileTypePolicies = [new FileTypeIndexPolicyOptions { Pattern = "*.md", MaximumFileBytes = 512 }]
                        }
                    }],
                    DisabledConfiguredPaths = [root + "/./", root + "/"]
                }, CancellationToken.None );
            }

            await using SqliteCatalog reopened = new( database );
            await reopened.InitializeAsync( CancellationToken.None );
            var export = await reopened.LauncherPreferences.ExportAsync( CancellationToken.None );
            var saved = export.IndexRoots.Added.Single( );
            Equal( 1234L, saved.CreatedAt.ToUnixTimeMilliseconds( ) );
            Equal( root, saved.Options.Path );
            True( saved.Options.IncludeHidden && saved.Options.FollowSymbolicLinks );
            True( !saved.Options.IncludeContentInSearch && !saved.Options.IncludeFileNameInSearch && !saved.Options.IncludeMetadataInSearch );
            True( !saved.Options.ExcludeGeneratedDirectories && !saved.Options.RespectGitIgnore );
            Equal( "*.md", saved.Options.IncludeGlobs.Single( ) );
            Equal( "private", saved.Options.ExcludeGlobs.Single( ) );
            Equal( 512L, saved.Options.FileTypePolicies.Single( ).MaximumFileBytes );
            Equal( root, export.IndexRoots.DisabledConfiguredPaths.Single( ) );

            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await reopened.LauncherPreferences.ReplaceIndexRootsAsync( new IndexRootPreferences
                {
                    Added = [new AddedIndexRoot { Options = new IndexRootOptions { Path = "" } }]
                }, CancellationToken.None ) );
            Equal( 1, (await reopened.GetIndexRootsAsync( CancellationToken.None )).Added.Count );

            await reopened.LauncherPreferences.ResetAsync( CancellationToken.None );
            var reset = await reopened.GetIndexRootsAsync( CancellationToken.None );
            Equal( 0, reset.Added.Count );
            Equal( 0, reset.DisabledConfiguredPaths.Count );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task IndexedFolderProtocolAppliesLiveAndResetAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var configured = Directory.CreateDirectory( Path.Combine( root, "configured" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            var protectedRoot = Directory.CreateDirectory( Path.Combine( root, "protected" ) ).FullName;
            var missing = Path.Combine( root, "missing" );
            await File.WriteAllTextAsync( Path.Combine( configured, "configuredneedle.txt" ), "original" );
            await File.WriteAllTextAsync( Path.Combine( added, "addedneedle.txt" ), "original" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), configured );
            options.Quicklinks.Clear( );
            options.Quicklinks.Add( TestQuicklink( "configured", alias: "configdocs" ) );
            options.Indexing.Roots.AddRange( [new IndexRootOptions { Path = missing }, new IndexRootOptions { Path = protectedRoot }] );
            options.Privacy.NeverIndex.Add( protectedRoot );
            var config = Path.Combine( root, "config.json" );
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );

            await using var runtime = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None );
            await runtime.Indexer.ReindexAsync( null, CancellationToken.None );
            using CancellationTokenSource lifetime = new( TimeSpan.FromSeconds( 30 ) );
            var socket = SpecTemporaryFiles.SocketPath( root, "daemon.sock" );
            UnixProtocolServer server = new( runtime, socket );
            var serving = server.RunAsync( lifetime.Token );
            try
            {
                await using var client = await ConnectWithRetryAsync( socket );
                long sequence = 0;
                async Task<ProtocolMessage> Send( string operation, string? path = null, string? id = null,
                    IndexRootPreferences? import = null )
                {
                    var requestId = ++sequence;
                    await client.SendAsync( new ProtocolRequest
                    {
                        RequestId = requestId,
                        Operation = operation,
                        Path = path,
                        RootId = id,
                        IndexRoots = import,
                        Confirmed = true
                    }, lifetime.Token );
                    ProtocolMessage? result = null;
                    await foreach ( var line in client.ReadRawMessagesAsync( lifetime.Token ) )
                    {
                        var message = DeserializeMessage( line );
                        if ( message?.RequestId != requestId )
                        {
                            continue;
                        }

                        if ( message.Type == ProtocolMessageTypes.Done )
                        {
                            break;
                        }

                        result = message;
                    }
                    return result ?? throw new InvalidOperationException( "Missing response." );
                }

                var listed = (await Send( ProtocolOperations.IndexRoots )).IndexRoots!;
                var configuredId = listed.Roots.Single( value => value.Path == configured ).Id;
                var protectedIssue = listed.Roots.Single( value => value.Path == protectedRoot ).Issue;
                Equal( "Folder does not exist.", listed.Roots.Single( value => value.Path == missing ).Issue );
                foreach ( var invalid in new[] { "", "bad\0path", missing, protectedRoot } )
                {
                    var rejected = await Send( ProtocolOperations.IndexAdd, invalid );
                    Equal( ProtocolMessageTypes.Error, rejected.Type );
                    string expected;
                    try
                    {
                        ConfigurationValidator.ValidateIndexRoot( new IndexRootOptions { Path = invalid } );
                        expected = invalid == protectedRoot ? protectedIssue! : "Folder does not exist.";
                    }
                    catch ( InvalidOperationException exception ) { expected = exception.Message; }
                    Equal( expected, rejected.Error );
                }

                var result = (await Send( ProtocolOperations.IndexAdd, added + "/./" )).IndexRoots!;
                await (( IndexCoordinator ) runtime.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                var addedId = result.Roots.Single( value => value.Source == "added" ).Id;
                True( (await SearchAsync( runtime.Catalog, "addedneedle" )).Count > 0 );
                True( (( IndexCoordinator ) runtime.Indexer).SnapshotWatchedDirectories( ).Contains( added ) );
                await File.WriteAllTextAsync( Path.Combine( added, "watchedneedle.txt" ), "new" );
                True( await WaitUntilAsync( async ( ) => (await SearchAsync( runtime.Catalog, "watchedneedle" )).Count > 0, TimeSpan.FromSeconds( 3 ) ) );
                Equal( ProtocolMessageTypes.Error, (await Send( ProtocolOperations.IndexAdd, added + "/" )).Type );
                Equal( ProtocolMessageTypes.Error, (await Send( ProtocolOperations.IndexRemove, id: configuredId )).Type );
                Equal( ProtocolMessageTypes.Error, (await Send( ProtocolOperations.IndexDisable, id: addedId )).Type );

                await runtime.ChangeQuicklinkAsync( "save", TestQuicklink( ), null, lifetime.Token );
                await runtime.ChangeQuicklinkAsync( "disable", null, "configured", lifetime.Token );
                var export = await runtime.ExportCustomizationsAsync( lifetime.Token );
                var json = JsonSerializer.Serialize( export, CommandDeckJsonContext.Default.CustomizationExport );
                export = JsonSerializer.Deserialize( json, CommandDeckJsonContext.Default.CustomizationExport );
                if ( export is null )
                {
                    throw new InvalidOperationException( "Missing combined customization export." );
                }

                Equal( added, export.IndexRoots.Added.Single( ).Options.Path );
                Equal( "docs", export.Quicklinks.UserQuicklinks.Single( ).Quicklink.Id );
                Equal( "configured", export.Quicklinks.DisabledConfiguredIds.Single( ) );
                await Send( ProtocolOperations.IndexRemove, id: addedId );
                Equal( 0, (await SearchAsync( runtime.Catalog, "addedneedle" )).Count );
                True( !(( IndexCoordinator ) runtime.Indexer).SnapshotWatchedDirectories( ).Contains( added ) );
                await File.WriteAllTextAsync( Path.Combine( added, "removedneedle.txt" ), "new" );
                await Send( ProtocolOperations.IndexDisable, id: configuredId );
                Equal( 0, (await SearchAsync( runtime.Catalog, "configuredneedle" )).Count );
                True( !(( IndexCoordinator ) runtime.Indexer).SnapshotWatchedDirectories( ).Contains( configured ) );
                await Send( ProtocolOperations.IndexEnable, id: configuredId );
                await (( IndexCoordinator ) runtime.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                True( (await SearchAsync( runtime.Catalog, "configuredneedle" )).Count > 0 );
                True( (( IndexCoordinator ) runtime.Indexer).SnapshotWatchedDirectories( ).Contains( configured ) );
                Equal( 0, (await SearchAsync( runtime.Catalog, "removedneedle" )).Count );
                await Send( ProtocolOperations.IndexRootsImport, import: export.IndexRoots );
                await (( IndexCoordinator ) runtime.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                True( (await SearchAsync( runtime.Catalog, "removedneedle" )).Count > 0 );
                await Send( ProtocolOperations.IndexDisable, id: configuredId );
                await ExecuteCatalogSqlAsync( options.Storage.DatabasePath, """
                    CREATE TRIGGER reject_combined_reset BEFORE INSERT ON catalog_items
                    WHEN new.kind='quicklink' BEGIN SELECT RAISE(ABORT,'publication rejected'); END;
                    """ );
                Equal( ProtocolMessageTypes.Error, (await Send( ProtocolOperations.CustomizationsReset )).Type );
                var retained = (await Send( ProtocolOperations.IndexRoots )).IndexRoots!;
                True( !retained.Roots.Single( value => value.Id == configuredId ).Enabled );
                True( retained.Roots.Any( value => value.Source == "added" ) );
                Equal( "quicklink:docs", (await (( SqliteCatalog ) runtime.Catalog).FindAliasAsync( "docs", lifetime.Token ))?.Id );
                await ExecuteCatalogSqlAsync( options.Storage.DatabasePath, "DROP TRIGGER reject_combined_reset;" );
                await Send( ProtocolOperations.CustomizationsReset );
                await (( IndexCoordinator ) runtime.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                var reset = (await Send( ProtocolOperations.IndexRoots )).IndexRoots!;
                True( reset.Roots.All( value => value.Source == "configuration" && value.Enabled ) );
                True( (await SearchAsync( runtime.Catalog, "configuredneedle" )).Count > 0 );
                Equal( 0, (await SearchAsync( runtime.Catalog, "addedneedle" )).Count );
                var quicklinks = await runtime.GetQuicklinksAsync( lifetime.Token );
                Equal( "configured", quicklinks.Quicklinks.Single( ).Quicklink.Id );
                True( quicklinks.Quicklinks.Single( ).Enabled );
                True( await (( SqliteCatalog ) runtime.Catalog).FindAliasAsync( "docs", lifetime.Token ) is null );
                Equal( "quicklink:configured", (await (( SqliteCatalog ) runtime.Catalog).FindAliasAsync( "configdocs", lifetime.Token ))?.Id );

                await Send( ProtocolOperations.IndexRootsImport, import: export.IndexRoots );
                await runtime.RestoreQuicklinksAsync( export, lifetime.Token );
                await (( IndexCoordinator ) runtime.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                True( (await SearchAsync( runtime.Catalog, "addedneedle" )).Count > 0 );
                Equal( "quicklink:docs", (await (( SqliteCatalog ) runtime.Catalog).FindAliasAsync( "docs", lifetime.Token ))?.Id );
                True( !(await runtime.GetQuicklinksAsync( lifetime.Token )).Quicklinks.Single( value => value.Source == "configured" ).Enabled );
            }
            finally
            {
                await lifetime.CancelAsync( );
                await serving;
            }
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }
}
