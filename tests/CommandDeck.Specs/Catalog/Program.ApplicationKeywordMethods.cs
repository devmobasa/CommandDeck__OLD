using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void DesktopExecProgramNamesSeeThroughLaunchers( )
    {
        foreach ( (var exec, var expected) in new (string, string?)[]
        {
            ("gnome-sound-recorder", "gnome-sound-recorder"),
            ("/usr/bin/gnome-sound-recorder %U", "gnome-sound-recorder"),
            ("env GDK_BACKEND=x11 /opt/foo/bin/foo-app %F", "foo-app"),
            ("env -u WAYLAND_DISPLAY --chdir=/tmp -i /opt/recorder/bin/recorder-cli", "recorder-cli"),
            ("env -S \"fixture --flag\"", null),
            ("xdg-terminal-exec --app-id TUI.float -e fixture", null),
            ("bash -l /opt/tool/run-tool.sh", "run-tool.sh"),
            ("/usr/bin/flatpak run --branch=stable --arch=x86_64 --command=obsidian --file-forwarding md.obsidian.Obsidian @@u %U @@", "obsidian"),
            ("flatpak run org.gnome.Maps", null),
            ("sh -c \"exec dua i /\"", "dua"),
            ("bash -lc \"notify-send hello\"", "notify-send"),
            ("xdg-terminal-exec --app-id=TUI.float -e bash -c \"dua i /\"", "dua"),
            ("sh -c \"fixture | other\"", null),
            ("sh -c 'fixture $1'", null),
            ("bash /opt/tool/run-tool.sh", "run-tool.sh"),
            ("env", null),
            ("\"unterminated", null),
            ("", null)
        } )
        {
            Equal( expected ?? "(none)", DesktopEntryCommand.ProgramName( exec ) ?? "(none)" );
        }
    }

    private static async Task ApplicationsAreFoundByCommandNameAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-app-keywords-{Guid.NewGuid( ):N}" );
        var applications = Directory.CreateDirectory( Path.Combine( root, "system", "applications" ) ).FullName;
        Directory.CreateDirectory( Path.Combine( root, "user", "applications" ) );
        var oldDataHome = Environment.GetEnvironmentVariable( "XDG_DATA_HOME" );
        var oldDataDirectories = Environment.GetEnvironmentVariable( "XDG_DATA_DIRS" );
        try
        {
            foreach ( (var id, var name, var exec) in new[]
            {
                ("org.gnome.SoundRecorder", "Sound Recorder", "gnome-sound-recorder"),
                ("md.obsidian.Obsidian", "Obsidian", "/usr/bin/flatpak run --branch=stable --arch=x86_64 --command=obsidian md.obsidian.Obsidian @@u %U @@"),
                ("org.gnome.Maps", "Maps", "/usr/bin/flatpak run --branch=stable --arch=x86_64 org.gnome.Maps"),
                ("legacy-tool", "Legacy Tool", "env GDK_BACKEND=x11 /opt/legacy/bin/legacy-tool %F"),
                ("com.example.Notes", "Audio Notes", "env -u WAYLAND_DISPLAY /opt/recorder/bin/recorder-cli"),
                ("disk-usage", "Disk Usage", "xdg-terminal-exec -e bash -c \"dua i /\"")
            } )
            {
                await File.WriteAllTextAsync( Path.Combine( applications, id + ".desktop" ), DesktopEntryText( name, exec ) );
            }

            Environment.SetEnvironmentVariable( "XDG_DATA_HOME", Path.Combine( root, "user" ) );
            Environment.SetEnvironmentVariable( "XDG_DATA_DIRS", Path.Combine( root, "system" ) );
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( DesktopEntryScanner.ScanAsync( default ), null, default );
            SearchCoordinator search = new( [new CatalogSearchProvider( catalog ), new ApplicationSearchProvider( catalog )],
                new SearchOptions( ) );

            async Task<IReadOnlyList<SearchResult>> ApplicationsAsync( string query ) =>
                [.. (await search.SearchAsync( new SearchRequest { Query = query, Limit = 20 }, default ))
                    .Where( static result => result.Kind == ResultKinds.Application )];

            foreach ( (var query, var title) in new[]
            {
                ("gnome-sound-recorder", "Sound Recorder"), ("org.gnome.SoundRecorder", "Sound Recorder"),
                ("obsidian", "Obsidian"), ("org.gnome.Maps", "Maps"), ("legacy-tool", "Legacy Tool"), ("dua", "Disk Usage"),
                ("recorder-cli", "Audio Notes")
            } )
            {
                var found = await ApplicationsAsync( query );
                True( found.Count > 0 && found[0].Title == title, $"{query} finds {title}" );
            }

            // Wrapper words and arguments are not keywords, so they match no application.
            foreach ( var query in new[] { "flatpak", "env", "branch", "stable", "x86_64", "GDK_BACKEND", "WAYLAND_DISPLAY", "xdg-terminal-exec" } )
            {
                True( (await ApplicationsAsync( query )).Count == 0, query );
            }

            // The keyword is searched, not shown.
            var recorder = (await ApplicationsAsync( "gnome-sound-recorder" ))[0];
            True( recorder.Subtitle is null );
            await catalog.DisposeAsync( );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "XDG_DATA_HOME", oldDataHome );
            Environment.SetEnvironmentVariable( "XDG_DATA_DIRS", oldDataDirectories );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ReplacingApplicationsKeepsOtherRowsAndRefreshesOpenWithAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-app-replace-" ).FullName;
        try
        {
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                mimeTypeResolver: new RecordingMimeTypeResolver( "text/plain" ) );
            await catalog.InitializeAsync( default );
            static IndexedItem Handler( string id, string title ) => new( )
            {
                StableId = $"app:{id}",
                Kind = ResultKinds.Application,
                Title = title,
                Path = $"/usr/share/applications/{id}",
                PayloadJson = PayloadWriter.Create( ("desktopId", id), ("exec", "handler %f"), ("mimeTypes", "text/plain") ),
                ProviderId = ProviderIds.Applications,
                SourceId = SourceIds.Applications
            };
            IndexedItem note = new( )
            {
                StableId = "file:notes",
                Kind = ResultKinds.File,
                Title = "notes.txt",
                Path = Path.Combine( root, "notes.txt" ),
                ProviderId = ProviderIds.Files,
                SourceId = SourceIds.Files
            };
            await catalog.ReplaceAllAsync( new[] { Handler( "viewer.desktop", "Fixture Viewer" ), note }.ToAsyncEnumerable( ), null, default );

            async Task<string> OpenWithAsync( ) => string.Join( ',',
                (await catalog.GetOpenWithActionsAsync( [note.Path!], 12, default ))[note.Path!].Select( static action => action.Id ) );
            Equal( "open-with:viewer.desktop", await OpenWithAsync( ) );

            await catalog.ReplaceApplicationsAsync( [Handler( "editor.desktop", "Fixture Editor" )], default );
            Equal( "open-with:editor.desktop", await OpenWithAsync( ) );
            Equal( "app:editor.desktop", (await catalog.SearchAsync( QueryParser.Parse( "Fixture Editor" ), 10, default )).Single( ).Id );
            Equal( 0, (await catalog.SearchAsync( QueryParser.Parse( "Fixture Viewer" ), 10, default )).Count );
            Equal( "file:notes", (await catalog.SearchAsync( QueryParser.Parse( "notes" ), 10, default )).Single( ).Id );
            True( (await catalog.CheckIntegrityAsync( default )).Succeeded, "search rows stay linked to their items" );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await catalog.ReplaceApplicationsAsync( [note], default ) );
            await catalog.DisposeAsync( );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }
}
