using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task IndexedFolderCliRoutesToDaemonAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var configured = Directory.CreateDirectory( Path.Combine( root, "configured" ) ).FullName;
            var added = Directory.CreateDirectory( Path.Combine( root, "added" ) ).FullName;
            await File.WriteAllTextAsync( Path.Combine( configured, "configclifolder.txt" ), "configured" );
            await File.WriteAllTextAsync( Path.Combine( added, "addedclifolder.txt" ), "added" );
            var config = Path.Combine( root, "config.json" );
            var options = CreateIncrementalIndexOptions( Path.Combine( root, "catalog.db" ), configured );
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            var original = await File.ReadAllTextAsync( config );
            var local = await RunCommandDeckProcessInDirectoryAsync( root, "index", "add", "./added", "--config", config );
            Equal( 0, local.ExitCode );
            True( local.StandardOutput.Contains( "source=added", StringComparison.Ordinal ) );
            var reopened = await RunCommandDeckProcessAsync( "index", "roots", "--config", config, "--json" );
            Equal( 0, reopened.ExitCode );
            var roots = JsonSerializer.Deserialize( reopened.StandardOutput, CommandDeckJsonContext.Default.IndexRootsResult )!;
            var addedId = roots.Roots.Single( value => value.Path == added ).Id;
            var configuredId = roots.Roots.Single( value => value.Path == configured ).Id;

            await using var runtime = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None );
            using CancellationTokenSource lifetime = new( TimeSpan.FromSeconds( 30 ) );
            var socket = SpecTemporaryFiles.SocketPath( root, "daemon.sock" );
            UnixProtocolServer server = new( runtime, socket );
            var serving = server.RunAsync( lifetime.Token );
            try
            {
                await using ( var probe = await ConnectWithRetryAsync( socket ) )
                { }
                var removed = await RunCommandDeckProcessAsync( "index", "remove", addedId, "--socket", socket );
                Equal( 0, removed.ExitCode );
                Equal( 0, (await SearchAsync( runtime.Catalog, "addedclifolder" )).Count );
                var disabled = await RunCommandDeckProcessAsync( "index", "disable", configuredId, "--socket", socket );
                Equal( 0, disabled.ExitCode );
                True( disabled.StandardOutput.Contains( "enabled=False", StringComparison.Ordinal ) );
                Equal( 0, (await SearchAsync( runtime.Catalog, "configclifolder" )).Count );
                var enabled = await RunCommandDeckProcessAsync( "index", "enable", configuredId, "--socket", socket );
                Equal( 0, enabled.ExitCode );
                await (( IndexCoordinator ) runtime.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                True( (await SearchAsync( runtime.Catalog, "configclifolder" )).Count > 0 );
                Equal( 0, (await RunCommandDeckProcessInDirectoryAsync( root, "index", "add", "./added", "--socket", socket )).ExitCode );
                Equal( 0, (await RunCommandDeckProcessAsync( "index", "disable", configuredId, "--socket", socket )).ExitCode );
                var exportPath = Path.Combine( root, "customizations.json" );
                await File.WriteAllTextAsync( exportPath, ProtocolJson.SerializeBoundedCustomizationExport( await runtime.ExportCustomizationsAsync( lifetime.Token ) ) );
                Equal( 0, (await RunCommandDeckProcessAsync( "preferences", "reset", "--yes", "--socket", socket )).ExitCode );
                await (( IndexCoordinator ) runtime.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                Equal( 0, (await RunCommandDeckProcessAsync( "index", "import", exportPath, "--socket", socket )).ExitCode );
                await (( IndexCoordinator ) runtime.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                Equal( 1, (await SearchAsync( runtime.Catalog, "addedclifolder" )).Count );
                Equal( 0, (await SearchAsync( runtime.Catalog, "configclifolder" )).Count );
                Equal( 0, (await RunCommandDeckProcessAsync( "preferences", "reset", "--yes", "--socket", socket )).ExitCode );
                await (( IndexCoordinator ) runtime.Indexer).BackgroundReindex.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                True( (await SearchAsync( runtime.Catalog, "configclifolder" )).Count > 0 );
                Equal( 0, (await SearchAsync( runtime.Catalog, "addedclifolder" )).Count );
                var rejected = await RunCommandDeckProcessAsync( "index", "add", Path.Combine( root, "missing" ), "--socket", socket );
                Equal( 1, rejected.ExitCode );
                True( rejected.StandardError.Contains( "Folder does not exist.", StringComparison.Ordinal ) );
                var invalid = await RunCommandDeckProcessAsync( "index", "remove", "--socket", socket );
                Equal( 2, invalid.ExitCode );
                Equal( original, await File.ReadAllTextAsync( config ) );
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
