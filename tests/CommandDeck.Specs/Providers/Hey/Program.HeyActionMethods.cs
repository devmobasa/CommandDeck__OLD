namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class HeyLaunchFixture : IDisposable
    {
        private readonly string? _originalPath = Environment.GetEnvironmentVariable( "PATH" );

        public HeyLaunchFixture( params string[] tools )
        {
            Root = SpecTemporaryFiles.CreateDirectory( "commanddeck-hey-actions-" ).FullName;
            foreach ( var tool in tools )
            {
                AddTool( tool );
            }

            Environment.SetEnvironmentVariable( "PATH", Root );
        }

        public string Root { get; }

        // Each stand-in records the argv it was launched with, one per line.
        public void AddTool( string name )
        {
            var path = Path.Combine( Root, name );
            File.WriteAllText( path, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{Captured( name )}'\n" );
            if ( OperatingSystem.IsLinux( ) )
            {
                File.SetUnixFileMode( path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            }
        }

        public string Captured( string name ) => Path.Combine( Root, name + ".argv" );

        public string[]? Argv( string name ) =>
            File.Exists( Captured( name ) ) ? File.ReadAllLines( Captured( name ) ) : null;

        // The arguments an Omarchy launcher was started with as a new user service
        // (uwsm-app -t service), or null when it was not; the launcher itself must
        // never run inside the daemon.
        public string[]? Launched( string launcher )
        {
            if ( Argv( launcher ) is not null )
            {
                throw new InvalidOperationException( $"{launcher} ran inside the daemon's sandbox." );
            }

            var argv = Argv( "uwsm-app" );
            string[] prefix = ["-t", "service", "-S", "both", "--", Path.Combine( Root, launcher )];
            return argv is not null && argv.Length >= prefix.Length && argv.Take( prefix.Length ).SequenceEqual( prefix )
                ? argv[prefix.Length..]
                : null;
        }

        public void Dispose( )
        {
            Environment.SetEnvironmentVariable( "PATH", _originalPath );
            Directory.Delete( Root, recursive: true );
        }
    }

    private static CommandAction HeyOpenAction( string topicId, string appUrl ) => new( )
    {
        Id = HeyProvider.OpenActionId,
        Title = "Open in HEY",
        Kind = ActionKinds.HeyOpen,
        Arguments = new( StringComparer.Ordinal )
        {
            ["topicId"] = topicId,
            ["appUrl"] = appUrl
        }
    };

    private static async Task HeyOpenLaunchesWebAppAndRefusesForgedLinksAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        using HeyLaunchFixture fixture = new( HeyActionHandler.HeyWebAppLauncher, "uwsm-app" );
        var row = HeyPrivateRow( );
        LinuxActionExecutor executor = new( );

        var opened = await executor.ExecuteAsync( row, HeyOpenAction( "9001", "https://app.hey.com/topics/9001" ), "", default );
        True( opened.Succeeded );
        Equal( "https://app.hey.com/topics/9001", string.Join( '|', fixture.Launched( HeyActionHandler.HeyWebAppLauncher )! ) );

        foreach ( var (topicId, appUrl) in new[]
        {
            ("9001", "http://app.hey.com/topics/9001"),
            ("9001", "https://app.hey.com/topics/9002"),
            ("9001", "https://evil.example/topics/9001/../../login"),
            ("9001", "https://phish.example/topics/9001"),
            ("-4", "https://app.hey.com/topics/-4"),
            ("9001", "javascript:alert(1)")
        } )
        {
            File.Delete( fixture.Captured( "uwsm-app" ) );
            var forged = await executor.ExecuteAsync( row, HeyOpenAction( topicId, appUrl ), "", default );
            True( !forged.Succeeded );
            Equal( ( string[]? ) null, fixture.Argv( "uwsm-app" ) );
        }

        // Without Omarchy's web-app launcher the link opens in the browser.
        File.Delete( Path.Combine( fixture.Root, HeyActionHandler.HeyWebAppLauncher ) );
        True( (await executor.ExecuteAsync( row, HeyOpenAction( "9001", "https://app.hey.com/topics/9001" ), "", default )).Succeeded );
        Equal( "-t|service|-S|both|--|xdg-open|https://app.hey.com/topics/9001", string.Join( '|', fixture.Argv( "uwsm-app" )! ) );
    }

    private static async Task HeySignInAndInstallUsePresentedTerminalAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        using HeyLaunchFixture fixture = new( HeyActionHandler.PresentedTerminalLauncher, "uwsm-app" );
        var row = HeyPrivateRow( );
        CommandAction signIn = new( )
        {
            Id = HeyProvider.SignInActionId,
            Title = "Sign in",
            Kind = ActionKinds.HeySignIn
        };
        CommandAction install = new( )
        {
            Id = HeyProvider.InstallActionId,
            Title = "Install",
            Kind = ActionKinds.HeyInstall
        };

        LinuxActionExecutor signedIn = new( hey: new HeyClient( resolve: static ( ) => "/opt/hey's/bin/hey" ) );
        True( (await signedIn.ExecuteAsync( row, signIn, "", default )).Succeeded );
        Equal( "'/opt/hey'\\''s/bin/hey' 'auth' 'login'", fixture.Launched( HeyActionHandler.PresentedTerminalLauncher )!.Single( ) );

        LinuxActionExecutor missing = new( hey: new HeyClient( resolve: static ( ) => null ) );
        True( !(await missing.ExecuteAsync( row, signIn, "", default )).Succeeded );

        // Without omarchy-mise-install the install guide opens instead.
        True( (await missing.ExecuteAsync( row, install, "", default )).Succeeded );
        Equal( HeyActionHandler.HeyInstallGuideUrl, fixture.Argv( "uwsm-app" )!.Last( ) );

        fixture.AddTool( HeyActionHandler.MiseInstaller );
        True( (await missing.ExecuteAsync( row, install, "", default )).Succeeded );
        Equal( "'omarchy-mise-install' 'github:basecamp/hey-cli' 'hey'", fixture.Launched( HeyActionHandler.PresentedTerminalLauncher )!.Single( ) );

        // A plain terminal runs the argv itself when Omarchy's launcher is absent.
        File.Delete( Path.Combine( fixture.Root, HeyActionHandler.PresentedTerminalLauncher ) );
        True( (await signedIn.ExecuteAsync( row, signIn, "", default )).Succeeded );
        Equal( "-t|service|-S|both|-T|--|/opt/hey's/bin/hey|auth|login", string.Join( '|', fixture.Argv( "uwsm-app" )! ) );
    }
}
