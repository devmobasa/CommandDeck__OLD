using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task<string> CreateAgentCliConfigAsync( string root, string? codexBinaryPath = null )
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
                CodexBinaryPath = codexBinaryPath,
                RuntimeDirectory = Path.Combine( root, "runtime" ),
                StateDirectory = Path.Combine( root, "state" )
            }
        };
        await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
        return config;
    }

    private static async Task AgentCliReportsStatusAndRefusesUnconfirmedAuthorityAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = CreateCliSpecRoot( );
        try
        {
            // A fake binary keeps the spec independent of any Codex on PATH. The
            // CLI never starts Codex, so status must still resolve and report it.
            var version = CodexAppServerContract.ReviewedVersion;
            var codex = Path.Combine( root, "codex" );
            await File.WriteAllTextAsync( codex, $"#!/bin/sh\necho 'codex-cli {version}'\n" );
            File.SetUnixFileMode( codex, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            var config = await CreateAgentCliConfigAsync( root, codex );
            var status = await RunCommandDeckProcessAsync( "agent", "status", "--config", config );
            Equal( 0, status.ExitCode );
            True( status.StandardOutput.Contains( "Codex", StringComparison.Ordinal ) );
            True( status.StandardOutput.Contains( $"Binary:         {codex} {version}\n", StringComparison.Ordinal ) );
            var statusJson = await RunCommandDeckProcessAsync( "agent", "status", "--json", "--config", config );
            Equal( 0, statusJson.ExitCode );
            var parsed = JsonSerializer.Deserialize( statusJson.StandardOutput, CommandDeckJsonContext.Default.AgentStatus );
            True( parsed is { Enabled: false } && parsed.Trust.Level == AgentTrustLevels.Default && !parsed.Helper.Installed );
            True( parsed is { BinaryReviewed: true } && parsed.BinaryPath == codex && parsed.BinaryVersion == version );

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

    // `agent status` resolves Codex in its own process, so a missing binary
    // names the fix instead of a bare "not resolved".
    private static async Task AgentCliStatusNamesTheFixWhenCodexIsMissingAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var emptyPath = Directory.CreateDirectory( Path.Combine( root, "empty-path" ) ).FullName;
            var missing = await RunCommandDeckWithEnvironmentAsync(
                new Dictionary<string, string?> { ["PATH"] = emptyPath },
                "agent", "status", "--config", await CreateAgentCliConfigAsync( root ) );
            Equal( 0, missing.ExitCode );
            True( missing.StandardOutput.Contains(
                "Binary:         not resolved (Codex was not found on PATH. Install it or set agent.codexBinaryPath.)",
                StringComparison.Ordinal ) );
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
