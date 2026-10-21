namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardDesktopExecExpandsStrictly( )
    {
        const string url = "https://example.invalid/a?b=1";
        Equal( ClipboardLaunchRejection.None, Expand( "browser %U", url, out var launch ) );
        Equal( "browser", launch!.Executable );
        True( launch.Arguments.SequenceEqual( [url] ), "list placeholder receives exactly one literal target" );
        Equal( "url", launch.TargetMode );
        Equal( ClipboardLaunchRejection.None, Expand( "\"quoted app\" --new-window %u", url, out launch ) );
        Equal( "quoted app", launch!.Executable );
        True( launch.Arguments.SequenceEqual( ["--new-window", url] ) );
        Equal( ClipboardLaunchRejection.TargetKindUnsupported, Expand( "browser %f", url, out _ ) );
        Equal( ClipboardLaunchRejection.NoTargetPlaceholder, Expand( "browser --flag", url, out _ ) );
        foreach ( var exec in new[] { "browser %u %U", "browser \"%u\"", "browser %x %u", "browser a|b %u", "browser=x %u", "sub/dir %u",
            "browser %u\\", "browser \"unterminated %u", "browser $HOME %u", "browser %U-suffix", "browser \"a\"b %u", "" } )
        {
            Equal( ClipboardLaunchRejection.InvalidDesktopExec, Expand( exec, url, out _ ) );
        }

        Equal( ClipboardLaunchRejection.None, Expand( "browser %i %u", url, out launch, icon: "app-icon" ) );
        True( launch!.Arguments.SequenceEqual( ["--icon", "app-icon", url] ) );
        Equal( ClipboardLaunchRejection.None, Expand( "browser %i %u", url, out launch ) );
        True( launch!.Arguments.SequenceEqual( [url] ), "standalone %i without Icon produces nothing" );
        Equal( ClipboardLaunchRejection.None, Expand( "browser --name=%c %d --pct=100%% \"a b\\\\\"c\" %u", url, out launch ) );
        True( launch!.Arguments.SequenceEqual( ["--name=My App", "--pct=100%", "a b\"c", url] ), string.Join( "|", launch.Arguments ) );
        Equal( ClipboardLaunchRejection.None, Expand( "browser --desktop=%k %u", url, out launch ) );
        Equal( "--desktop=/apps/browser.desktop", launch!.Arguments[0] );

        var path = "/tmp/ä b#1?.txt";
        Equal( ClipboardLaunchRejection.None, ClipboardDesktopExec.TryExpand( "viewer %f", "Viewer", null, "/apps/viewer.desktop",
            ClipboardLaunchTarget.LocalPath, path, out launch ) );
        Equal( path, launch!.Arguments.Single( ) );
        Equal( "path", launch.TargetMode );
        Equal( ClipboardLaunchRejection.None, ClipboardDesktopExec.TryExpand( "viewer %U", "Viewer", null, "/apps/viewer.desktop",
            ClipboardLaunchTarget.LocalPath, path, out launch ) );
        Equal( "file:///tmp/%C3%A4%20b%231%3F.txt", launch!.Arguments.Single( ) );
        Equal( "file-uri", launch.TargetMode );
        Equal( ClipboardLaunchRejection.DescriptorLimit, Expand( "browser " + new string( 'x', 17 * 1024 ) + " %u", url, out _ ) );

        static ClipboardLaunchRejection Expand( string exec, string target, out ClipboardDesktopLaunch? launch, string? icon = null ) =>
            ClipboardDesktopExec.TryExpand( exec, "My App", icon, "/apps/browser.desktop", ClipboardLaunchTarget.HttpUrl, target, out launch );
    }

    private static void ClipboardOpenHandlersResolveExactAssociations( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-xdg-" ).FullName;
        try
        {
            var bin = Directory.CreateDirectory( Path.Combine( root, "bin" ) ).FullName;
            foreach ( var name in new[] { "browser", "viewer" } )
            {
                var file = Path.Combine( bin, name );
                File.WriteAllText( file, "#!/bin/sh\nexit 0\n" );
                File.SetUnixFileMode( file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            }

            var config = Directory.CreateDirectory( Path.Combine( root, "config" ) ).FullName;
            var data = Directory.CreateDirectory( Path.Combine( root, "data", "applications" ) ).FullName;
            var lower = Directory.CreateDirectory( Path.Combine( root, "sys1", "applications" ) ).FullName;
            Directory.CreateDirectory( Path.Combine( lower, "foo" ) );
            const string key = "x-scheme-handler/https";
            WriteEntry( data, "browser.desktop", "Name=Browser\nName[de]=Browser DE\nExec=browser %U\nMimeType=" + key + ";\nIcon=web" );
            WriteEntry( data, "hidden.desktop", "Name=Hidden\nExec=browser %u\nHidden=true\nMimeType=" + key + ";" );
            WriteEntry( lower, "hidden.desktop", "Name=Lower hidden\nExec=browser %u\nMimeType=" + key + ";" );
            WriteEntry( data, "term.desktop", "Name=Terminal viewer\nExec=viewer %u\nTerminal=true\nMimeType=" + key + ";" );
            WriteEntry( data, "dbus.desktop", "Name=Bus app\nDBusActivatable=true\nMimeType=" + key + ";" );
            WriteEntry( data, "noexec.desktop", "Name=No exec\nExec=missing %u\nMimeType=" + key + ";" );
            WriteEntry( data, "tryexec.desktop", "Name=Try\nExec=browser %u\nTryExec=missing\nMimeType=" + key + ";" );
            WriteEntry( data, "nodisplay.desktop", "Name=Quiet\nExec=browser %u\nNoDisplay=true\nMimeType=" + key + ";" );
            WriteEntry( lower, "foo-bar.desktop", "Name=Flat\nExec=browser %u\nMimeType=" + key + ";" );
            WriteEntry( Path.Combine( lower, "foo" ), "bar.desktop", "Name=Nested\nExec=browser %u\nMimeType=" + key + ";" );
            WriteEntry( lower, "lower.desktop", "Name=Lower\nExec=browser %u\nPath=/\nMimeType=text/plain;" );
            File.WriteAllText( Path.Combine( config, "mimeapps.list" ), "[Default Applications]\n" + key + "=missing.desktop;term.desktop;\n" +
                "[Added Associations]\n" + key + "=lower.desktop;browser.desktop;\n[Removed Associations]\n" + key + "=noexec.desktop;\n" );
            File.WriteAllText( Path.Combine( config, "hyprland-mimeapps.list" ), "[Default Applications]\n" + key + "=browser.desktop;\n" );

            ClipboardResolverEnvironment environment = new( Path.Combine( root, "home" ), config, "/nonexistent/xdg", Path.Combine( root, "data" ),
                Path.Combine( root, "sys1" ) + ":" + Path.Combine( root, "sys2" ), "Hyprland:GNOME", bin, "de_DE.UTF-8" );
            var resolution = new ClipboardAssociationResolver( environment ).Resolve( key, ClipboardLaunchTarget.HttpUrl, "https://example.invalid/" );
            Equal( "Resolved", resolution.Status );
            Equal( "browser.desktop", resolution.DefaultDesktopId );
            True( resolution.Handlers.Select( static handler => handler.DesktopId )
                .SequenceEqual( ["browser.desktop", "lower.desktop", "dbus.desktop", "nodisplay.desktop", "term.desktop"] ),
                string.Join( ",", resolution.Handlers.Select( static handler => handler.DesktopId ) ) );
            Equal( "Browser DE", resolution.Handlers[0].Label );
            True( resolution.Handlers[0].Available && resolution.Handlers[1].Available && resolution.Handlers[3].Available, "direct launch handlers are available" );
            Equal( "UnsupportedActivation", resolution.Handlers[2].Reason );
            Equal( "UnsupportedTerminalLaunch", resolution.Handlers[4].Reason );
            Equal( 5, resolution.Total );
            True( !resolution.MoreAvailable, "five handlers fit the cap" );
            Equal( resolution.Revision, new ClipboardAssociationResolver( environment ).Resolve( key, ClipboardLaunchTarget.HttpUrl, "https://example.invalid/" ).Revision );

            WriteEntry( data, "override.desktop", "Name=User override\nExec=browser %U\nMimeType=text/plain;" );
            WriteEntry( lower, "override.desktop", "Name=System browser\nExec=browser %U\nMimeType=" + key + ";" );
            File.WriteAllText( Path.Combine( lower, "mimeapps.list" ), "[Added Associations]\n" + key + "=override.desktop;\n" );
            var shadowed = new ClipboardAssociationResolver( environment ).Resolve( key, ClipboardLaunchTarget.HttpUrl, "https://example.invalid/" );
            True( shadowed.Handlers.All( static handler => handler.DesktopId != "override.desktop" ),
                "a higher-priority desktop file shadows lower associations for the same desktop ID" );

            const string inheritedDefaultKey = "x-scheme-handler/ftp";
            WriteEntry( data, "override.desktop", "Name=User override\nExec=browser %U\nMimeType=" + inheritedDefaultKey + ";" );
            File.WriteAllText( Path.Combine( lower, "mimeapps.list" ),
                "[Default Applications]\n" + inheritedDefaultKey + "=override.desktop;\n" );
            var inheritedDefault = new ClipboardAssociationResolver( environment ).Resolve( inheritedDefaultKey,
                ClipboardLaunchTarget.HttpUrl, "ftp://example.invalid/" );
            Equal( "override.desktop", inheritedDefault.DefaultDesktopId );
            Equal( "default", inheritedDefault.Handlers.Single( ).Rank );

            WriteEntry( data, "override.desktop", "Name=User override\nExec=browser %U\nMimeType=text/plain;" );
            File.WriteAllText( Path.Combine( lower, "mimeapps.list" ), "[Added Associations]\n" + key + "=override.desktop;\n" );

            WriteEntry( data, "browser.desktop", "Name=Browser\nExec=browser --safe %U\nMimeType=" + key + ";" );
            var changed = new ClipboardAssociationResolver( environment ).Resolve( key, ClipboardLaunchTarget.HttpUrl, "https://example.invalid/" );
            True( changed.Revision != resolution.Revision, "revision follows consulted desktop file content" );
            Equal( "Browser", changed.Handlers[0].Label );

            for ( var index = 0; index < 13; index++ )
            {
                WriteEntry( data, $"many{index:D2}.desktop", "Name=Many\nExec=browser %u\nMimeType=" + key + ";" );
            }

            var capped = new ClipboardAssociationResolver( environment ).Resolve( key, ClipboardLaunchTarget.HttpUrl, "https://example.invalid/" );
            Equal( 12, capped.Handlers.Count );
            Equal( 18, capped.Total );
            True( capped.MoreAvailable && capped.Handlers[0].DesktopId == "browser.desktop", "the default survives the cap" );

            File.WriteAllText( Path.Combine( config, "mimeapps.list" ), "[Added Associations]\n" + key + "=lower.desktop;\n[Removed Associations]\n" + key + "=lower.desktop;\n" );
            Equal( "InvalidAssociations", new ClipboardAssociationResolver( environment ).Resolve( key, ClipboardLaunchTarget.HttpUrl, "https://example.invalid/" ).Status );
            File.WriteAllText( Path.Combine( config, "mimeapps.list" ), new string( '#', 1024 * 1024 + 1 ) );
            Equal( "HandlerDiscoveryUnavailable", new ClipboardAssociationResolver( environment ).Resolve( key, ClipboardLaunchTarget.HttpUrl, "https://example.invalid/" ).Status );
            File.Delete( Path.Combine( config, "mimeapps.list" ) );
            Equal( "NoAssociations", new ClipboardAssociationResolver( environment ).Resolve( "x-scheme-handler/gopher", ClipboardLaunchTarget.HttpUrl, "https://example.invalid/" ).Status );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }

        static void WriteEntry( string directory, string name, string body ) =>
            File.WriteAllText( Path.Combine( directory, name ), "[Desktop Entry]\nType=Application\n" + body + "\n" );
    }
}
