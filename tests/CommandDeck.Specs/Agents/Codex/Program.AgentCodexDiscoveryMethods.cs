namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task CodexDiscoverySkipsLauncherShimsAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.PathFor( $"commanddeck-codex-shim-{Guid.NewGuid( ):N}" );
        var shimDirectory = Path.Combine( root, "shim" );
        var directDirectory = Path.Combine( root, "direct" );
        Directory.CreateDirectory( shimDirectory );
        Directory.CreateDirectory( directDirectory );
        var previousPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var launcher = Path.Combine( root, "mise" );
            await File.WriteAllTextAsync( launcher, "#!/bin/sh\necho 'mise 2026.9.9'\n" );
            File.SetUnixFileMode( launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            File.CreateSymbolicLink( Path.Combine( shimDirectory, "codex" ), launcher );
            var direct = Path.Combine( directDirectory, "codex" );
            await File.WriteAllTextAsync( direct, "#!/bin/sh\necho 'codex-cli 0.155.1'\n" );
            File.SetUnixFileMode( direct, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( "PATH", shimDirectory + ":" + directDirectory );

            var detected = await CodexBinaryLocator.LocateAsync( new AgentOptions( ), default );
            Equal( direct, detected.CanonicalPath );
            True( CodexBinaryLocator.LauncherWarning( new AgentOptions( ) )?.Contains(
                direct, StringComparison.Ordinal ) == true );

            try
            {
                await CodexBinaryLocator.LocateAsync( new AgentOptions
                {
                    CodexBinaryPath = Path.Combine( shimDirectory, "codex" )
                }, default );
                True( false, "A configured launcher shim must be rejected." );
            }
            catch ( AiOperationException exception )
            {
                True( exception.SafeSummary?.Contains( "launcher shim", StringComparison.Ordinal ) == true );
            }

            File.SetUnixFileMode( direct, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute | UnixFileMode.GroupWrite );
            try
            {
                await CodexBinaryLocator.LocateAsync( new AgentOptions( ), default );
                True( false, "A direct binary with group write permission must be rejected." );
            }
            catch ( AiOperationException exception )
            {
                True( exception.SafeSummary?.Contains( "group or world writable", StringComparison.Ordinal ) == true );
            }

            Environment.SetEnvironmentVariable( "PATH", shimDirectory );
            True( CodexBinaryLocator.LauncherWarning( new AgentOptions( ) )?.Contains(
                "agent.codexBinaryPath", StringComparison.Ordinal ) == true );
            try
            {
                await CodexBinaryLocator.LocateAsync( new AgentOptions( ), default );
                True( false, "A PATH with only a launcher shim must be rejected." );
            }
            catch ( AiOperationException exception )
            {
                True( exception.SafeSummary?.Contains( "agent.codexBinaryPath", StringComparison.Ordinal ) == true );
            }

            Environment.SetEnvironmentVariable( "PATH", root );
            try
            {
                await CodexBinaryLocator.LocateAsync( new AgentOptions( ), default );
                True( false, "A PATH without Codex must be rejected." );
            }
            catch ( AiOperationException exception )
            {
                Equal( "Codex was not found on PATH. Install it or set agent.codexBinaryPath.", exception.SafeSummary );
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", previousPath );
            Directory.Delete( root, recursive: true );
        }
    }
}
