namespace CommandDeck.Specs;

public static partial class Program
{
    // Codex 0.157 and later places its App Server socket under /tmp, so the
    // daemon unit must share the user's /tmp and may write its own files there.
    private static void DaemonUnitSharesTheUserTemporaryDirectory( )
    {
        var settings = File.ReadAllLines( Path.Combine( FindRepositoryRoot( ), "packaging/systemd/commanddeck.service" ) )
            .Select( static line => line.Trim( ) )
            .Where( static line => line.Length > 0 && !line.StartsWith( '#' ) )
            .ToList( );

        True( !settings.Any( static line => line.StartsWith( "PrivateTmp=", StringComparison.Ordinal ) ),
            "the daemon unit must not hide /tmp" );
        True( settings.Any( static line => line.StartsWith( "ReadWritePaths=", StringComparison.Ordinal ) &&
            line["ReadWritePaths=".Length..].Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Contains( "/tmp" ) ),
            "the daemon unit must keep /tmp writable under ProtectSystem=strict" );
    }

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
