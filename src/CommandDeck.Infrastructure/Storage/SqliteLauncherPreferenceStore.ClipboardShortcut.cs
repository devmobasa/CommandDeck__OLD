using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore
{
    private static async ValueTask EnsureClipboardShortcutAvailableAsync( SqliteConnection connection, SqliteTransaction transaction,
        string chord, CancellationToken token )
    {
        if ( chord.Length == 0 )
        {
            return;
        }

        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM launcher_preferences WHERE key = 'clipboard_history_shortcut';";
        var previous = ( string? ) await command.ExecuteScalarAsync( token ).ConfigureAwait( false ) ?? ClipboardShortcut.Default;
        // Existing saved-search bindings keep precedence on upgrade; unrelated preference saves must still work.
        if ( string.Equals( previous, chord, StringComparison.OrdinalIgnoreCase ) )
        {
            return;
        }

        command.CommandText = "SELECT name FROM saved_searches WHERE shortcut = $chord COLLATE NOCASE LIMIT 1;";
        command.Parameters.AddWithValue( "$chord", chord );
        if ( await command.ExecuteScalarAsync( token ).ConfigureAwait( false ) is string name )
        {
            throw new InvalidOperationException( $"Clipboard shortcut is already assigned to saved search '{name}'. Choose another chord or disable it." );
        }

        var inventory = await HyprlandShortcutInspector.InspectInventoryAsync( token ).ConfigureAwait( false );
        SavedSearchValidator.EnsureShortcutAvailable( chord, string.Empty, [], inventory );
    }

    private static async ValueTask EnsureNotClipboardShortcutAsync( SqliteConnection connection, SqliteTransaction transaction,
        string id, string? chord, CancellationToken token )
    {
        if ( chord is null )
        {
            return;
        }

        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM launcher_preferences WHERE key = 'clipboard_history_shortcut';";
        var clipboardChord = ( string? ) await command.ExecuteScalarAsync( token ).ConfigureAwait( false ) ?? ClipboardShortcut.Default;
        if ( string.Equals( clipboardChord, chord, StringComparison.OrdinalIgnoreCase ) )
        {
            command.CommandText = "SELECT shortcut FROM saved_searches WHERE id = $id;";
            command.Parameters.AddWithValue( "$id", id );
            var previous = await command.ExecuteScalarAsync( token ).ConfigureAwait( false ) as string;
            if ( string.Equals( previous, chord, StringComparison.OrdinalIgnoreCase ) )
            {
                return;
            }

            throw new InvalidOperationException( "Shortcut is assigned to Clipboard History. Change it in Settings → Shortcuts first." );
        }
    }
}
