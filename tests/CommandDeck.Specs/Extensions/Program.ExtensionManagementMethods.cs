using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionManagementUsesDaemonAsync( )
    {
        var root = CreateCliSpecRoot( );
        using CancellationTokenSource lifetime = new( TimeSpan.FromSeconds( 45 ) );
        Task? serverTask = null;
        try
        {
            var config = await WriteManagedSpecConfigAsync( root, new AiOptions { Enabled = false } );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, lifetime.Token );
            var socket = SpecTemporaryFiles.SocketPath( root, "extensions.sock" );
            serverTask = new UnixProtocolServer( runtime, socket ).RunAsync( lifetime.Token );
            var archive = WritePackageFixture( root, PackageFixture( ) );
            var inspect = await RunCommandDeckProcessAsync( "extensions", "inspect", "--archive", archive, "--json", "--socket", socket );
            Equal( 0, inspect.ExitCode );
            var inspected = JsonSerializer.Deserialize( inspect.StandardOutput, CommandDeckJsonContext.Default.ExtensionInventory )!;
            Equal( 0, inspected.Total );
            True( inspected.ArchiveDigest is { Length: 64 } );
            var denied = await RunCommandDeckProcessAsync( "extensions", "install", "--archive", archive, "--digest", inspected.ArchiveDigest!, "--socket", socket );
            True( denied.ExitCode != 0 );
            var install = await RunCommandDeckProcessAsync( "extensions", "install", "--archive", archive, "--digest", inspected.ArchiveDigest!, "--yes", "--json", "--socket", socket );
            Equal( 0, install.ExitCode );
            var installed = JsonSerializer.Deserialize( install.StandardOutput, CommandDeckJsonContext.Default.ExtensionInventory )!;
            Equal( 1, installed.Total );
            True( !installed.Enabled && !installed.Packages[0].Enabled );
            Equal( 0, (await RunCommandDeckProcessAsync( "extensions", "enable", installed.Packages[0].Id, "--socket", socket )).ExitCode );
            Equal( 0, (await RunCommandDeckProcessAsync( "extensions", "enable", "--socket", socket )).ExitCode );
            var bootstrap = await runtime.GetBootstrapAsync( lifetime.Token );
            var source = bootstrap.Sources.Single( source => source.Id.StartsWith( "extension:", StringComparison.Ordinal ) );
            var filter = bootstrap.Filters.Single( filter => filter.SourceId == source.Id );
            var rows = await runtime.SearchAsync( new SearchRequest { Query = filter.Token + " UUID", Limit = 10 }, lifetime.Token );
            True( rows.Any( row => row.Kind == ResultKinds.ExtensionCommand && row.SourceId == source.Id ) );
            var expression = new QueryExpression { IncludeFilterIds = [filter.Id], Clauses = [new( ) { Text = "UUID" }] };
            await runtime.Preferences.SaveSavedSearchAsync( new SavedSearch { Id = "", Name = "Utilities", Expression = expression }, lifetime.Token );
            await runtime.SetFilterAliasAsync( "utils", filter.Id, lifetime.Token );
            var request = new ProtocolRequest { RequestId = 10, Operation = ProtocolOperations.Search, Expression = expression };
            ProtocolRequestValidator.Validate( request, runtime.Contributions );
            ProtocolRequestValidator.Validate( request, deferFilterResolution: true );
            await using var client = await ConnectWithRetryAsync( socket );
            await client.SendAsync( request, lifetime.Token );
            var searchMessage = await ReadMessageAsync( client, 10, ProtocolMessageTypes.Result, lifetime.Token );
            True( searchMessage?.Result?.Kind == ResultKinds.ExtensionCommand );
            Equal( 0, (await RunCommandDeckProcessAsync( "extensions", "disable", installed.Packages[0].Id, "--socket", socket )).ExitCode );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await runtime.SearchAsync( new SearchRequest { Query = "utils: UUID" }, lifetime.Token ) );
            Equal( 1, (await runtime.Preferences.GetSavedSearchesAsync( lifetime.Token )).Count );
            var unavailable = await runtime.GetBootstrapAsync( lifetime.Token );
            True( !unavailable.Filters.Single( value => value.Id == filter.Id ).Available );
            Equal( 0, (await RunCommandDeckProcessAsync( "extensions", "remove", installed.Packages[0].Id, "--yes", "--socket", socket )).ExitCode );
            await lifetime.CancelAsync( );
            await serverTask;
        }
        finally
        {
            await lifetime.CancelAsync( );
            if ( serverTask is not null )
            {
                await serverTask;
            }
            Directory.Delete( root, recursive: true );
        }
    }
}
