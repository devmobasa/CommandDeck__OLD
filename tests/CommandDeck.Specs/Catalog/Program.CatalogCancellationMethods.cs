using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    // A superseded keystroke used to keep its catalog provider slot until its FTS
    // step had ranked every prefix match, so two cancelled searches left the next
    // one saturated. Budgets are relative to an uncancelled search on this machine.
    private static async Task CancelledCatalogSearchReleasesProviderSlotsAsync( )
    {
        var database = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-catalog-interrupt-{Guid.NewGuid( ):N}.db" );
        SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
        SearchCoordinator search = new(
            [new CatalogSearchProvider( catalog )],
            new SearchOptions { ProviderTimeoutMilliseconds = 30_000 } );
        try
        {
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( SlowPrefixMatchItems( 30_000 ), null, default );
            SearchRequest heavy = new( )
            {
                Query = "s"
            };
            _ = await search.SearchWithOutcomeAsync( heavy, SearchPurpose.Interactive, default );
            var uncancelled = Stopwatch.StartNew( );
            var complete = await search.SearchWithOutcomeAsync( heavy, SearchPurpose.Interactive, default );
            uncancelled.Stop( );
            Equal( 0, complete.IncompleteProviderCount );

            // The coordinator runs a wider query and source supplements. Measure
            // the direct query itself: it can finish before a timer calibrated
            // against the coordinator fires, especially after ranked FTS improved.
            var parsed = QueryParser.Parse( "s" );
            var directDuration = TimeSpan.MaxValue;
            for ( var attempt = 0; attempt < 3; attempt++ )
            {
                var measurement = Stopwatch.StartNew( );
                _ = await catalog.SearchAsync( parsed, 50, default );
                directDuration = TimeSpan.FromTicks( Math.Min( directDuration.Ticks, measurement.Elapsed.Ticks ) );
            }

            var catalogCancellation = Stopwatch.StartNew( );
            using ( CancellationTokenSource direct = new( ) )
            {
                // SQLite's async API performs its step synchronously. A dedicated
                // cancellation thread cannot be delayed by blocked pool workers.
                var cancel = CancelCatalogQueriesAfterAsync( directDuration / 4, direct );
                try
                {
                    await ThrowsAsync<OperationCanceledException>( async ( ) =>
                        await catalog.SearchAsync( parsed, 50, direct.Token ) );
                }
                finally
                {
                    await cancel;
                }
            }
            catalogCancellation.Stop( );
            True( catalogCancellation.Elapsed < directDuration * 0.75,
                $"a cancelled catalog query returned after {catalogCancellation.Elapsed.TotalMilliseconds:F0} ms; " +
                $"the same uncancelled query takes {directDuration.TotalMilliseconds:F0} ms" );

            using CancellationTokenSource first = new( );
            using CancellationTokenSource second = new( );

            // Both SQLite steps can occupy the pool workers. Schedule cancellation
            // before starting them, on the same dedicated seam as the direct query.
            var cancelSuperseded = CancelCatalogQueriesAfterAsync( uncancelled.Elapsed / 4, first, second );
            var superseded = new[]
            {
                search.SearchWithOutcomeAsync( heavy, SearchPurpose.Interactive, first.Token ).AsTask( ),
                search.SearchWithOutcomeAsync( heavy, SearchPurpose.Interactive, second.Token ).AsTask( )
            };

            long cancelledAt;
            try
            {
                foreach ( var pending in superseded )
                {
                    await ThrowsAsync<OperationCanceledException>( async ( ) => await pending );
                }
            }
            finally
            {
                cancelledAt = await cancelSuperseded;
            }

            SearchRequest followUp = new( )
            {
                Query = "sample 42"
            };
            var outcome = await search.SearchWithOutcomeAsync( followUp, SearchPurpose.Interactive, default );
            while ( outcome.IncompleteProviderCount > 0 && Stopwatch.GetElapsedTime( cancelledAt ) < uncancelled.Elapsed * 2 )
            {
                await Task.Delay( 5 );
                outcome = await search.SearchWithOutcomeAsync( followUp, SearchPurpose.Interactive, default );
            }

            var sinceCancel = Stopwatch.GetElapsedTime( cancelledAt );

            Equal( 0, outcome.IncompleteProviderCount );
            True( outcome.Results.Any( result => result.Id == "slow:42" ) );
            True( sinceCancel < uncancelled.Elapsed / 2,
                $"the follow-up search stayed saturated for {sinceCancel.TotalMilliseconds:F0} ms " +
                $"after cancellation; an uncancelled search takes {uncancelled.Elapsed.TotalMilliseconds:F0} ms" );
        }
        finally
        {
            await search.DisposeAsync( );
            await catalog.DisposeAsync( );
            SqliteConnection.ClearAllPools( );
            DeleteDatabase( database );
        }
    }

    private static Task<long> CancelCatalogQueriesAfterAsync( TimeSpan delay, params CancellationTokenSource[] cancellations ) =>
        Task.Factory.StartNew(
            ( ) =>
            {
                Thread.Sleep( delay );
                var cancelledAt = Stopwatch.GetTimestamp( );
                foreach ( var cancellation in cancellations )
                {
                    cancellation.Cancel( );
                }

                return cancelledAt;
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default );

    // Every row matches the one-letter prefix "s" through many body tokens, so a
    // ranked prefix query spends a measurable time inside one SQLite step.
    private static async IAsyncEnumerable<IndexedItem> SlowPrefixMatchItems( int count )
    {
        System.Text.StringBuilder body = new( );
        for ( var index = 0; index < count; index++ )
        {
            body.Clear( );
            for ( var word = 0; word < 40; word++ )
            {
                body.Append( 's' )
                    .Append( ( char ) ('a' + (index + word) % 26) )
                    .Append( (index * 31 + word) % 997 )
                    .Append( ' ' );
            }

            yield return new IndexedItem
            {
                StableId = $"slow:{index}",
                Kind = ResultKinds.File,
                Title = $"sample {index}.txt",
                Subtitle = $"series/section {index % 50}",
                Path = $"/srv/samples/section-{index % 50}/sample-{index}.txt",
                Body = body.ToString( ),
                ProviderId = ProviderIds.Files,
                SourceId = SourceIds.Files
            };
            if ( index % 1_000 == 0 )
            {
                await Task.Yield( );
            }
        }
    }
}
