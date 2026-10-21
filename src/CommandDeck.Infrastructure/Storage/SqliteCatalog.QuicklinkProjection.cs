using Microsoft.Data.Sqlite;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    private static async ValueTask PublishQuicklinksAsync( SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<QuicklinkEntry> entries, CancellationToken cancellationToken )
    {
        await using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM catalog_fts WHERE rowid IN (SELECT fts_rowid FROM catalog_items WHERE kind='quicklink');
            DELETE FROM catalog_items WHERE kind='quicklink';
            """;
        await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );

        foreach ( var entry in entries.Where( value => value.Enabled ) )
        {
            var item = ConfiguredItemSource.CreateQuicklinkItem( entry.Quicklink );
            command.Parameters.Clear( );
            command.CommandText = $"""
                INSERT INTO catalog_fts({CatalogSearchColumns})
                VALUES($id,'quicklink',$source,$title,$subtitle,'',$body,'');
                INSERT INTO catalog_items(stable_id,kind,title,subtitle,icon,payload_json,provider_id,source_id,fts_rowid)
                VALUES($id,'quicklink',$title,$subtitle,$icon,$payload,'quicklinks',$source,last_insert_rowid());
                """;
            command.Parameters.AddWithValue( "$id", item.StableId );
            command.Parameters.AddWithValue( "$source", item.SourceId );
            command.Parameters.AddWithValue( "$title", item.Title );
            command.Parameters.AddWithValue( "$subtitle", item.Subtitle! );
            command.Parameters.AddWithValue( "$body", item.Body! );
            command.Parameters.AddWithValue( "$icon", item.Icon! );
            command.Parameters.AddWithValue( "$payload", item.PayloadJson! );
            await command.ExecuteNonQueryAsync( cancellationToken ).ConfigureAwait( false );
        }
    }
}
