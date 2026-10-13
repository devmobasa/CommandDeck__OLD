namespace CommandDeck.Specs;

public static partial class Program
{
    // Holds every search and box listing until released; a triage change
    // answers at once.
    private sealed class GatedHeyFake
    {
        private readonly TaskCompletionSource _release = new( TaskCreationOptions.RunContinuationsAsynchronously );
        private int _started;

        public HeyFake Inner { get; } = new( );

        public List<CancellationToken> Tokens { get; } = [];

        public int Started => Volatile.Read( ref _started );

        public void Release( ) => _release.TrySetResult( );

        public BoundedProcessRunner Runner => async ( file, args, timeout, limits, token ) =>
        {
            if ( args[0] is "search" or "box" )
            {
                lock ( Tokens )
                {
                    Tokens.Add( token );
                }

                Interlocked.Increment( ref _started );
                await _release.Task.WaitAsync( token );
            }

            return args[0] is "search" or "box"
                ? await Inner.Runner( file, args, timeout, limits, token )
                : HeyResult( """{"ok":true}""" );
        };
    }

    private static async Task UntilAsync( Func<bool> condition, string because )
    {
        using CancellationTokenSource limit = new( TimeSpan.FromSeconds( 5 ) );
        while ( !condition( ) )
        {
            if ( limit.IsCancellationRequested )
            {
                throw new InvalidOperationException( "Timed out waiting: " + because );
            }

            await Task.Delay( 5, CancellationToken.None );
        }
    }

    // Two identical searches in flight share one hey process. A waiter that
    // gives up leaves it running for the other; when every waiter has gone,
    // the process is stopped.
    private static async Task HeyProviderSharesPendingReadsAsync( )
    {
        GatedHeyFake gated = new( );
        var client = new HeyClient( gated.Runner, static ( ) => HeyBinary, HeyEnvironment );
        HeyProvider provider = new( client, new HeyClock( HeyNow ), TimeSpan.Zero );
        Task<IReadOnlyList<SearchResult>> Search( string query, CancellationToken token ) =>
            provider.SearchAsync( QueryParser.Parse( query ), HeyRequest( ), token ).AsTask( );

        using CancellationTokenSource first = new( );
        var one = Search( "hey: invoice", first.Token );
        await UntilAsync( ( ) => gated.Started == 1, "the first search starts hey" );
        var two = Search( "hey:  invoice", CancellationToken.None );
        await Task.Delay( 50 );
        Equal( 1, gated.Started );

        first.Cancel( );
        await ThrowsAsync<OperationCanceledException>( async ( ) => await one );
        True( !gated.Tokens[0].IsCancellationRequested, "the other search still waits for this hey" );
        gated.Release( );
        Equal( "hey:topic:9001", (await two)[0].Id );
        Equal( 1, gated.Inner.Calls.Count );

        // A box listing has no typing pause, so the same box asked for twice
        // while the first is still reading shares it too.
        GatedHeyFake boxes = new( );
        HeyProvider boxProvider = new( new HeyClient( boxes.Runner, static ( ) => HeyBinary, HeyEnvironment ), new HeyClock( HeyNow ),
            TimeSpan.Zero );
        var imbox = boxProvider.SearchAsync( QueryParser.Parse( "hey: box:imbox" ), HeyRequest( ), CancellationToken.None ).AsTask( );
        var filtered = boxProvider.SearchAsync( QueryParser.Parse( "hey: box:imbox lunch" ), HeyRequest( ), CancellationToken.None ).AsTask( );
        await UntilAsync( ( ) => boxes.Started >= 1, "the box listing starts hey" );
        boxes.Release( );
        await Task.WhenAll( imbox, filtered );
        Equal( 1, boxes.Inner.Calls.Count );

        // When the only waiter gives up, hey is stopped.
        GatedHeyFake abandoned = new( );
        HeyProvider abandonedProvider = new( new HeyClient( abandoned.Runner, static ( ) => HeyBinary, HeyEnvironment ),
            new HeyClock( HeyNow ), TimeSpan.Zero );
        using CancellationTokenSource only = new( );
        var alone = abandonedProvider.SearchAsync( QueryParser.Parse( "hey: invoice" ), HeyRequest( ), only.Token ).AsTask( );
        await UntilAsync( ( ) => abandoned.Started == 1, "the search starts hey" );
        only.Cancel( );
        await ThrowsAsync<OperationCanceledException>( async ( ) => await alone );
        await UntilAsync( ( ) => abandoned.Tokens[0].IsCancellationRequested, "hey is stopped once nobody waits" );

        // A read started before a triage change is not shared after it.
        GatedHeyFake changed = new( );
        var changedClient = new HeyClient( changed.Runner, static ( ) => HeyBinary, HeyEnvironment );
        HeyProvider changedProvider = new( changedClient, new HeyClock( HeyNow ), TimeSpan.Zero );
        var before = changedProvider.SearchAsync( QueryParser.Parse( "hey: box:imbox" ), HeyRequest( ), CancellationToken.None ).AsTask( );
        await UntilAsync( ( ) => changed.Started == 1, "the box listing starts hey" );
        Equal( ( HeyFailure? ) null, await changedClient.TriageAsync( HeyTriageChanges.Seen, 601, null, default ) );
        var after = changedProvider.SearchAsync( QueryParser.Parse( "hey: box:imbox" ), HeyRequest( ), CancellationToken.None ).AsTask( );
        await UntilAsync( ( ) => changed.Started == 2, "the listing after the change reads HEY again" );
        changed.Release( );
        await Task.WhenAll( before, after );
    }

    // One character matches nearly every thread and still costs a HEY request.
    private static async Task HeyProviderNeedsTwoCharactersAsync( )
    {
        HeyFake fake = new( );
        var provider = HeyProviderFor( fake );

        foreach ( var query in new[] { "hey: a", "hey: -a", "hey: from:j", "@hey x" } )
        {
            var row = (await HeySearchAsync( provider, query )).Single( );
            Equal( "hey:state:too-short", row.Id );
            True( row.Subtitle!.Contains( "2 characters", StringComparison.Ordinal ), "the row says how much to type" );
        }

        Equal( 0, fake.Calls.Count );

        await HeySearchAsync( provider, "hey: ab" );
        await HeySearchAsync( provider, "hey: in:imbox" );
        await HeySearchAsync( provider, "hey: \"a b\"" );
        Equal( 3, fake.Calls.Count );
    }
}
