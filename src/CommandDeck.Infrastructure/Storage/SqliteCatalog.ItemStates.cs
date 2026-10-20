using System.Buffers;
using System.Text.Json;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    private const int MaximumItemStates = 512;

    // One read of bookmarks, aliases and usage for rows a live provider builds
    // under catalog IDs, so they show and toggle the same bookmark and alias as
    // the catalog row and rank text matches by the same accepted use.
    public async ValueTask<IReadOnlyDictionary<string, CatalogItemState>> GetItemStatesAsync(
        string kind,
        IReadOnlyList<string> stableIds,
        CancellationToken cancellationToken )
    {
        Dictionary<string, CatalogItemState> states = new( StringComparer.Ordinal );
        if ( stableIds.Count == 0 )
        {
            return states;
        }

        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = """
            SELECT ids.value AS stable_id,
                   EXISTS(SELECT 1 FROM favorites f WHERE f.stable_id = ids.value) AS is_favorite,
                   COALESCE((SELECT group_concat(ordered_alias.alias, char(31))
                             FROM (SELECT a.alias FROM aliases a
                                   WHERE a.stable_id = ids.value
                                   ORDER BY a.created_unix_ms, a.alias COLLATE NOCASE)
                                  ordered_alias), '') AS aliases,
                   COALESCE(u.use_count, 0) AS use_count,
                   COALESCE(u.last_used_unix_ms, 0) AS last_used_unix_ms
            FROM (SELECT DISTINCT value FROM json_each($stable_ids)) ids
            LEFT JOIN usage u ON u.stable_id = ids.value;
            """;
        command.Parameters.AddWithValue( "$stable_ids", EncodeIds( stableIds.Take( MaximumItemStates ) ) );

        await using var reader = await command.ExecuteReaderAsync( cancellationToken ).ConfigureAwait( false );
        while ( await reader.ReadAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            var favorite = reader.GetInt64( 1 ) != 0;
            var aliases = SplitAliases( reader.GetString( 2 ) );
            var usage = AdaptiveScore( kind, reader.GetInt64( 3 ), reader.GetInt64( 4 ) );
            if ( favorite || aliases.Count > 0 || usage > 0 )
            {
                states[reader.GetString( 0 )] = new CatalogItemState( favorite, aliases, usage );
            }
        }

        return states;
    }

    private static string EncodeIds( IEnumerable<string> stableIds )
    {
        ArrayBufferWriter<byte> buffer = new( );
        using ( Utf8JsonWriter writer = new( buffer ) )
        {
            writer.WriteStartArray( );
            foreach ( var stableId in stableIds )
            {
                writer.WriteStringValue( stableId );
            }

            writer.WriteEndArray( );
        }

        return System.Text.Encoding.UTF8.GetString( buffer.WrittenSpan );
    }
}
