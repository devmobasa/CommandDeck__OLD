using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task<string> CreateAgentCliConfigAsync( string root )
    {
        var config = Path.Combine( root, "config.json" );
        CommandDeckOptions options = new( )
        {
            Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
            Indexing = new IndexingOptions { Roots = [], IndexDesktopApplications = false, IndexOmarchyCommands = false },
            Privacy = new PrivacyOptions { StoreUsageHistory = false, NeverIndex = [] },
            Ai = new AiOptions { Enabled = false, Providers = [] },
            Agent = new AgentOptions
            {
                RuntimeDirectory = Path.Combine( root, "runtime" ),
                StateDirectory = Path.Combine( root, "state" )
            }
        };
        await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
        return config;
    }

    private static async Task AgentCliReportsStatusAndRefusesUnconfirmedAuthorityAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var config = await CreateAgentCliConfigAsync( root );
            var status = await RunCommandDeckProcessAsync( "agent", "status", "--config", config );
            Equal( 0, status.ExitCode );
            True( status.StandardOutput.Contains( "Codex", StringComparison.Ordinal ) );
            var statusJson = await RunCommandDeckProcessAsync( "agent", "status", "--json", "--config", config );
            Equal( 0, statusJson.ExitCode );
            var parsed = JsonSerializer.Deserialize( statusJson.StandardOutput, CommandDeckJsonContext.Default.AgentStatus );
            True( parsed is { Enabled: false } && parsed.Trust.Level == AgentTrustLevels.Default && !parsed.Helper.Installed );

            var probe = await RunCommandDeckProcessAsync( "agent", "probe", "tools", "--config", config );
            Equal( 0, probe.ExitCode );
            True( probe.StandardOutput.Contains( "--unshare-all", StringComparison.Ordinal ) );
            True( probe.StandardOutput.Contains( "--strict-config", StringComparison.Ordinal ) );
            True( !probe.StandardOutput.Contains( "--yolo", StringComparison.OrdinalIgnoreCase ) );
            True( !probe.StandardOutput.Contains( "dangerously-bypass", StringComparison.OrdinalIgnoreCase ) );

            var trust = await RunCommandDeckProcessAsync( "agent", "trust", "set", "6", "--config", config );
            Equal( 2, trust.ExitCode );
            var logout = await RunCommandDeckProcessAsync( "agent", "logout", "--config", config );
            Equal( 2, logout.ExitCode );
            var invalid = await RunCommandDeckProcessAsync(
                "agent", "preferences", "set", "--ttl", "999999", "--config", config );
            Equal( 1, invalid.ExitCode );
            var unavailable = await RunCommandDeckProcessAsync(
                "agent", "ask", "hello", "--socket", SpecTemporaryFiles.SocketPath( root, "missing.sock" ), "--config", config );
            Equal( 1, unavailable.ExitCode );
            True( unavailable.StandardError.Contains( "daemon", StringComparison.OrdinalIgnoreCase ) );
            var doctor = await RunCommandDeckProcessAsync( "doctor", "--config", config );
            True( doctor.StandardOutput.Contains( "Bubblewrap", StringComparison.Ordinal ) );
            True( doctor.StandardOutput.Contains( "Package helper", StringComparison.Ordinal ) );
            True( doctor.StandardOutput.Contains( "Herdr default session", StringComparison.Ordinal ) );
            True( doctor.StandardOutput.Contains( "PATH-selected herdr client", StringComparison.Ordinal ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentCliUsesDaemonStateAcrossInvocationsAsync( )
    {
        ScriptedAgentBackend backend = new( )
        {
            Script = async ( _, host, token ) => await host.OnTextAsync( "daemon-owned answer", token )
        };
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) =
            await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        try
        {
            await using ( runtime )
            {
                await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
                var socket = SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" );
                var ask = await RunCommandDeckProcessAsync(
                    "agent", "ask", "hello", "--socket", socket );
                Equal( 0, ask.ExitCode );
                True( ask.StandardOutput.Contains( "daemon-owned answer", StringComparison.Ordinal ) );

                var conversation = await RunCommandDeckProcessAsync(
                    "agent", "conversation", "--json", "--socket", socket );
                Equal( 0, conversation.ExitCode );
                True( conversation.StandardOutput.Contains( "daemon-owned answer", StringComparison.Ordinal ) );

                var clear = await RunCommandDeckProcessAsync(
                    "agent", "clear", "--socket", socket );
                Equal( 0, clear.ExitCode );
                var cleared = await RunCommandDeckProcessAsync(
                    "agent", "conversation", "--json", "--socket", socket );
                Equal( 0, cleared.ExitCode );
                True( !cleared.StandardOutput.Contains( "daemon-owned answer", StringComparison.Ordinal ) );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }
}
