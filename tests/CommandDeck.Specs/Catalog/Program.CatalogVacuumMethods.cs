namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task CatalogOptimizeVacuumsOnlyWhenItFreesSpaceAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-vacuum-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.ReplaceAllAsync( VacuumItems( 2_000 ), null, CancellationToken.None );

            // Scheduled maintenance ran VACUUM every six hours: it rewrote the
            // whole database through the WAL even when nothing was free.
            var compact = await catalog.OptimizeAsync( CancellationToken.None );
            True( compact.Succeeded );
            True( compact.Messages.Any( static message =>
                    message.Contains( "vacuum skipped", StringComparison.Ordinal ) ),
                string.Join( ' ', compact.Messages ) );
            True( WalBytes( database ) < compact.DatabaseBytesAfter / 4,
                $"the database was not rewritten: WAL {WalBytes( database )} bytes" );

            await catalog.ReplaceAllAsync( VacuumItems( 20 ), null, CancellationToken.None );
            var sparse = await ReadPageCountsAsync( catalog );
            True( sparse.Free * 4 >= sparse.Total, $"fixture frees most pages: {sparse}" );

            var vacuumed = await catalog.OptimizeAsync( CancellationToken.None );
            True( vacuumed.Succeeded );
            True( vacuumed.Messages.Any( static message =>
                    message.Contains( "database vacuumed", StringComparison.Ordinal ) ),
                string.Join( ' ', vacuumed.Messages ) );
            Equal( 0L, (await ReadPageCountsAsync( catalog )).Free );
            True( vacuumed.DatabaseBytesAfter < vacuumed.DatabaseBytesBefore / 2,
                $"vacuum returned the space: {vacuumed.DatabaseBytesBefore} -> {vacuumed.DatabaseBytesAfter}" );
            Equal( 0L, WalBytes( database ) );
            Equal( 20L, (await catalog.GetStatsAsync( CancellationToken.None )).ItemCount );
            Equal( "file:vacuum-0019", (await SearchAsync( catalog, "vacuumitem0019" )).Single( ).Id );
            True( (await catalog.CheckIntegrityAsync( CancellationToken.None )).Succeeded );
        }
        finally
        {
            DeleteDatabase( database );
        }

        static async IAsyncEnumerable<IndexedItem> VacuumItems( int count )
        {
            var filler = string.Join( ' ', Enumerable.Repeat( "vacuumword filler text", 100 ) );
            for ( var index = 0; index < count; index++ )
            {
                yield return new IndexedItem
                {
                    StableId = $"file:vacuum-{index:D4}",
                    Kind = ResultKinds.File,
                    Title = $"vacuum-{index:D4}.txt",
                    Path = $"/spec/vacuum/vacuum-{index:D4}.txt",
                    Body = $"{filler} vacuumitem{index:D4}",
                    ProviderId = ProviderIds.Files,
                    SourceId = SourceIds.Files
                };
            }

            await Task.CompletedTask;
        }
    }

    private static async Task<(long Free, long Total)> ReadPageCountsAsync( SqliteCatalog catalog )
    {
        await using var connection = await catalog.OpenAsync( CancellationToken.None );
        await using var command = connection.CreateCommand( );
        command.CommandText = "SELECT freelist_count, page_count FROM pragma_freelist_count, pragma_page_count;";
        await using var reader = await command.ExecuteReaderAsync( );
        True( await reader.ReadAsync( ) );

        return (reader.GetInt64( 0 ), reader.GetInt64( 1 ));
    }
}
