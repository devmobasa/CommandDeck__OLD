using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task HeyOpenTargetPersistsAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-hey-target-" ).FullName;
        try
        {
            StorageOptions options = new( )
            {
                DatabasePath = Path.Combine( root, "catalog.db" )
            };
            await using ( SqliteCatalog catalog = new( options ) )
            {
                await catalog.InitializeAsync( default );
                var defaults = await catalog.GetPreferencesAsync( default );
                Equal( HeyOpenTargets.Web, defaults.HeyOpenTarget );

                // Settings sends the whole preferences object back with one field changed.
                var json = JsonSerializer.SerializeToNode( defaults, CommandDeckJsonContext.Default.LauncherPreferences )!;
                json["heyOpenTarget"] = HeyOpenTargets.Terminal;
                var saved = await catalog.ReplacePreferencesAsync(
                    json.Deserialize( CommandDeckJsonContext.Default.LauncherPreferences )!, default );
                Equal( HeyOpenTargets.Terminal, saved.HeyOpenTarget );

                json["heyOpenTarget"] = "tab";
                var rejected = await CaptureInvalidOperationAsync( async ( ) => await catalog.ReplacePreferencesAsync(
                    json.Deserialize( CommandDeckJsonContext.Default.LauncherPreferences )!, default ) );
                Equal( "HEY threads open in the web app or the terminal app.", rejected?.Message );
            }

            await using ( SqliteCatalog catalog = new( options ) )
            {
                var reopened = await catalog.GetPreferencesAsync( default );
                Equal( HeyOpenTargets.Terminal, reopened.HeyOpenTarget );
                Equal( HeyOpenTargets.Terminal, (await catalog.ExportAsync( default )).Preferences.HeyOpenTarget );
                True( PreferencesTextFormatter.Format( await catalog.GetSnapshotAsync( false, default ) )
                    .Contains( "HEY threads open in: terminal" ) );

                // A snapshot or patch never loses the choice.
                var patched = await catalog.PatchSourcePreferencesAsync( new SourcePreferencePatch
                {
                    RegistryRevision = reopened.RegistryRevision,
                    PreferenceRevision = reopened.PreferenceRevision,
                    Changes = [new( ) { SourceId = SourceIds.Hey, EnabledInSearch = false }]
                }, default );
                Equal( HeyOpenTargets.Terminal, patched.HeyOpenTarget );
            }

            // A value this build does not know opens threads in the web app.
            await using ( SqliteConnection connection = new( "Data Source=" + options.DatabasePath ) )
            {
                await connection.OpenAsync( );
                await using var update = connection.CreateCommand( );
                update.CommandText = "UPDATE launcher_preferences SET value = 'tab' WHERE key = 'hey_open_target';";
                Equal( 1, await update.ExecuteNonQueryAsync( ) );
            }

            await using ( SqliteCatalog catalog = new( options ) )
            {
                Equal( HeyOpenTargets.Web, (await catalog.GetPreferencesAsync( default )).HeyOpenTarget );
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }

    // The terminal target hands the thread to a running HEY TUI and raises its
    // window, or opens a new TUI on the thread.
    private static async Task HeyTerminalTargetReusesRunningTuiAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-hey-terminal-" ).FullName;
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            var json = JsonSerializer.SerializeToNode( await catalog.GetPreferencesAsync( default ), CommandDeckJsonContext.Default.LauncherPreferences )!;
            json["heyOpenTarget"] = HeyOpenTargets.Terminal;
            await catalog.ReplacePreferencesAsync( json.Deserialize( CommandDeckJsonContext.Default.LauncherPreferences )!, default );

            var running = true;
            HeyFake fake = new( )
            {
                Respond = args => HeyResult( running ? """{"ok":true}""" : """{"ok":false,"error":"no running HEY TUI","code":"unknown"}""", running ? 0 : 1 )
            };
            LinuxActionExecutor executor = new( catalog, hey: fake.Client( ) );
            using HeyLaunchFixture launchers = new( HeyActionHandler.TuiLaunchOrFocus, "uwsm-app" );
            var row = HeyPrivateRow( );
            var open = HeyOpenAction( "9001", "https://app.hey.com/topics/9001" );

            True( (await executor.ExecuteAsync( row, open, "", default )).Succeeded );
            Equal( "tui|--instance|omarchy|--topic|9001|--remote", string.Join( '|', fake.Calls.Single( ).Args ) );
            Equal( "--app-id=org.omarchy.hey|'/opt/hey/bin/hey'|tui|--instance|omarchy",
                string.Join( '|', launchers.Launched( HeyActionHandler.TuiLaunchOrFocus )! ) );

            running = false;
            True( (await executor.ExecuteAsync( row, open, "", default )).Succeeded );
            Equal( "--app-id=org.omarchy.hey|'/opt/hey/bin/hey'|tui|--instance|omarchy|--topic|9001",
                string.Join( '|', launchers.Launched( HeyActionHandler.TuiLaunchOrFocus )! ) );

            // Without Omarchy's launcher a terminal opens the TUI on the thread.
            File.Delete( Path.Combine( launchers.Root, HeyActionHandler.TuiLaunchOrFocus ) );
            True( (await executor.ExecuteAsync( row, open, "", default )).Succeeded );
            Equal( "-t|service|-S|both|-T|--|/opt/hey/bin/hey|tui|--instance|omarchy|--topic|9001",
                string.Join( '|', launchers.Argv( "uwsm-app" )! ) );

            // The web target never runs hey.
            json["heyOpenTarget"] = HeyOpenTargets.Web;
            await catalog.ReplacePreferencesAsync( json.Deserialize( CommandDeckJsonContext.Default.LauncherPreferences )!, default );
            var calls = fake.Calls.Count;
            launchers.AddTool( HeyActionHandler.HeyWebAppLauncher );
            True( (await executor.ExecuteAsync( row, open, "", default )).Succeeded );
            Equal( calls, fake.Calls.Count );
            Equal( "https://app.hey.com/topics/9001", launchers.Launched( HeyActionHandler.HeyWebAppLauncher )!.Single( ) );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }
}
