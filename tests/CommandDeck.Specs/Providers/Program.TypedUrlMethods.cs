namespace CommandDeck.Specs;

public static partial class Program
{
    private const string TypedUrlQuery = "github.com/devmobasa";
    private const string TypedUrlAddress = "https://github.com/devmobasa";

    private sealed class CapturingActionExecutor : IActionExecutor
    {
        public List<(CommandAction Action, string Query)> Calls { get; } = [];

        public ValueTask<ActionExecutionResult> ExecuteAsync(
            SearchResult result, CommandAction action, string query, CancellationToken cancellationToken )
        {
            Calls.Add( (action, query) );
            return ValueTask.FromResult( ActionExecutionResult.Success( "Accepted" ) );
        }
    }

    private static void TypedUrlDetectionAcceptsAddressesAndRejectsLookalikes( )
    {
        (string Text, string Url)[] accepted =
        [
            ("https://example.com/x", "https://example.com/x"),
            ("http://example.com", "http://example.com/"),
            ("HTTPS://Example.COM/Docs?q=1#top", "https://example.com/Docs?q=1#top"),
            ("  github.com/devmobasa  ", TypedUrlAddress),
            ("localhost:8080", "https://localhost:8080/"),
            ("localhost:3000/api/health", "https://localhost:3000/api/health"),
            ("www.example.com", "https://www.example.com/"),
            ("example.com:8443", "https://example.com:8443/"),
            ("192.168.1.1:8080", "https://192.168.1.1:8080/"),
            ("10.0.0.1/admin", "https://10.0.0.1/admin"),
            ("medium.com/@user", "https://medium.com/@user"),
            ("en.wikipedia.org/wiki/URL", "https://en.wikipedia.org/wiki/URL"),
            ("http://intranet/wiki", "http://intranet/wiki"),
            ("https://localhost", "https://localhost/"),
            ("https://[::1]:8080/", "https://[::1]:8080/")
        ];
        foreach ( var (text, expected) in accepted )
        {
            True( TypedUrl.TryParse( text, out var url ), $"'{text}' is a typed URL" );
            Equal( expected, url );
        }

        string?[] rejected =
        [
            null, "", "   ",
            // Decimals, versions, and file names, including real top-level domains.
            "1.5", "v1.2.3", "file.txt", "notes.md", "setup.py", "archive.tar.gz", "e.g.", "a.b/c",
            // A bare host with neither a port nor a path stays a search.
            "example.com", "localhost", "127.0.0.1", "1.2.3.4",
            // Emails and schemes other than http and https.
            "foo@example.com", "mailto:foo@example.com", "ftp://example.com/x", "file:///etc/passwd",
            "javascript:alert(1)", "about:blank", "chrome://settings",
            // Paths, times, and anything with spaces.
            "/usr/bin", "~/notes", "./foo", "docs/readme.md", "src/main.rs", "12:30", "C:\\Users",
            "github.com/devmobasa extra", "https://example.com/a b",
            // Hosts the system parser would reinterpret, and unsafe or invalid forms.
            "https://1.5", "https://127.1/", "https://0x7f.0.0.1/", "https://user:pw@example.com",
            "https://example.com:0/", "https://example.com:99999", "https://exa_mple.com", "https://-bad.com/",
            "https://example.com./x", "https://example.com/{query}", "https://bücher.de/", "[::1]:8080",
            "http://", "https://"
        ];
        foreach ( var text in rejected )
        {
            True( !TypedUrl.TryParse( text, out var url ) && url is null, $"'{text}' is not a typed URL" );
        }
    }

    private static async Task TypedUrlRowOpensAndCopiesTheAddressAsync( )
    {
        // Engines are optional: the row needs only the web-search source.
        foreach ( var provider in new[] { WebSearchAt( ), new WebSearchProvider( [] ) } )
        {
            var results = await provider.SearchAsync(
                QueryParser.Parse( TypedUrlQuery ),
                new SearchRequest { Query = TypedUrlQuery },
                default );

            var row = results[0];
            Equal( WebSearchProvider.TypedUrlResultId, row.Id );
            Equal( $"Open {TypedUrlAddress}", row.Title );
            Equal( ResultKinds.WebSearch, row.Kind );
            Equal( ProviderIds.WebSearch, row.ProviderId );
            Equal( SourceIds.WebSearch, row.SourceId );
            True( !row.CanCustomize, "a typed URL cannot be bookmarked, aliased, or hidden" );
            True( ResultPlacements.IsExplicit( row ) && !ResultPlacements.IsTrailing( row ) );
            True( !row.Metadata.ContainsKey( ResultMetadataKeys.ExactAlias ), "the row does not pose as an alias" );
            Equal( "open|copy-url", string.Join( '|', row.Actions.Select( static action => action.Id ) ) );

            // The open-url executor expands {query}; the typed address has none to expand.
            Equal( ActionKinds.OpenUrl, row.Actions[0].Kind );
            Equal( "Open URL", row.Actions[0].Title );
            Equal( TypedUrlAddress, UrlActionPayload.Read( row.Actions[0].Arguments ).Expand( TypedUrlQuery ) );
            Equal( ActionKinds.CopyText, row.Actions[1].Kind );
            Equal( "Copy URL", row.Actions[1].Title );
            Equal( TypedUrlAddress, TextActionPayload.Read( row.Actions[1].Arguments ).Text );
        }

        var engines = await WebSearchAt( ).SearchAsync(
            QueryParser.Parse( TypedUrlQuery ), new SearchRequest { Query = TypedUrlQuery }, default );
        Equal( "web-search:url|web-search:google|web-search:duckduckgo",
            string.Join( '|', engines.Select( static result => result.Id ) ) );

        // An engine alias still routes the address to that engine as search text.
        var aliased = await WebSearchAt( ).SearchAsync(
            QueryParser.Parse( "g: github.com/devmobasa" ),
            new SearchRequest { Query = "g: github.com/devmobasa" },
            default );
        Equal( "web-search:google", aliased.Single( ).Id );

        var words = await WebSearchAt( ).SearchAsync(
            QueryParser.Parse( "notes.md" ), new SearchRequest { Query = "notes.md" }, default );
        True( words.All( static result => result.Id != WebSearchProvider.TypedUrlResultId ) );
    }

    private static async Task TypedUrlRanksFirstButNeverAboveAnExactAliasAsync( )
    {
        var exactTitle = PromotionRow( "file:exact", TypedUrlQuery, ResultKinds.File, SourceIds.Files );
        var ordinary = PromotionRow( "app:github", "GitHub Desktop" );
        SearchCoordinator coordinator = new(
            [new WebSearchProvider( WebEngineFixture ), new StaticSearchProvider( [exactTitle, ordinary] )],
            new SearchOptions( ) );

        var results = await coordinator.SearchAsync( new SearchRequest { Query = TypedUrlQuery }, default );
        Equal( WebSearchProvider.TypedUrlResultId, results[0].Id );
        Equal( "Top Matches", results[0].Section!.Title );
        Equal( "explicit", results[0].Ranking.Placement!.Tier );
        Equal( "Explicit intent", results[0].Ranking.Placement!.Reason );
        Equal( exactTitle.Id, results[1].Id );
        Equal( "exact", results[1].Ranking.Placement!.Tier );
        True( results.Single( static result => result.Id == "app:github" ).Section!.Id == SourceIds.Applications,
            "an ordinary match keeps its source group" );

        // A real exact alias stays ahead, even when a source weight caps the
        // URL row at the alias score and the title order would favor the URL.
        var alias = PromotionRow( "app:zen", "Zen Browser", score: 1.4, metadata: new( StringComparer.Ordinal )
        {
            [ResultMetadataKeys.ExactAlias] = "www.example.com"
        } );
        foreach ( var weight in new[] { 1d, 5d } )
        {
            SearchOptions options = new( )
            {
                Ranking = new RankingOptions { SourceWeights = new( ) { [SourceIds.WebSearch] = weight } }
            };
            SearchCoordinator aliased = new(
                [new WebSearchProvider( WebEngineFixture ), new StaticSearchProvider( [alias] )], options );

            var ranked = await aliased.SearchAsync( new SearchRequest { Query = "www.example.com" }, default );
            Equal( "app:zen", ranked[0].Id );
            Equal( WebSearchProvider.TypedUrlResultId, ranked[1].Id );
            True( ranked.Take( 2 ).All( static result => result.Section!.Id == "top-match" ) );
            Equal( weight > 1, ranked[0].Score == ranked[1].Score );
        }

        // The legacy exact-only assembly keeps the row in its Top Match section.
        SearchCoordinator legacy = new(
            [new WebSearchProvider( WebEngineFixture ), new StaticSearchProvider( [alias] )],
            new SearchOptions { Ranking = new RankingOptions { TopMatches = new TopMatchesOptions { Enabled = false } } } );
        var rollback = await legacy.SearchAsync( new SearchRequest { Query = "www.example.com" }, default );
        Equal( "app:zen|web-search:url", string.Join( '|', rollback.Take( 2 ).Select( static result => result.Id ) ) );
        True( rollback.Take( 2 ).All( static result => result.Section!.Title == "Top Match" ) );
    }

    private static async Task TypedUrlFollowsTheWebSourceAndExplicitFiltersAsync( )
    {
        var database = Path.Combine( Path.GetTempPath( ), $"commanddeck-typed-url-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( CustomizationItems( includeFile: true ), null, default );
            var brave = (await catalog.SearchAsync( QueryParser.Parse( "Brave" ), 10, default ))
                .Single( static value => value.Kind == ResultKinds.Application );
            await catalog.SetAliasAsync( brave, "www.example.com", default );
            SearchCoordinator coordinator = new(
                [new CatalogSearchProvider( catalog, catalog ), new WebSearchProvider( WebEngineFixture )],
                new SearchOptions( ), catalog, catalog );

            var aliased = await coordinator.SearchAsync( new SearchRequest { Query = "www.example.com" }, default );
            Equal( brave.Id, aliased[0].Id );
            Equal( WebSearchProvider.TypedUrlResultId, aliased[1].Id );

            // web: keeps every engine after the typed URL; plain words get no URL row.
            var web = await coordinator.SearchAsync( new SearchRequest { Query = $"web: {TypedUrlQuery}" }, default );
            Equal( WebSearchProvider.TypedUrlResultId, web[0].Id );
            Equal( "web-search:duckduckgo|web-search:google|web-search:internal",
                string.Join( '|', web.Skip( 1 ).Select( static result => result.Id ).Order( StringComparer.Ordinal ) ) );
            var words = await coordinator.SearchAsync( new SearchRequest { Query = "web: rust" }, default );
            Equal( WebEngineFixture.Count, words.Count );
            True( words.All( static result => result.Id != WebSearchProvider.TypedUrlResultId ) );

            foreach ( var query in new[]
            {
                $"file: {TypedUrlQuery}", $"app: {TypedUrlQuery}", $"{TypedUrlQuery} -web:", $"title:{TypedUrlQuery}", $">{TypedUrlQuery}"
            } )
            {
                var filtered = await coordinator.SearchAsync( new SearchRequest { Query = query }, default );
                True( filtered.All( static result => result.Id != WebSearchProvider.TypedUrlResultId ),
                    $"'{query}' does not offer the typed URL" );
            }

            // Turning the Web Search source off in Settings removes the row.
            var preferences = await catalog.GetPreferencesAsync( default );
            await catalog.PatchSourcePreferencesAsync( new SourcePreferencePatch
            {
                RegistryRevision = preferences.RegistryRevision,
                PreferenceRevision = preferences.PreferenceRevision,
                Changes = [new SourcePreferenceChange { SourceId = SourceIds.WebSearch, EnabledInSearch = false }]
            }, default );
            var disabled = await coordinator.SearchAsync( new SearchRequest { Query = "www.example.com" }, default );
            Equal( brave.Id, disabled[0].Id );
            True( disabled.All( static result => result.SourceId != SourceIds.WebSearch ) );
            var plain = await coordinator.SearchAsync( new SearchRequest { Query = TypedUrlQuery }, default );
            True( plain.All( static result => result.SourceId != SourceIds.WebSearch ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static async Task TypedUrlInvocationStoresNoAddressAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-typed-url-usage-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            AcceptedUsageHistory history = new( catalog.UsageHistory );
            CapturingActionExecutor executor = new( );
            SearchCoordinator search = new( [new WebSearchProvider( options.Search.WebEngines )], options.Search );
            await using var runtime = InvocationFixture( options, catalog, history, executor, search );

            var row = (await runtime.SearchAsync( new SearchRequest { Query = TypedUrlQuery }, default ))[0];
            Equal( WebSearchProvider.TypedUrlResultId, row.Id );

            // Copying records nothing; opening records one use under the fixed id.
            True( (await runtime.InvokeAsync( row.Id, "copy-url", false, default )).Succeeded );
            Equal( 0, history.Writes );
            True( (await runtime.InvokeAsync( row.Id, "open", false, default )).Succeeded );
            var (action, query) = executor.Calls[^1];
            Equal( ActionKinds.OpenUrl, action.Kind );
            Equal( TypedUrlAddress, UrlActionPayload.Read( action.Arguments ).Expand( query ) );
            Equal( 1, history.Writes );
            Equal( WebSearchProvider.TypedUrlResultId, history.LastId );
            True( history.LastQuery is null, "query history is off, so the typed address is not kept" );

            var learning = await catalog.UsageHistory.GetRankingLearningAsync( 10, default );
            var stored = learning.Events.Single( );
            Equal( WebSearchProvider.TypedUrlResultId, stored.StableId );
            True( stored.Query is null, "the invocation event keeps no query text" );
            Equal( 0L, (await catalog.UsageHistory.GetQueryHistoryAsync( 10, default )).TotalEntries );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }
}
