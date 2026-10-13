using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task<int> RunNativeIndexAotAsync( string executable )
    {
        True( OperatingSystem.IsLinux( ) );
        var root = CreateIncrementalSpecRoot( );
        Process? daemon = null;
        try
        {
            var documents = Directory.CreateDirectory( Path.Combine( root, "documents" ) ).FullName;
            var excluded = Directory.CreateDirectory( Path.Combine( documents, "excluded" ) ).FullName;
            var config = Path.Combine( root, "config.json" );
            var socket = Path.Combine( root, "daemon.sock" );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Indexing = new IndexingOptions
                {
                    Roots = [new IndexRootOptions { Path = documents, IncludeContentInSearch = true, ExcludeGlobs = ["excluded/**"] }],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false,
                    IncrementalDebounceMilliseconds = 40,
                    IncrementalMaximumLatencyMilliseconds = 300
                },
                Privacy = new PrivacyOptions { StoreUsageHistory = false, NeverIndex = [] },
                Ai = new AiOptions { Enabled = false, Providers = [] },
                Agent = new AgentOptions { StateDirectory = Path.Combine( root, "state" ), RuntimeDirectory = Path.Combine( root, "runtime" ) }
            };
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            ProcessStartInfo start = new( Path.GetFullPath( executable ) )
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "daemon", "--config", config, "--socket", socket }
            };
            start.Environment["XDG_CONFIG_HOME"] = Path.Combine( root, "config" );
            start.Environment["XDG_STATE_HOME"] = Path.Combine( root, "xdg-state" );
            start.Environment["XDG_CACHE_HOME"] = Path.Combine( root, "cache" );
            daemon = Process.Start( start );
            if ( daemon is null )
            {
                throw new InvalidOperationException( "Native daemon could not start." );
            }

            var output = daemon.StandardOutput.ReadToEndAsync( );
            var errors = daemon.StandardError.ReadToEndAsync( );
            await using var client = await ConnectWithRetryAsync( socket );
            await client.SendAsync( new ProtocolRequest { RequestId = 1, Operation = ProtocolOperations.Reindex }, default );
            var indexed = await CollectUntilDoneAsync( client, 1 );
            True( indexed.Any( static message => message.ReindexReport is not null ) );
            True( indexed.All( static message => message.Type != ProtocolMessageTypes.Error ) );

            await client.SendAsync( new ProtocolRequest { RequestId = 2, Operation = ProtocolOperations.Status }, default );
            var status = await CollectUntilDoneAsync( client, 2 );
            True( status.Single( static message => message.RuntimeStatus is not null ).RuntimeStatus!.Indexing.Roots.Single( ).Watched );

            var nested = Directory.CreateDirectory( Path.Combine( documents, "new-directory" ) ).FullName;
            var path = Path.Combine( nested, "native-note.txt" );
            await File.WriteAllTextAsync( path, "nativewatchcreatedmarker" );
            long requestId = 2;
            async Task<bool> ContainsAsync( string query, string expectedPath )
            {
                await client.SendAsync( new ProtocolRequest { RequestId = ++requestId, Operation = ProtocolOperations.Search, Query = query }, default );
                var messages = await CollectUntilDoneAsync( client, requestId );
                True( messages.All( static message => message.Type != ProtocolMessageTypes.Error ) );
                var expectedId = StableId.Create( ResultKinds.File, expectedPath );
                return messages.Any( message => message.Result?.Id == expectedId );
            }

            True( await WaitUntilAsync( ( ) => ContainsAsync( "nativewatchcreatedmarker", path ), TimeSpan.FromSeconds( 5 ) ) );
            await File.WriteAllTextAsync( path, "nativewatcheditedmarker" );
            True( await WaitUntilAsync( ( ) => ContainsAsync( "nativewatcheditedmarker", path ), TimeSpan.FromSeconds( 5 ) ) );
            var renamed = Path.Combine( nested, "renamed-note.txt" );
            File.Move( path, renamed );
            True( await WaitUntilAsync( ( ) => ContainsAsync( "nativewatcheditedmarker", renamed ), TimeSpan.FromSeconds( 5 ) ) );
            True( !await ContainsAsync( "nativewatcheditedmarker", path ) );
            var hidden = Path.Combine( excluded, "hidden.txt" );
            await File.WriteAllTextAsync( hidden, "nativewatchexcludedmarker" );
            File.Delete( renamed );
            True( await WaitUntilAsync( async ( ) => !await ContainsAsync( "nativewatcheditedmarker", renamed ), TimeSpan.FromSeconds( 5 ) ) );
            True( !await ContainsAsync( "nativewatchexcludedmarker", hidden ) );

            using var signal = Process.Start( new ProcessStartInfo( "/bin/kill" )
            {
                UseShellExecute = false,
                ArgumentList = { "-TERM", daemon.Id.ToString( CultureInfo.InvariantCulture ) }
            } );
            if ( signal is null )
            {
                throw new InvalidOperationException( "Native daemon shutdown signal could not start." );
            }

            await signal.WaitForExitAsync( ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( 0, signal.ExitCode );
            await daemon.WaitForExitAsync( ).WaitAsync( TimeSpan.FromSeconds( 10 ) );
            Equal( 0, daemon.ExitCode );
            await Task.WhenAll( output, errors );
            Console.WriteLine( "PASS Native AOT index watcher create/edit/rename/delete, exclusion and graceful shutdown" );
            return 0;
        }
        finally
        {
            if ( daemon is not null )
            {
                if ( !daemon.HasExited )
                {
                    daemon.Kill( entireProcessTree: true );
                    await daemon.WaitForExitAsync( );
                }
                daemon.Dispose( );
            }
            DeleteIncrementalSpecRoot( root );
        }
    }
}
