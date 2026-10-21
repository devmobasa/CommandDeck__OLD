using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed partial class SqliteLauncherPreferenceStore
{
    private async ValueTask<QuicklinkState> ExportQuicklinksAsync( CancellationToken cancellationToken )
    {
        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var transaction = connection.BeginTransaction( deferred: true );
        return await ReadQuicklinkStateAsync( connection, transaction, cancellationToken ).ConfigureAwait( false );
    }

    internal static async ValueTask<QuicklinkState> ReadQuicklinkStateAsync( SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken )
    {
        QuicklinkState state = new( );
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM disabled_configured_quicklinks ORDER BY id;";
        await using ( var reader = await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
            {
                state.DisabledConfiguredIds.Add( reader.GetString( 0 ) );
            }
        }

        command.CommandText = "SELECT id,title,url_template,alias,icon,created_unix_ms FROM user_quicklinks ORDER BY created_unix_ms,id;";
        await using ( var reader = await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
            {
                state.UserQuicklinks.Add( new QuicklinkEntry
                {
                    Source = "user",
                    CreatedUnixMs = reader.GetInt64( 5 ),
                    Quicklink = new QuicklinkOptions
                    {
                        Id = reader.GetString( 0 ),
                        Title = reader.GetString( 1 ),
                        UrlTemplate = reader.GetString( 2 ),
                        Alias = reader.IsDBNull( 3 ) ? null : reader.GetString( 3 ),
                        Icon = reader.IsDBNull( 4 ) ? null : reader.GetString( 4 )
                    }
                } );
            }
        }
        return state;
    }
}
