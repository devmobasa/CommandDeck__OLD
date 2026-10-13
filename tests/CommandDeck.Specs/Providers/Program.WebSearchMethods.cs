namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly List<WebEngineOptions> WebEngineFixture =
    [
        new WebEngineOptions
        {
            Id = "google",
            Name = "Google",
            Alias = "g",
            UrlTemplate = "https://www.google.com/search?q={query}"
        },
        new WebEngineOptions
        {
            Id = "duckduckgo",
            Name = "DuckDuckGo",
            Alias = "ddg",
            UrlTemplate = "https://duckduckgo.com/?q={query}"
        },
        new WebEngineOptions
        {
            Id = "internal",
            Name = "Internal",
            Alias = "int",
            UrlTemplate = "https://example.test/s?q={query}",
            IncludeInGlobal = false
        }
    ];

    private static WebSearchProvider WebSearchAt( ) => new( WebEngineFixture );

    private static void WebSearchConfigurationRejectsInvalidEngines( )
    {
        // The shipped defaults validate.
        ConfigurationValidator.Validate( new CommandDeckOptions( ) );

        // Missing {query} placeholder.
        Throws<InvalidOperationException>( ( ) => ValidateWithEngines( new WebEngineOptions
        {
            Id = "bad",
            Name = "Bad",
            Alias = "b",
            UrlTemplate = "https://example.test/search"
        } ) );

        // Malformed alias (contains a space).
        Throws<InvalidOperationException>( ( ) => ValidateWithEngines( new WebEngineOptions
        {
            Id = "spacey",
            Name = "Spacey",
            Alias = "b ad",
            UrlTemplate = "https://example.test/s?q={query}"
        } ) );

        // Alias that collides with built-in launcher syntax.
        Throws<InvalidOperationException>( ( ) => ValidateWithEngines( new WebEngineOptions
        {
            Id = "reserved",
            Name = "Reserved",
            Alias = "file",
            UrlTemplate = "https://example.test/s?q={query}"
        } ) );

        // Duplicate id.
        Throws<InvalidOperationException>( ( ) => ValidateWithEngines(
            new WebEngineOptions { Id = "dup", Name = "One", Alias = "one", UrlTemplate = "https://a.test/{query}" },
            new WebEngineOptions { Id = "dup", Name = "Two", Alias = "two", UrlTemplate = "https://b.test/{query}" } ) );

        // Duplicate alias (case-insensitive).
        Throws<InvalidOperationException>( ( ) => ValidateWithEngines(
            new WebEngineOptions { Id = "one", Name = "One", Alias = "x", UrlTemplate = "https://a.test/{query}" },
            new WebEngineOptions { Id = "two", Name = "Two", Alias = "X", UrlTemplate = "https://b.test/{query}" } ) );

        // A well-formed custom engine validates.
        ValidateWithEngines( new WebEngineOptions
        {
            Id = "kagi",
            Name = "Kagi",
            Alias = "k",
            UrlTemplate = "https://kagi.com/search?q={query}"
        } );
    }

    private static void ValidateWithEngines( params WebEngineOptions[] engines ) =>
        ConfigurationValidator.Validate( new CommandDeckOptions
        {
            Search = new SearchOptions { WebEngines = [.. engines] }
        } );

    private static async Task WebSearchProviderEmitsGlobalEnginesForBareQueryAsync( )
    {
        var provider = WebSearchAt( );

        var results = await provider.SearchAsync(
            QueryParser.Parse( "rust traits" ),
            new SearchRequest { Query = "rust traits", Limit = 20 },
            default );

        // Only the two global engines answer a bare query; the includeInGlobal:false
        // engine stays out of ambient results.
        Equal( 2, results.Count );
        True( results.All( result => result.SourceId == SourceIds.WebSearch ),
            "every row belongs to the web-search source" );
        True( results.All( result => result.Kind == ResultKinds.WebSearch ),
            "every row carries the web-search kind" );
        True( results.Any( result => result.Id == "web-search:google" ), "Google is offered" );
        True( results.Any( result => result.Id == "web-search:duckduckgo" ), "DuckDuckGo is offered" );
        True( results.All( result => !result.Metadata.ContainsKey( "exactAlias" ) ),
            "ambient rows are not exact-alias top matches" );

        var google = results.Single( result => result.Id == "web-search:google" );
        Equal( ActionKinds.OpenUrl, google.Actions[0].Kind );
        True( google.Actions[0].Arguments["urlTemplate"] ==
            "https://www.google.com/search?q=rust%20traits",
            "the bare-query URL substitutes the encoded text" );

        // A query that only targets another filter draws no web rows.
        Equal( 0, (await provider.SearchAsync(
            QueryParser.Parse( "file: rust" ),
            new SearchRequest { Query = "file: rust", Limit = 20 },
            default )).Count );

        // An empty query does nothing (off Home).
        Equal( 0, (await provider.SearchAsync(
            QueryParser.Parse( string.Empty ),
            new SearchRequest { Query = string.Empty, Limit = 20 },
            default )).Count );

        // No engines configured -> no work.
        Equal( 0, (await new WebSearchProvider( [] ).SearchAsync(
            QueryParser.Parse( "rust" ),
            new SearchRequest { Query = "rust", Limit = 20 },
            default )).Count );
    }

    private static async Task WebSearchProviderRoutesAliasToSingleEngineAsync( )
    {
        var provider = WebSearchAt( );

        var results = await provider.SearchAsync(
            QueryParser.Parse( "g: rust traits" ),
            new SearchRequest { Query = "g: rust traits", Limit = 20 },
            default );

        // Exactly the aliased engine answers, as an exact-alias top match.
        Equal( 1, results.Count );
        var google = results[0];
        Equal( "web-search:google", google.Id );
        Equal( SourceIds.WebSearch, google.SourceId );
        Equal( "g", google.Metadata["exactAlias"] );
        True( google.Title.Contains( "Google", StringComparison.Ordinal ), "the title names the engine" );
        Equal( "https://www.google.com/search?q=rust%20traits",
            google.Actions[0].Arguments["urlTemplate"] );

        // A colon-free space still routes ("g:rust").
        var tight = await provider.SearchAsync(
            QueryParser.Parse( "ddg:rust" ),
            new SearchRequest { Query = "ddg:rust", Limit = 20 },
            default );
        Equal( 1, tight.Count );
        Equal( "web-search:duckduckgo", tight[0].Id );
        Equal( "https://duckduckgo.com/?q=rust", tight[0].Actions[0].Arguments["urlTemplate"] );

        // A non-global engine is still reachable through its alias.
        var internalEngine = await provider.SearchAsync(
            QueryParser.Parse( "int: secret" ),
            new SearchRequest { Query = "int: secret", Limit = 20 },
            default );
        Equal( 1, internalEngine.Count );
        Equal( "web-search:internal", internalEngine[0].Id );

        // Percent-encoding covers reserved characters, not only spaces.
        var encoded = (await provider.SearchAsync(
            QueryParser.Parse( "g: c# && rust" ),
            new SearchRequest { Query = "g: c# && rust", Limit = 20 },
            default ))[0];
        True( encoded.Actions[0].Arguments["urlTemplate"].Contains(
            Uri.EscapeDataString( "c# && rust" ), StringComparison.Ordinal ),
            "the reserved characters are percent-encoded before substitution" );
        True( !encoded.Actions[0].Arguments["urlTemplate"].Contains( ' ', StringComparison.Ordinal ),
            "no raw spaces survive in the URL" );

        // An alias with no text yields nothing.
        Equal( 0, (await provider.SearchAsync(
            QueryParser.Parse( "g:" ),
            new SearchRequest { Query = "g:", Limit = 20 },
            default )).Count );

        // An unknown prefix is not an engine; it falls back to the global rows.
        Equal( 2, (await provider.SearchAsync(
            QueryParser.Parse( "zz: rust" ),
            new SearchRequest { Query = "zz: rust", Limit = 20 },
            default )).Count );
    }

    private static async Task WebSearchProviderStaysOffHomeAndWiresFilterAsync( )
    {
        // The base web: filter targets the web-search source and runs the provider.
        Equal( SourceIds.WebSearch, SearchFilterRegistry.FindByToken( "web" )!.SourceId );
        Equal( "web-search", SearchFilterRegistry.FindByToken( "web" )!.ProviderId );

        var provider = WebSearchAt( );
        var viaBaseFilter = await provider.SearchAsync(
            QueryParser.Parse( "web: rust" ),
            new SearchRequest { Query = "web: rust", Limit = 20 },
            default );
        Equal( 3, viaBaseFilter.Count );
        True( viaBaseFilter.All( result => result.SourceId == SourceIds.WebSearch ),
            "the base filter shows every configured engine" );

        var options = new SearchOptions( );
        SearchCoordinator coordinator = new( [new WebSearchProvider( options.WebEngines )], options );
        foreach ( var request in new[]
        {
            new SearchRequest { Query = "web: wayland layer shell" },
            new SearchRequest
            {
                Query = "wayland layer shell",
                Expression = new QueryExpression { IncludeFilterIds = ["web-search"] }
            }
        } )
        {
            var explicitResults = await coordinator.SearchAsync( request, default );

            Equal( options.WebEngines.Count, explicitResults.Count );
            True( explicitResults.Count > 0, "the shipped engines answer an explicit web search" );
            True( explicitResults.All( row => row.Actions.Single( ).Arguments["urlTemplate"]
                .Contains( "wayland%20layer%20shell", StringComparison.Ordinal ) ) );
        }

        // Off bare Home, on in search, by default.
        var preference = LauncherPreferences.CreateDefault( ).Sources
            .Single( source => source.SourceId == SourceIds.WebSearch );
        True( preference.EnabledInSearch, "web search participates in filtered search" );
        True( !preference.EnabledOnHome, "web search is off the bare Home surface" );
    }
}
