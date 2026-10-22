namespace CommandDeck.Specs;

public static partial class Program
{
    // Codex 0.157 and later links the requested socket path to a socket under
    // /tmp. A daemon unit with PrivateTmp sees only a dangling link there.
    private static async Task UnrestrictedLaunchNamesTheFixForAnUnreachableSocketLinkAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.PathFor( $"commanddeck-codex-socket-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            var systemdRun = Path.Combine( root, "systemd-run" );
            var hiddenSocket = Path.Combine( root, "private-tmp", "codex-daemon", "app-server" );
            await File.WriteAllTextAsync( systemdRun,
                "#!/bin/sh\n" +
                "while [ $# -gt 0 ]; do\n" +
                $"  if [ \"$1\" = --listen ]; then ln -s '{hiddenSocket}' \"${{2#unix://}}\"; fi\n" +
                "  shift\n" +
                "done\n" );
            File.SetUnixFileMode( systemdRun, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            TransientUnitCodexLauncher launcher = new( systemdRun, "/usr/bin/true" );
            CodexLaunchRequest request = new( )
            {
                BinaryPath = "/usr/bin/codex",
                RuntimeHome = root,
                Generation = "spec",
                TrustLevel = AgentTrustLevels.Unrestricted,
                WorkingDirectory = root,
                SocketPath = Path.Combine( root, "app-server.sock" )
            };

            try
            {
                await using var handle = await launcher.LaunchAsync( request, default );
                True( false, "A socket link the daemon cannot follow must not connect." );
            }
            catch ( AiOperationException exception )
            {
                Equal( AiErrorCategories.Unavailable, exception.Category );
                True( exception.SafeSummary.Contains( "Reinstall CommandDeck", StringComparison.Ordinal ) );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
