namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly DateTimeOffset HeyNow = new( 2026, 9, 22, 18, 0, 0, TimeSpan.Zero );

    // UTC keeps the short dates the same on every machine; the timestamp
    // follows Now, so elapsed time does too.
    private sealed class HeyClock( DateTimeOffset now ) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow( ) => Now;

        public override long GetTimestamp( ) => Now.UtcTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static SearchRequest HeyRequest( bool enabled = true, int limit = 50 ) => new( )
    {
        Query = "",
        Limit = limit,
        EnabledSearchSources = enabled ? [SourceIds.Hey, SourceIds.Applications] : [SourceIds.Applications]
    };

    private static HeyProvider HeyProviderFor( HeyFake fake, HeyClock? clock = null, bool installed = true ) =>
        new( new HeyClient( fake.Runner, installed ? static ( ) => HeyBinary : static ( ) => null, HeyEnvironment ),
            clock ?? new HeyClock( HeyNow ), TimeSpan.Zero );

    private static async Task<IReadOnlyList<SearchResult>> HeySearchAsync( HeyProvider provider, string query, SearchRequest? request = null ) =>
        await provider.SearchAsync( QueryParser.Parse( query ), request ?? HeyRequest( ), CancellationToken.None );

    private static async Task HeyProviderRunsOnlyWhenAskedAsync( )
    {
        HeyFake fake = new( );
        var provider = HeyProviderFor( fake );

        // Plain words only get the trailing "Search HEY for" row; it runs nothing.
        Equal( "hey:fallback", (await HeySearchAsync( provider, "invoice" )).Single( ).Id );
        Equal( 0, (await HeySearchAsync( provider, "-hey: invoice" )).Count );
        Equal( 0, (await HeySearchAsync( provider, "hey: invoice", HeyRequest( enabled: false ) )).Count );
        Equal( 0, fake.Calls.Count );

        True( (await HeySearchAsync( provider, "hey: invoice" )).Count > 0 );
        True( (await HeySearchAsync( provider, "mail: lunch" )).Count > 0 );
        True( (await HeySearchAsync( provider, "@hey receipts" )).Count > 0 );
        Equal( 3, fake.Calls.Count );
    }

    private static async Task HeyProviderProjectsThreadsInHeyOrderAsync( )
    {
        var rows = await HeySearchAsync( HeyProviderFor( new HeyFake( ) ), "hey: invoice" );

        Equal( "hey:topic:9001,hey:topic:9002,hey:state:more", string.Join( ',', rows.Select( static row => row.Id ) ) );
        True( rows[0].Score > rows[1].Score && rows[1].Score > rows[2].Score );
        True( rows.All( static row => !row.CanCustomize && row.SourceId == SourceIds.Hey && PrivateSources.IsPrivate( row ) ) );

        var first = rows[0];
        Equal( ResultKinds.Mail, first.Kind );
        Equal( "Invoice #2291 for September", first.Title );
        Equal( "Jane Cooper · 21 Sep · attached is the invoice for the retainer", first.Subtitle );
        Equal( HeyProvider.ThreadIcon, first.Icon );
        Equal( "From=Jane Cooper <jane@example.com>|Date=Mon 21 Sep 2026, 08:30|Preview=attached is the invoice for the retainer",
            string.Join( '|', first.Details.Select( static field => field.Label + "=" + field.Value ) ) );
        Equal( "9001", first.Metadata[HeyProvider.TopicIdMetadata] );
        Equal( "501", first.Metadata[HeyProvider.PostingIdMetadata] );
        Equal( "open,read,copy-link", string.Join( ',', first.Actions.Take( 3 ).Select( static action => action.Id ) ) );
        Equal( ActionKinds.HeyOpen, first.Actions[0].Kind );
        Equal( "https://app.hey.com/topics/9001", first.Actions[0].Arguments["appUrl"] );
        Equal( ActionKinds.HeyReadThread, first.Actions[1].Kind );
        Equal( "9001", first.Actions[1].Arguments["topicId"] );
        Equal( "https://app.hey.com/topics/9001", first.Actions[2].Arguments["text"] );

        Equal( ResultKinds.Status, rows[2].Kind );
        True( rows[2].Actions.Count == 0 && rows[2].Subtitle!.Contains( "from:", StringComparison.Ordinal ) );

        // The page limit trims threads and says there are more.
        var trimmed = await HeySearchAsync( HeyProviderFor( new HeyFake( ) ), "hey: invoice", HeyRequest( limit: 1 ) );
        Equal( "hey:topic:9001,hey:state:more", string.Join( ',', trimmed.Select( static row => row.Id ) ) );
    }

    private static async Task HeyProviderSendsRefinementsAsync( )
    {
        HeyFake fake = new( );
        var provider = HeyProviderFor( fake );

        await HeySearchAsync( provider, "hey: invoice from:\"Jane Cooper\" in:the-feed has:pdf date:30d -spam \"quarterly plan\" label:Receipts" );

        Equal( "search|--json|--exact=quarterly plan|--none=spam|--from=Jane Cooper|--in=feed|--date=last_30_days|--label=Receipts|--attachment=pdfs|--|invoice",
            string.Join( '|', fake.Calls[0].Args ) );

        // An unknown vocabulary value is explained before anything runs.
        var unknown = await HeySearchAsync( provider, "hey: in:outbox invoice" );
        Equal( "hey:state:refinement", unknown.Single( ).Id );
        True( unknown[0].Subtitle!.Contains( "in:outbox", StringComparison.Ordinal ) );
        Equal( 1, fake.Calls.Count );

        // So is a refinement HEY cannot run, instead of being dropped: a
        // negated one, one given two values, and a field of local search.
        foreach ( var (query, explained) in new[]
        {
            ("hey: invoice -from:jane", "HEY can't leave out from:jane; a minus only leaves out words."),
            ("hey: invoice from:jane from:bob", "HEY takes one from: value; keep from:jane or from:bob."),
            ("hey: title:invoice", "HEY has no title: refinement. Use subject:, from:, to:, label:, in:, has:, or date:."),
            ("hey: invoice -path:/tmp", "HEY has no path: refinement. Use subject:, from:, to:, label:, in:, has:, or date:.")
        } )
        {
            var row = (await HeySearchAsync( provider, query )).Single( );
            Equal( "hey:state:refinement", row.Id );
            Equal( explained, row.Subtitle );
        }

        Equal( 1, fake.Calls.Count );

        // The same value twice is one refinement.
        await HeySearchAsync( provider, "hey: invoice from:jane FROM:Jane" );
        Equal( "search|--json|--from=jane|--|invoice", string.Join( '|', fake.Calls[^1].Args ) );
    }

    private static async Task HeyProviderExplainsEveryStateAsync( )
    {
        HeyFake quiet = new( );
        var notInstalled = await HeySearchAsync( HeyProviderFor( quiet, installed: false ), "hey: invoice" );
        Equal( "hey:state:not-installed", notInstalled.Single( ).Id );
        Equal( ActionKinds.HeyInstall, notInstalled[0].Actions.Single( ).Kind );

        var home = await HeySearchAsync( HeyProviderFor( quiet ), "hey:" );
        Equal( "hey:state:hint", home[^1].Id );
        Equal( 0, quiet.Calls.Count );

        async Task<SearchResult> StateFor( ProcessResult result )
        {
            HeyFake fake = new( )
            {
                Respond = _ => result
            };
            return (await HeySearchAsync( HeyProviderFor( fake ), "hey: invoice" )).Single( );
        }

        var signedOut = await StateFor( HeyResult( HeySignedOutJson, 1 ) );
        Equal( "hey:state:signed-out", signedOut.Id );
        Equal( ActionKinds.HeySignIn, signedOut.Actions.Single( ).Kind );
        Equal( "hey:state:offline", (await StateFor( HeyResult( """{"ok":false,"error":"no route","code":"network"}""", 1 ) )).Id );
        Equal( "hey:state:rate-limited", (await StateFor( HeyResult( """{"ok":false,"error":"slow","code":"rate_limit"}""", 1 ) )).Id );
        Equal( "hey:state:timed-out", (await StateFor( new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", TimedOut = true } )).Id );
        Equal( "hey:state:failed", (await StateFor( HeyResult( "", 3 ) )).Id );
        Equal( "hey:state:no-matches", (await StateFor( HeyResult( """{"ok":true,"data":[]}""" ) )).Id );

        var rejected = await StateFor( HeyResult( """{"ok":false,"error":"bad --date value","code":"usage"}""", 1 ) );
        Equal( "hey:state:rejected", rejected.Id );
        Equal( "bad --date value", rejected.Subtitle );
    }

    private static async Task HeyProviderCachesOnlySuccessBrieflyAsync( )
    {
        HeyFake fake = new( );
        HeyClock clock = new( HeyNow );
        var provider = HeyProviderFor( fake, clock );

        await HeySearchAsync( provider, "hey: invoice" );
        await HeySearchAsync( provider, "hey:   invoice" );
        Equal( 1, fake.Calls.Count );

        clock.Now = HeyNow + HeyProvider.CacheLifetime + TimeSpan.FromSeconds( 1 );
        await HeySearchAsync( provider, "hey: invoice" );
        Equal( 2, fake.Calls.Count );

        // A failure is never replayed: signing in must take effect at once.
        HeyFake signedOut = new( )
        {
            Respond = _ => HeyResult( HeySignedOutJson, 1 )
        };
        var retrying = HeyProviderFor( signedOut, clock );
        await HeySearchAsync( retrying, "hey: invoice" );
        await HeySearchAsync( retrying, "hey: invoice" );
        Equal( 2, signedOut.Calls.Count );
    }

    // Each keystroke replaces the search; the pause lets the replaced one end
    // before hey ever starts.
    private static async Task HeyProviderPausesForTypingAsync( )
    {
        HeyFake fake = new( );
        HeyProvider provider = new( new HeyClient( fake.Runner, static ( ) => HeyBinary, HeyEnvironment ),
            new HeyClock( HeyNow ), TimeSpan.FromSeconds( 5 ) );
        using CancellationTokenSource replaced = new( TimeSpan.FromMilliseconds( 50 ) );

        await ThrowsAsync<OperationCanceledException>( async ( ) =>
            await provider.SearchAsync( QueryParser.Parse( "hey: invoice" ), HeyRequest( ), replaced.Token ) );

        Equal( 0, fake.Calls.Count );
    }

    private sealed class SlowHeyFake
    {
        public HeyFake Inner { get; } = new( );

        public BoundedProcessRunner Runner => async ( file, args, timeout, limits, token ) =>
        {
            await Task.Delay( TimeSpan.FromMilliseconds( 800 ), token );
            return await Inner.Runner( file, args, timeout, limits, token );
        };
    }

    // HEY answers over the network, so a hey: search outlives the local
    // provider deadline; its rows keep HEY's order and the query is private.
    private static async Task HeyCoordinatorKeepsHeyOrderAndPrivacyAsync( )
    {
        SlowHeyFake slow = new( );
        HeyProvider provider = new( new HeyClient( slow.Runner, static ( ) => HeyBinary, HeyEnvironment ), new HeyClock( HeyNow ), TimeSpan.Zero );
        SearchCoordinator search = new( [provider], new SearchOptions { ProviderTimeoutMilliseconds = 350 } );

        // The second thread's subject is an exact match for its query, which
        // would promote any other row to Top Match.
        SearchRequest request = new( )
        {
            Query = "hey: (no subject)"
        };
        var rows = await search.SearchAsync( request, CancellationToken.None );

        Equal( "hey:topic:9001,hey:topic:9002,hey:state:more", string.Join( ',', rows.Select( static row => row.Id ) ) );
        True( rows.All( static row => row.Section?.Id == SourceIds.Hey && row.Section.Title == "HEY" ) );
        True( request.ContainsPrivateQuery );

        Equal( "hey:fallback", (await search.SearchAsync( new SearchRequest { Query = "invoice" }, CancellationToken.None )).Single( ).Id );
        Equal( 1, slow.Inner.Calls.Count );
    }

    private static void HeySearchesCannotBeSaved( )
    {
        var exception = CaptureInvalidOperation( ( ) => SavedSearchValidator.ValidateExpression( new QueryExpression { IncludeFilterIds = [HeySearch.FilterId] } ) );
        Equal( "HEY searches cannot be saved.", exception?.Message );

        // mail: is the same filter, so it is private too.
        var parsed = QueryParser.Parse( "mail: invoice" );
        True( HeySearch.IsRequested( parsed ) && PrivateSources.ReferencesPrivateSource( parsed ) );
        Equal( HeySearch.FilterId, parsed.IncludedFilters.Single( ).Id );
    }

    private static InvalidOperationException? CaptureInvalidOperation( Action action )
    {
        try
        {
            action( );
            return null;
        }
        catch ( InvalidOperationException exception )
        {
            return exception;
        }
    }
}
