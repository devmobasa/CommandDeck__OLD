using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static Task IndexedFolderScanKeepsConnectionResponsiveAsync( ) =>
        ExerciseBlockedRootScanAsync( checkResponsiveness: true );

    private static Task IndexedFolderShutdownCancelsCommittedScanAsync( ) =>
        ExerciseBlockedRootScanAsync( checkResponsiveness: false );

    private static async Task ExerciseBlockedRootScanAsync( bool checkResponsiveness )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "The blocked extractor fixture requires Linux." );
        }

        var root = CreateCliSpecRoot( );
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var retained = Directory.CreateDirectory( Path.Combine( root, "retained" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            var bin = Directory.CreateDirectory( Path.Combine( root, "bin" ) ).FullName;
            var started = Path.Combine( root, "extractor-started" );
            var executable = Path.Combine( bin, "pdftotext" );
            // A controlled extractor, following the existing isolation fixtures.
            // ProcessRunner owns and kills the sleeping process on cancellation.
            await File.WriteAllTextAsync( executable,
                "#!/bin/sh\n: > '" + started.Replace( "'", "'\\''", StringComparison.Ordinal ) + "'\nexec sleep 60\n" );
            File.SetUnixFileMode( executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( "PATH", bin + Path.PathSeparator + originalPath );
            await File.WriteAllTextAsync( Path.Combine( retained, "retainedsignal.txt" ), "existing" );
            var pdf = Path.Combine( added, "blocked.pdf" );
            await File.WriteAllTextAsync( pdf, "%PDF fixture" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), retained );
            options = new CommandDeckOptions
            {
                Storage = options.Storage,
                Privacy = options.Privacy,
                Indexing = options.Indexing with
                {
                    EnablePdfExtraction = true,
                    ExtractorTimeoutMilliseconds = 60_000,
                    EnableScheduledMaintenance = false
                }
            };
            var config = Path.Combine( root, "config.json" );
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, default );
            await runtime.Indexer.ReindexAsync( null, default );
            var indexer = ( IndexCoordinator ) runtime.Indexer;
            var watches = indexer.SnapshotWatchedDirectories( ).ToArray( );
            using CancellationTokenSource shutdown = new( );
            var socket = SpecTemporaryFiles.SocketPath( root, "daemon.sock" );
            UnixProtocolServer server = new( runtime, socket );
            var serving = server.RunAsync( shutdown.Token );
            using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 15 ) );
            try
            {
                await using var client = await ConnectWithRetryAsync( socket );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 1,
                    Operation = ProtocolOperations.IndexAdd,
                    Path = added
                }, deadline.Token );
                True( await WaitUntilAsync( ( ) => Task.FromResult( File.Exists( started ) ), TimeSpan.FromSeconds( 5 ) ) );

                if ( checkResponsiveness )
                {
                    // Both requests share the root edit's existing connection.
                    await client.SendAsync( new ProtocolRequest
                    {
                        RequestId = 2,
                        Operation = ProtocolOperations.Search,
                        Query = "retainedsignal"
                    }, deadline.Token );
                    await client.SendAsync( new ProtocolRequest
                    {
                        RequestId = 3,
                        Operation = ProtocolOperations.IndexRoots
                    }, deadline.Token );
                    HashSet<long> done = [];
                    bool searched = false;
                    bool listed = false;
                    using CancellationTokenSource responsive = new( TimeSpan.FromSeconds( 3 ) );
                    await foreach ( var line in client.ReadRawMessagesAsync( responsive.Token ) )
                    {
                        var message = DeserializeMessage( line );
                        if ( message is null )
                        {
                            continue;
                        }
                        if ( message.RequestId == 2 && message.Result?.Title == "retainedsignal.txt" )
                        {
                            searched = true;
                        }
                        if ( message.RequestId == 3 && message.IndexRoots?.Roots.Any( row => row.Path == added && row.Status.StartsWith( "Indexing", StringComparison.Ordinal ) ) == true )
                        {
                            listed = true;
                        }
                        if ( message.Type == ProtocolMessageTypes.Done )
                        {
                            done.Add( message.RequestId );
                        }
                        if ( done.IsSupersetOf( new long[] { 1, 2, 3 } ) )
                        {
                            break;
                        }
                    }
                    True( searched && listed );
                    True( watches.All( path => indexer.SnapshotWatchedDirectories( ).Contains( path ) ) );
                    await File.WriteAllTextAsync( Path.Combine( retained, "incrementalwhileblocked.txt" ), "new" );

                    var listing = await indexer.ManageRootsAsync( ProtocolOperations.IndexRoots, null, null, null, default );
                    await indexer.ManageRootsAsync( ProtocolOperations.IndexRemove, null,
                        listing.Roots.Single( row => row.Path == added ).Id, null, deadline.Token ).AsTask( ).WaitAsync( TimeSpan.FromSeconds( 3 ) );
                    Equal( 0, (await SearchAsync( runtime.Catalog, "blocked" )).Count );

                    File.Delete( started );
                    await indexer.ManageRootsAsync( ProtocolOperations.IndexAdd, added, null, null, default );
                    True( await WaitUntilAsync( ( ) => Task.FromResult( File.Exists( started ) ), TimeSpan.FromSeconds( 5 ) ) );
                    File.Delete( pdf );
                    await indexer.ReindexAsync( null, deadline.Token ).AsTask( ).WaitAsync( TimeSpan.FromSeconds( 3 ) );
                    await indexer.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 3 ) );
                    Equal( 0, (await SearchAsync( runtime.Catalog, "blocked" )).Count );
                }

                await shutdown.CancelAsync( );
                await serving.WaitAsync( TimeSpan.FromSeconds( 3 ) );
                // Observe cancellation before runtime disposal can cause it.
                await indexer.BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 3 ) );
                if ( !checkResponsiveness )
                {
                    await using SqliteCatalog persisted = new( options.Storage );
                    True( await persisted.RequiresReindexAsync( default ) );
                    await File.WriteAllTextAsync( pdf, "finished" );
                    Environment.SetEnvironmentVariable( "PATH", originalPath );
                    await runtime.DisposeAsync( );
                    await using var restarted = await CommandDeckRuntime.CreateAsync( config, default );
                    await (( IndexCoordinator ) restarted.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                    Equal( 1, (await SearchAsync( restarted.Catalog, "blocked" )).Count );
                    True( !await persisted.RequiresReindexAsync( default ) );
                }
            }
            finally
            {
                indexer.CancelBackgroundWork( );
                await shutdown.CancelAsync( );
                await serving.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( root, true );
        }
    }
}
