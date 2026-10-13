using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task<double> MeasureSparseFuzzyLatencyAsync( string root )
    {
        await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "sparse.db" ) } );
        await catalog.InitializeAsync( default );
        await using ( var connection = await catalog.OpenAsync( default ) )
        {
            await SeedSparseFuzzySourcesAsync( connection );
            await GrowDisabledFuzzySourceAsync( connection, 0, 100_000 );
        }

        var query = QueryParser.Parse( "brve" );
        return await MeasureP95Async( async ( ) =>
        {
            var results = await catalog.SearchSourcesAsync( query, [SourceIds.Applications, SourceIds.Files], 20, default );
            Equal( 20, results.Count );
            True( results.All( result => result.Id.StartsWith( "visible:", StringComparison.Ordinal ) ) );
        } );
    }

    private static async Task SparseFuzzyLookupIgnoresDisabledCatalogGrowthAsync( )
    {
        var database = Path.Combine( Path.GetTempPath( ), $"commanddeck-sparse-fuzzy-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( default );
            await using var connection = await catalog.OpenAsync( default );
            await SeedSparseFuzzySourcesAsync( connection );
            await GrowDisabledFuzzySourceAsync( connection, 0, 5_000 );
            var small = await MeasureSparseFuzzyWorkAsync( connection );
            await GrowDisabledFuzzySourceAsync( connection, 5_000, 100_000 );
            var large = await MeasureSparseFuzzyWorkAsync( connection );

            Console.WriteLine( $"MEASURE sparse fuzzy SQLite work: small={small} large={large} progress callbacks" );
            True( large <= small * 3,
                "A 20x increase in disabled rows must not cause a proportional fuzzy candidate walk." );

            var results = await catalog.SearchSourcesAsync( QueryParser.Parse( "brve" ),
                [SourceIds.Applications, SourceIds.Files], 20, default );
            Equal( 20, results.Count );
            True( results.All( result => result.Id.StartsWith( "visible:", StringComparison.Ordinal ) ) );
            True( results.Any( result => result.SourceId == SourceIds.Applications ) );
            True( results.Any( result => result.SourceId == SourceIds.Files ) );

            var repeatedSource = await catalog.SearchSourcesAsync( QueryParser.Parse( "brve" ),
                [SourceIds.Applications, SourceIds.Files, SourceIds.Applications], 20, default );
            True( results.Select( result => result.Id ).SequenceEqual( repeatedSource.Select( result => result.Id ) ),
                "Repeating a selected source must not duplicate rows or consume another source's budget." );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task SeedSparseFuzzySourcesAsync( SqliteConnection connection )
    {
        await using var seed = connection.CreateCommand( );
        seed.CommandText = """
            WITH RECURSIVE numbers(n) AS (
                SELECT 0 UNION ALL SELECT n + 1 FROM numbers WHERE n < 575
            )
            INSERT INTO catalog_items(stable_id, kind, title, provider_id, source_id)
            SELECT CASE WHEN n < 512 THEN 'hidden:' ELSE 'visible:' END || printf('%04d', n),
                   CASE WHEN n % 2 = 0 THEN 'application' ELSE 'file' END,
                   printf('Brave %04d', n), 'catalog',
                   CASE WHEN n % 2 = 0 THEN 'applications' ELSE 'files' END
            FROM numbers;
            INSERT INTO hidden_items(stable_id, title_snapshot, source_id, hidden_unix_ms)
            SELECT stable_id, title, source_id, 0 FROM catalog_items WHERE stable_id LIKE 'hidden:%';
            """;
        await seed.ExecuteNonQueryAsync( );
    }

    private static async Task GrowDisabledFuzzySourceAsync( SqliteConnection connection, int start, int end )
    {
        await using var seed = connection.CreateCommand( );
        seed.CommandText = """
            WITH RECURSIVE numbers(n) AS (
                SELECT $start UNION ALL SELECT n + 1 FROM numbers WHERE n + 1 < $end
            )
            INSERT INTO catalog_items(stable_id, kind, title, provider_id, source_id)
            SELECT 'disabled:' || n, 'configuration', printf('Brave -disabled %06d', n), 'catalog', 'configuration'
            FROM numbers;
            """;
        seed.Parameters.AddWithValue( "$start", start );
        seed.Parameters.AddWithValue( "$end", end );
        await seed.ExecuteNonQueryAsync( );
    }

    private static async Task<int> MeasureSparseFuzzyWorkAsync( SqliteConnection connection )
    {
        var progress = 0;
        SQLitePCL.raw.sqlite3_progress_handler( connection.Handle, 1_000, _ => { progress++; return 0; }, null );
        try
        {
            await using var command = SqliteCatalog.CreateFuzzyCandidateCommand( connection,
                QueryParser.Parse( "brve" ), [SourceIds.Applications, SourceIds.Files], 'b' );
            List<string> ids = [];
            await using var reader = await command.ExecuteReaderAsync( );
            while ( await reader.ReadAsync( ) )
            {
                ids.Add( reader.GetString( 0 ) );
            }

            Equal( string.Join( ',', Enumerable.Range( 512, 64 ).Select( index => $"visible:{index:D4}" ) ),
                string.Join( ',', ids ) );
            True( progress > 0, "SQLite work counter must be active." );
            return progress;
        }
        finally
        {
            SQLitePCL.raw.sqlite3_progress_handler( connection.Handle, 0, null, null );
        }
    }
}
