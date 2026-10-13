namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardShortcutPreferencesAsync( )
    {
        Equal( "CTRL+SHIFT+V", ClipboardShortcut.Parse( " shift + ctrl + v " ) );
        Equal( "", ClipboardShortcut.Parse( " " ) );
        foreach ( var invalid in new[] { "+", "Ctrl+V", "Ctrl+Shift+C", "Super+V", "Ctrl+Ctrl+V", "Ctrl+Shift+Escape" } )
        {
            Throws<InvalidOperationException>( ( ) => ClipboardShortcut.Parse( invalid ) );
        }

        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-shortcut-spec-" ).FullName;
        var previousPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var hyprctl = Path.Combine( root, "hyprctl" );
            await File.WriteAllTextAsync( hyprctl, "#!/bin/sh\nprintf '[]'\n" );
            File.SetUnixFileMode( hyprctl, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( "PATH", root );

            var options = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) };
            await using ( SqliteCatalog catalog = new( options ) )
            {
                await catalog.InitializeAsync( default );
                Equal( ClipboardShortcut.Default, (await catalog.GetPreferencesAsync( default )).ClipboardHistoryShortcut );
                var defaults = LauncherPreferences.CreateDefault( );
                foreach ( var response in new[] { "[{\"modmask\":12,\"key\":\"v\"}]", "not-json" } )
                {
                    await File.WriteAllTextAsync( hyprctl, $"#!/bin/sh\nprintf '{response}'\n" );
                    await ThrowsAsync<InvalidOperationException>( ( ) => catalog.ReplacePreferencesAsync( new( )
                    {
                        Sources = defaults.Sources,
                        ClipboardHistoryShortcut = "ctrl+alt+v"
                    }, default ).AsTask( ) );
                    Equal( ClipboardShortcut.Default, (await catalog.GetPreferencesAsync( default )).ClipboardHistoryShortcut );
                }

                File.Delete( hyprctl );
                await ThrowsAsync<InvalidOperationException>( ( ) => catalog.ReplacePreferencesAsync( new( )
                {
                    Sources = defaults.Sources,
                    ClipboardHistoryShortcut = "CTRL+ALT+V"
                }, default ).AsTask( ) );
                // Unrelated saves must work even while inspection is unavailable.
                await catalog.ReplacePreferencesAsync( defaults, default );

                await File.WriteAllTextAsync( hyprctl, "#!/bin/sh\nprintf '[]'\n" );
                File.SetUnixFileMode( hyprctl, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
                await catalog.ReplacePreferencesAsync( new( )
                {
                    Sources = defaults.Sources,
                    ClipboardHistoryShortcut = "ctrl+alt+v"
                }, default );
            }

            await using ( SqliteCatalog catalog = new( options ) )
            {
                await catalog.InitializeAsync( default );
                var current = await catalog.GetPreferencesAsync( default );
                Equal( "CTRL+ALT+V", current.ClipboardHistoryShortcut );
                SavedSearch search = new( )
                {
                    Id = "",
                    Name = "Files",
                    Expression = QueryParser.Parse( "file:" ).Expression,
                    Shortcut = "CTRL+ALT+V"
                };
                await ThrowsAsync<InvalidOperationException>( ( ) => catalog.SaveSavedSearchAsync( search, default ).AsTask( ) );
                await catalog.ReplacePreferencesAsync( new( )
                {
                    Sources = current.Sources,
                    ClipboardHistoryShortcut = ""
                }, default );
                Equal( "", (await catalog.GetPreferencesAsync( default )).ClipboardHistoryShortcut );
                await catalog.SaveSavedSearchAsync( search, default );
                await ThrowsAsync<InvalidOperationException>( ( ) => catalog.ReplacePreferencesAsync(
                    new( )
                    {
                        Sources = current.Sources,
                        ClipboardHistoryShortcut = "CTRL+ALT+V"
                    }, default ).AsTask( ) );
                Equal( "", (await catalog.GetPreferencesAsync( default )).ClipboardHistoryShortcut );
                Equal( ClipboardShortcut.Default, (await catalog.ResetAsync( default )).ClipboardHistoryShortcut );

                // Recreate an upgrade: a saved search owned the default before the preference existed.
                await catalog.ReplacePreferencesAsync( new( )
                {
                    Sources = current.Sources,
                    ClipboardHistoryShortcut = ""
                }, default );
                await catalog.SaveSavedSearchAsync( new( )
                {
                    Id = "saved-search:legacy",
                    Name = "Legacy",
                    Shortcut = ClipboardShortcut.Default,
                    Expression = QueryParser.Parse( "file:" ).Expression
                }, default );
                await using ( var connection = new Microsoft.Data.Sqlite.SqliteConnection( $"Data Source={options.DatabasePath}" ) )
                {
                    await connection.OpenAsync( );
                    await using var command = connection.CreateCommand( );
                    command.CommandText = "DELETE FROM launcher_preferences WHERE key = 'clipboard_history_shortcut';";
                    await command.ExecuteNonQueryAsync( );
                }

                var updated = await catalog.SaveSavedSearchAsync( new( )
                {
                    Id = "saved-search:legacy",
                    Name = "Renamed",
                    Shortcut = "ctrl+shift+v",
                    Expression = QueryParser.Parse( "file: report" ).Expression
                }, default );
                Equal( "Renamed", updated.Single( value => value.Id == "saved-search:legacy" ).Name );
                Equal( "report", updated.Single( value => value.Id == "saved-search:legacy" ).Expression.Clauses.Single( ).Text );
                await ThrowsAsync<InvalidOperationException>( ( ) => catalog.SaveSavedSearchAsync( new( )
                {
                    Id = "saved-search:new",
                    Name = "New claimant",
                    Shortcut = ClipboardShortcut.Default
                }, default ).AsTask( ) );

                await catalog.SaveSavedSearchAsync( new( )
                {
                    Id = "saved-search:legacy",
                    Name = "Released"
                }, default );
                await ThrowsAsync<InvalidOperationException>( ( ) => catalog.SaveSavedSearchAsync( new( )
                {
                    Id = "saved-search:legacy",
                    Name = "Former owner",
                    Shortcut = ClipboardShortcut.Default
                }, default ).AsTask( ) );
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", previousPath );
            Directory.Delete( root, recursive: true );
        }
    }
}
