namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    // One indexed existence probe per source (ix_catalog_items_source), read at
    // query time: nothing is cached, so a reindex, an incremental apply, or new
    // configured items that add a source are visible to the next search.
    public async ValueTask<IReadOnlySet<string>> GetPopulatedSourcesAsync(
        IReadOnlyList<string> sourceIds,
        CancellationToken cancellationToken )
    {
        HashSet<string> populated = new( StringComparer.Ordinal );
        if ( sourceIds.Count == 0 )
        {
            return populated;
        }

        await InitializeAsync( cancellationToken ).ConfigureAwait( false );
        await using var connection = await OpenAsync( cancellationToken ).ConfigureAwait( false );
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM catalog_items WHERE source_id = $source);";
        var source = command.Parameters.Add( "$source", Microsoft.Data.Sqlite.SqliteType.Text );

        foreach ( var sourceId in sourceIds.Distinct( StringComparer.Ordinal ) )
        {
            source.Value = sourceId;
            var exists = await command.ExecuteScalarAsync( cancellationToken ).ConfigureAwait( false );
            if ( exists is not long value )
            {
                throw new InvalidOperationException( "The catalog source probe returned no value." );
            }

            if ( value != 0 )
            {
                populated.Add( sourceId );
            }
        }

        return populated;
    }
}
