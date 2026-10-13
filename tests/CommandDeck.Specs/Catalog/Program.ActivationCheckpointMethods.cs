using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static long WalBytes( string database ) =>
        File.Exists( database + "-wal" ) ? new FileInfo( database + "-wal" ).Length : 0;

    private static async Task GenerationActivationTruncatesWalAndKeepsResultsAsync( )
    {
        var database = Path.Combine( Path.GetTempPath( ), $"commanddeck-generation-wal-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.ReplaceAllAsync( Items( ), null, default );
            Equal( 0L, WalBytes( database ) );
            var before = (await SearchAsync( catalog, "generationmarker" )).Select( static row => row.Id ).ToArray( );
            Equal( 10, before.Length );
            await catalog.ReplaceAllAsync( Items( ), null, default );
            Equal( 0L, WalBytes( database ) );
            True( before.SequenceEqual( (await SearchAsync( catalog, "generationmarker" )).Select( static row => row.Id ) ) );
            Equal( 1_000L, (await catalog.GetStatsAsync( default )).ItemCount );
            True( (await catalog.CheckIntegrityAsync( default )).Succeeded );
        }
        finally
        {
            DeleteDatabase( database );
        }

        static async IAsyncEnumerable<IndexedItem> Items( )
        {
            for ( var index = 0; index < 1_000; index++ )
            {
                yield return new IndexedItem
                {
                    StableId = $"file:generation-{index}",
                    Kind = ResultKinds.File,
                    Title = $"generation-{index}.txt",
                    Path = $"/spec/generation/{index}.txt",
                    Body = string.Join( ' ', Enumerable.Repeat( "generationmarker", 80 ) ),
                    ProviderId = ProviderIds.Files,
                    SourceId = SourceIds.Files
                };
            }
            await Task.CompletedTask;
        }
    }

    private static async Task ActivationCheckpointDoesNotWaitForReadersAsync( )
    {
        const long busyTimeoutMilliseconds = 10_000;
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-activation-checkpoint-{Guid.NewGuid( ):N}.db" );
        try
        {
            // Every connection the catalog opens gets this busy timeout, so
            // activation runs with it too.
            await using SqliteCatalog catalog = new( new StorageOptions
            {
                DatabasePath = database,
                BusyTimeoutMilliseconds = ( int ) busyTimeoutMilliseconds
            } );
            await catalog.ReplaceAllAsync( CheckpointItems( ), null, CancellationToken.None );

            // A reader still on the published generation keeps the WAL from being
            // reset. With a busy handler, TRUNCATE waited for it while holding the
            // writer lock, so every writer waited the whole busy timeout.
            await using SqliteConnection reader = new( $"Data Source={database};Pooling=False" );
            await reader.OpenAsync( );
            await using var snapshot = reader.BeginTransaction( deferred: true );
            await using ( var read = reader.CreateCommand( ) )
            {
                read.Transaction = snapshot;
                read.CommandText = "SELECT COUNT(*) FROM catalog_items;";
                Equal( 2_000L, ( long ) (await read.ExecuteScalarAsync( ))! );
            }

            var started = Stopwatch.GetTimestamp( );
            var replacement = catalog.ReplaceAllAsync( CheckpointItems( ), null, CancellationToken.None ).AsTask( );
            var longestWriterWait = await MeasureLongestWriterWaitAsync( database, replacement );
            await replacement.WaitAsync( TimeSpan.FromSeconds( 30 ) );
            var elapsed = Stopwatch.GetElapsedTime( started );

            True( elapsed < TimeSpan.FromSeconds( 5 ),
                $"activation did not wait for the reader: {elapsed.TotalMilliseconds:F0} ms" );
            True( longestWriterWait < TimeSpan.FromSeconds( 5 ),
                $"a concurrent writer waited {longestWriterWait.TotalMilliseconds:F0} ms" );
            True( WalBytes( database ) > 0, "the reader left the checkpoint partial" );
            Equal( busyTimeoutMilliseconds, await ReadPooledBusyTimeoutAsync( catalog ) );

            snapshot.Rollback( );
            await reader.CloseAsync( );
            await catalog.ReplaceAllAsync( CheckpointItems( ), null, CancellationToken.None );
            Equal( 0L, WalBytes( database ) );
            True( (await catalog.CheckIntegrityAsync( CancellationToken.None )).Succeeded );
        }
        finally
        {
            DeleteDatabase( database );
        }

        static async IAsyncEnumerable<IndexedItem> CheckpointItems( )
        {
            var filler = string.Join( ' ', Enumerable.Repeat( "checkpointword filler text", 60 ) );
            for ( var index = 0; index < 2_000; index++ )
            {
                yield return new IndexedItem
                {
                    StableId = $"file:checkpoint-{index:D4}",
                    Kind = ResultKinds.File,
                    Title = $"checkpoint-{index:D4}.txt",
                    Path = $"/spec/checkpoint/checkpoint-{index:D4}.txt",
                    Body = $"{filler} checkpointitem{index:D4}",
                    ProviderId = ProviderIds.Files,
                    SourceId = SourceIds.Files
                };
            }

            await Task.CompletedTask;
        }
    }

    private static async Task<long> ReadPooledBusyTimeoutAsync( SqliteCatalog catalog )
    {
        await using var connection = await catalog.OpenAsync( CancellationToken.None );
        await using var busy = connection.CreateCommand( );
        busy.CommandText = "PRAGMA busy_timeout;";

        return ( long ) (await busy.ExecuteScalarAsync( ))!;
    }

    private static async Task<TimeSpan> MeasureLongestWriterWaitAsync( string database, Task operation )
    {
        await using SqliteConnection writer = new( $"Data Source={database};Pooling=False;Default Timeout=60" );
        await writer.OpenAsync( );
        await using ( var busy = writer.CreateCommand( ) )
        {
            busy.CommandText = "PRAGMA busy_timeout = 60000;";
            await busy.ExecuteNonQueryAsync( );
        }

        var longest = TimeSpan.Zero;
        while ( !operation.IsCompleted )
        {
            var started = Stopwatch.GetTimestamp( );
            await using ( var transaction = writer.BeginTransaction( deferred: false ) )
            {
                transaction.Rollback( );
            }

            var waited = Stopwatch.GetElapsedTime( started );
            longest = waited > longest ? waited : longest;
            await Task.Delay( 10 );
        }

        return longest;
    }
}
