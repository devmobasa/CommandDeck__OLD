namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FileOpenUsesManagedLaunchAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }
        var root = Directory.CreateTempSubdirectory( "commanddeck-open-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var capture = Path.Combine( root, "launch-argv" );
            var launcher = Path.Combine( root, "uwsm-app" );
            var opener = Path.Combine( root, "xdg-open" );
            await File.WriteAllTextAsync( launcher, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{capture}'\n" );
            await File.WriteAllTextAsync( opener, "#!/bin/sh\nexit 97\n" );
            File.SetUnixFileMode( launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            File.SetUnixFileMode( opener, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( "PATH", root );
            var result = new SearchResult
            {
                Id = "file:test",
                Title = "Test",
                Kind = ResultKinds.File,
                ProviderId = "files",
                SourceId = SourceIds.Files
            };
            var path = Path.Combine( root, "image with spaces.png" );
            foreach ( var kind in new[] { ActionKinds.OpenPath, ActionKinds.RevealPath, ActionKinds.OpenUrl } )
            {
                var outcome = await new LinuxActionExecutor( ).ExecuteAsync( result, new CommandAction
                {
                    Id = "open",
                    Title = "Open",
                    Kind = kind,
                    Arguments = new( StringComparer.Ordinal ) { ["path"] = path, ["urlTemplate"] = "https://example.com/{query}" }
                }, "two words", default );
                True( outcome.Succeeded, "opening returns after the managed launcher accepts the request" );
                var argv = await File.ReadAllLinesAsync( capture );
                Equal( "-t|service|-S|both|--|xdg-open", string.Join( '|', argv.Take( 6 ) ) );
                Equal( kind == ActionKinds.OpenUrl ? "https://example.com/two%20words"
                    : kind == ActionKinds.RevealPath ? root : path, argv[6] );
            }
            await File.WriteAllTextAsync( launcher, "#!/bin/sh\nexit 9\n" );
            var failed = await new LinuxActionExecutor( ).ExecuteAsync( result, new CommandAction
            {
                Id = "open",
                Title = "Open",
                Kind = ActionKinds.OpenPath,
                Arguments = new( StringComparer.Ordinal ) { ["path"] = path }
            }, "", default );
            True( !failed.Succeeded, "a rejected managed launch remains an action failure" );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( root, recursive: true );
        }
    }
}
