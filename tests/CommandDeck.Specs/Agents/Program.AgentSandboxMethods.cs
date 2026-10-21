namespace CommandDeck.Specs;

public static partial class Program
{
    private static void SandboxBindsTheCodeModeHostWhenPresent( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-codemode-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( Path.Combine( root, "bin" ) );
        try
        {
            var codex = Path.Combine( root, "bin", "codex" );
            var host = Path.Combine( root, "bin", "codex-code-mode-host" );
            File.WriteAllText( codex, "#!/bin/sh\n" );
            File.WriteAllText( host, "#!/bin/sh\n" );
            CodexLaunchRequest request = new( )
            {
                BinaryPath = codex,
                RuntimeHome = "/run/user/1000/commanddeck/codex/gen",
                Generation = "gen",
                TrustLevel = AgentTrustLevels.ApprovedContext,
                WorkingDirectory = "/run/user/1000/commanddeck/codex/gen"
            };

            // The bundled code-mode host is bound next to codex so Code Mode can
            // spawn it; its tool calls stay mediated (item/tool/call).
            var joined = string.Join( ' ', BubblewrapCodexLauncher.BuildArguments( request ) );
            True( joined.Contains( $"--ro-bind {host} /opt/commanddeck/codex-code-mode-host", StringComparison.Ordinal ),
                "the bundled code-mode host is bound into the sandbox next to codex" );

            // Absent host binary: no bind, and the launch still builds (graceful).
            File.Delete( host );
            var withoutHost = string.Join( ' ', BubblewrapCodexLauncher.BuildArguments( request ) );
            True( !withoutHost.Contains( "codex-code-mode-host", StringComparison.Ordinal ),
                "no host bind is emitted when the binary is absent" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
