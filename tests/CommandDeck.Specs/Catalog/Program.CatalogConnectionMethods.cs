using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    // synchronous = NORMAL used to stick only to the bootstrap connection, so
    // pooled connections committed with FULL, and pragmas set for one job (a
    // generation's file-backed stage, a registry commit's FULL sync) leaked to
    // whoever reused that connection.
    private static async Task PooledCatalogConnectionsApplyTheirPragmasAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-pragmas-{Guid.NewGuid( ):N}.db" );
        try
        {
            SqliteCatalog catalog = new( new StorageOptions
            {
                DatabasePath = database,
                BusyTimeoutMilliseconds = 1_900
            } );
            await catalog.InitializeAsync( default );

            for ( var round = 0; round < 2; round++ )
            {
                SqliteConnection[] connections =
                [
                    await catalog.OpenAsync( default ),
                    await catalog.OpenAsync( default ),
                    await catalog.OpenAsync( default )
                ];
                foreach ( var connection in connections )
                {
                    Equal( 1L, await PragmaAsync( connection, "synchronous" ) );
                    Equal( 1_900L, await PragmaAsync( connection, "busy_timeout" ) );
                    Equal( 1L, await PragmaAsync( connection, "temp_store" ) );
                    Equal( 16L * 1024 * 1024, await PragmaAsync( connection, "journal_size_limit" ) );
                    Equal( 1L, await PragmaAsync( connection, "foreign_keys" ) );

                    // Jobs change these for their own work; the next open restores them.
                    await using var change = connection.CreateCommand( );
                    change.CommandText = "PRAGMA synchronous = FULL; PRAGMA temp_store = MEMORY; PRAGMA busy_timeout = 1;";
                    await change.ExecuteNonQueryAsync( );
                }

                foreach ( var connection in connections )
                {
                    await connection.DisposeAsync( );
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            DeleteDatabase( database );
        }
    }

    // storage.busyTimeoutMilliseconds used to reach Microsoft.Data.Sqlite only as
    // whole seconds (1,900 ms became 1 s) and SQLite itself never waited, so a
    // writer blocked for 1.4 s failed with "database is locked".
    private static async Task BlockedCatalogWriterWaitsForTheConfiguredTimeoutAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-busy-{Guid.NewGuid( ):N}.db" );
        try
        {
            SqliteCatalog catalog = new( new StorageOptions
            {
                DatabasePath = database,
                BusyTimeoutMilliseconds = 1_900
            } );
            await catalog.InitializeAsync( default );
            await using SqliteConnection blocker = new( $"Data Source={database};Pooling=False" );
            await blocker.OpenAsync( );
            await using ( var begin = blocker.CreateCommand( ) )
            {
                begin.CommandText = "BEGIN IMMEDIATE; DELETE FROM launcher_preferences WHERE key = 'spec-lock';";
                await begin.ExecuteNonQueryAsync( );
            }

            var blocked = Stopwatch.StartNew( );
            var release = Task.Run( async ( ) =>
            {
                await Task.Delay( 1_400 );
                await using var commit = blocker.CreateCommand( );
                commit.CommandText = "COMMIT;";
                await commit.ExecuteNonQueryAsync( );
            } );
            var saved = await catalog.ReplacePreferencesAsync( LauncherPreferences.CreateDefault( ), default );
            blocked.Stop( );
            await release;

            True( saved.Sources.Count > 0 );
            True( blocked.Elapsed >= TimeSpan.FromMilliseconds( 1_300 ),
                $"the write finished after {blocked.Elapsed.TotalMilliseconds:F0} ms while the lock was still held" );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            DeleteDatabase( database );
        }
    }

    private static async Task<long> PragmaAsync( SqliteConnection connection, string pragma )
    {
        await using var command = connection.CreateCommand( );
        command.CommandText = $"PRAGMA {pragma};";
        var value = await command.ExecuteScalarAsync( );
        if ( value is not long number )
        {
            throw new InvalidOperationException( $"PRAGMA {pragma} returned {value ?? "nothing"}." );
        }

        return number;
    }
}
