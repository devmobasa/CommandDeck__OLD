namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ResultCacheBoundsAndExpiry( )
    {
        CacheClock clock = new( );
        SearchResultCache cache = new( clock, capacity: 2 );
        var one = CacheResult( "one" );
        var two = CacheResult( "two" );
        var three = CacheResult( "three" );
        cache.Store( [one, two], "first" );
        cache.Store( [one], "refreshed" );
        cache.Store( [three], "preview" );
        Equal( 2, cache.Count );
        True( !cache.TryGet( two.Id, out _ ) );
        True( cache.TryGet( one.Id, out var active ) );
        Equal( "refreshed", active!.Query );

        clock.Timestamp = 120;
        True( !cache.TryGet( one.Id, out _ ) );
        cache.Store( [two], "new" );
        Equal( 1, cache.Count );
        True( cache.TryGet( two.Id, out _ ) );
        True( !cache.TryGet( three.Id, out _ ) );

        Parallel.For( 0, 1000, index => cache.Store( [CacheResult( index.ToString( ) )], "burst" ) );
        Equal( 2, cache.Count );
    }

    private static void DetachedUndoCacheBoundsAndExpiry( )
    {
        CacheClock clock = new( );
        SearchResultCache cache = new( clock, capacity: 2 );
        var row = CacheResult( "one" );
        cache.Store( [row], "live", owner: "client" );
        var undo = cache.StoreDetached( row, "original", false, "client" );
        Equal( 2, cache.Count );
        True( cache.TryGetOwned( row.Id, "client", out var live ) );
        Equal( "live", live!.Query );
        True( !cache.TryGetInvocation( undo, "other", row.Id, out _ ) );

        // A refresh retires the live reference but leaves its undo. Expiring
        // that undo must not erase the newer stable-ID entry with the same ID.
        clock.Timestamp = 60;
        cache.Store( [row], "refreshed", owner: "client" );
        clock.Timestamp = 120;
        True( !cache.TryGetInvocation( undo, "client", row.Id, out _ ) );
        True( cache.TryGetOwned( row.Id, "client", out live ) );
        Equal( "refreshed", live!.Query );

        var evicted = cache.StoreDetached( row, "undo", false, "client" );
        var newest = cache.StoreDetached( row, "undo", false, "client" );
        var last = cache.StoreDetached( row, "undo", false, "client" );
        Equal( 2, cache.Count );
        True( !cache.TryGetInvocation( evicted, "client", row.Id, out _ ) );
        True( !cache.TryGet( row.Id, out _ ) );
        True( !cache.TryGetOwned( row.Id, "client", out _ ) );
        True( cache.TryGetInvocation( newest, "client", row.Id, out _ ) );
        True( cache.TryGetInvocation( last, "client", row.Id, out _ ) );
        cache.RemoveOwner( "client" );
        Equal( 0, cache.Count );
    }

    private static SearchResult CacheResult( string id ) => new( )
    {
        Id = id,
        Title = id,
        Kind = ResultKinds.Application,
        ProviderId = "test",
        SourceId = SourceIds.Applications
    };

    private sealed class CacheClock : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => 1;
        public override long GetTimestamp( ) => Timestamp;
    }
}
