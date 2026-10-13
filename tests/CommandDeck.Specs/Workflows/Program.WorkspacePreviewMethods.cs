using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task WorkspacePreviewProviderIdentityAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-workspace-spec-" ).FullName;
        var previousPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var executable = Path.Combine( root, "hyprctl" );
            await File.WriteAllTextAsync( executable, """
                #!/bin/sh
                case "$2" in
                  clients) printf '[]';;
                  workspaces) printf '%s' '[
                    {"id":10,"name":"10","monitor":"DP-1","windows":0},
                    {"id":1,"name":"1","monitor":"DP-1","windows":2},
                    {"id":-1337,"name":"named","monitor":"DP-1","windows":1},
                    {"id":-99,"name":"special:scratch","monitor":"DP-1","windows":1},
                    {"id":"bad","name":"malformed","monitor":"DP-1"},
                    {"id":2,"name":"2","monitor":"DP-1","windows":0},
                    {"id":-98,"name":"conflicting","monitor":"DP-1"},
                    {"id":"003","name":"3","monitor":"DP-1","windows":1},
                    {"id":20,"name":"missing"}
                  ]';;
                esac
                """ );
            File.SetUnixFileMode( executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( "PATH", root );

            var results = await new HyprlandProvider( ).SearchAsync(
                QueryParser.Parse( "ws:" ), new SearchRequest { Limit = 50 }, default );
            Equal( 9, results.Count );
            Equal(
                "workspace:1|workspace:003|workspace:-1337|workspace:-99|workspace:2|workspace:10|workspace:-98|workspace:bad|workspace:20",
                string.Join( '|', results.Select( static value => value.Id ) ) );
            Equal( "ordinary", results.Single( value => value.Id == "workspace:1" ).Metadata!["workspaceKind"] );
            var named = results.Single( value => value.Id == "workspace:-1337" );
            Equal( "ordinary", named.Metadata!["workspaceKind"] );
            Equal( "named", named.Actions.Single( ).Arguments!["argument"] );
            Equal( "switch-workspace", named.Actions.Single( ).Arguments!["operation"] );
            Equal( "special", results.Single( value => value.Id == "workspace:-99" ).Metadata!["workspaceKind"] );
            Equal( "3", results.Single( value => value.Id == "workspace:003" ).Metadata!["workspaceId"] );
            foreach ( var id in new[] { "bad", "20", "-98" } )
            {
                Equal( 0, results.Single( value => value.Id == $"workspace:{id}" ).Metadata!.Count );
            }

            var json = JsonSerializer.Serialize( named, CommandDeckJsonContext.Default.SearchResult );
            var restored = JsonSerializer.Deserialize( json, CommandDeckJsonContext.Default.SearchResult );
            if ( restored is null )
            {
                throw new InvalidOperationException( "Workspace result did not roundtrip." );
            }
            Equal( "-1337", restored!.Metadata!["workspaceId"] );
            var filtered = await new HyprlandProvider( ).SearchAsync(
                QueryParser.Parse( "ws: named" ), new SearchRequest { Limit = 1 }, default );
            Equal( named.Id, filtered.Single( ).Id );

            SearchCoordinator numericCoordinator = new(
                [new HyprlandProvider( )],
                new SearchOptions( ) );
            var numeric = await numericCoordinator.SearchAsync(
                new SearchRequest { Query = "ws:", Limit = 50 },
                default );
            Equal(
                "workspace:1|workspace:003|workspace:2|workspace:10",
                string.Join( '|', numeric.Where( static value =>
                    value.Id is "workspace:1" or "workspace:2" or "workspace:003" or "workspace:10" )
                    .Select( static value => value.Id ) ) );

            PromotionHistory history = new( );
            history.Add( "workspace:10", count: 3 );
            SearchCoordinator usageCoordinator = new(
                [new HyprlandProvider( )],
                new SearchOptions( ),
                usageHistory: history,
                timeProvider: new FixedTimeProvider( PromotionNow ) );
            var ranked = await usageCoordinator.SearchAsync(
                new SearchRequest { Query = "ws:", Limit = 50 },
                default );
            Equal( "workspace:10", ranked[0].Id );
            Equal(
                "workspace:1|workspace:003|workspace:2",
                string.Join( '|', ranked.Where( static value =>
                    value.Id is "workspace:1" or "workspace:2" or "workspace:003" ).Select( static value => value.Id ) ) );

            await File.WriteAllTextAsync( executable, """
                #!/bin/sh
                case "$2" in
                  clients) printf '%s' '[{"address":"0xabc","title":"Fixture window","class":"fixture","workspace":{"id":1,"name":"1"}},
                    {"address":"0xdef","title":"","class":"hyprland-dialog","workspace":{"id":1,"name":"1"}}]';;
                  workspaces) printf '[]';;
                esac
                """ );
            var windows = await new HyprlandProvider( ).SearchAsync(
                QueryParser.Parse( "win:" ), new SearchRequest { Limit = 50 }, default );
            var window = windows.Single( value => value.Id == "window:0xabc" );
            Equal( "focus-window", window.Actions[0].Arguments["operation"] );
            Equal( "address:0xabc", window.Actions[0].Arguments["argument"] );
            Equal( "close-window", window.Actions[1].Arguments["operation"] );
            True( window.Actions[1].RequiresConfirmation );

            var untitled = windows.Single( value => value.Id == "window:0xdef" );
            Equal( "(untitled window)", untitled.Title );
            Equal( "hyprland-dialog · workspace 1", untitled.Subtitle );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", previousPath );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task WorkspacePreviewPreferencesAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-preview-preferences-" ).FullName;
        var options = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) };
        try
        {
            await using ( SqliteCatalog catalog = new( options ) )
            {
                Equal( "snapshots", (await catalog.GetPreferencesAsync( default )).WorkspacePreviewMode );
                var legacy = JsonSerializer.Deserialize( "{}", CommandDeckJsonContext.Default.LauncherPreferences );
                Equal( "snapshots", legacy!.WorkspacePreviewMode );

                foreach ( var mode in new[] { "off", "layout", "snapshots", "invalid" } )
                {
                    var preferences = await catalog.GetPreferencesAsync( default );
                    var json = JsonSerializer.SerializeToNode( preferences, CommandDeckJsonContext.Default.LauncherPreferences )!;
                    json["workspacePreviewMode"] = mode;
                    var updated = json.Deserialize( CommandDeckJsonContext.Default.LauncherPreferences )!;
                    var saved = await catalog.ReplacePreferencesAsync( updated, default );
                    Equal( WorkspacePreviewModes.Normalize( mode ), saved.WorkspacePreviewMode );
                }

                var current = await catalog.GetPreferencesAsync( default );
                await catalog.ReplacePreferencesAsync( new LauncherPreferences
                {
                    Sources = current.Sources,
                    WorkspacePreviewMode = "snapshots"
                }, default );
            }

            await using ( SqliteCatalog catalog = new( options ) )
            {
                var saved = await catalog.GetPreferencesAsync( default );
                Equal( "snapshots", saved.WorkspacePreviewMode );
                Equal( "snapshots", (await catalog.ExportAsync( default )).Preferences.WorkspacePreviewMode );
                var patched = await catalog.PatchSourcePreferencesAsync( new SourcePreferencePatch
                {
                    RegistryRevision = saved.RegistryRevision,
                    PreferenceRevision = saved.PreferenceRevision,
                    Changes = [new( ) { SourceId = SourceIds.WindowsWorkspaces, EnabledOnHome = false }]
                }, default );
                Equal( "snapshots", patched.WorkspacePreviewMode );
                saved = patched;
                var json = JsonSerializer.SerializeToNode( saved, CommandDeckJsonContext.Default.LauncherPreferences )!;
                json["reduceMotion"] = true;
                var updated = json.Deserialize( CommandDeckJsonContext.Default.LauncherPreferences )!;
                Equal( "snapshots", (await catalog.ReplacePreferencesAsync( updated, default )).WorkspacePreviewMode );
                await ThrowsAsync<InvalidOperationException>( ( ) => catalog.ReplacePreferencesAsync(
                    new LauncherPreferences { Sources = [], WorkspacePreviewMode = "off" }, default ).AsTask( ) );
                Equal( "snapshots", (await catalog.GetPreferencesAsync( default )).WorkspacePreviewMode );
                Equal( "snapshots", (await catalog.ResetAsync( default )).WorkspacePreviewMode );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
