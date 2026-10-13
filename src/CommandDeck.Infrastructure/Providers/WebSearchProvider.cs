namespace CommandDeck;

/// <summary>
/// Configurable named web-search engines (U3). Each engine renders a result
/// that opens its <c>urlTemplate</c> with the query percent-encoded, reusing the
/// same <see cref="ActionKinds.OpenUrl"/> plumbing (xdg-open) as quicklinks and
/// the web fallback - the URL is substituted here so no open machinery is
/// duplicated. Three shapes are understood:
/// <list type="bullet">
///   <item><c>&lt;alias&gt;: &lt;text&gt;</c> - routes to that one engine as an
///   exact-alias top match (e.g. <c>g: rust traits</c> opens Google).</item>
///   <item>a bare <c>&lt;text&gt;</c> query - one low-scored result per engine
///   whose <c>includeInGlobal</c> is set, so a plain search still offers the
///   web engines alongside catalog matches.</item>
///   <item><c>web: &lt;text&gt;</c> - one result per configured engine, including
///   engines kept out of global searches.</item>
/// </list>
/// The engine alias is recovered from the raw query, so routing is identical
/// whether or not the alias was registered as a dynamic filter for the parser.
/// Engine aliases are config-driven; nothing is hardcoded here. AOT-safe: no
/// reflection, and results carry only the (already public) destination URL.
/// </summary>
public sealed class WebSearchProvider : ICommandProvider
{
    private const string PlaceholderToken = "{query}";

    private readonly IReadOnlyList<WebEngineOptions> _engines;
    private readonly Dictionary<string, WebEngineOptions> _byAlias;

    public WebSearchProvider( IReadOnlyList<WebEngineOptions> engines )
    {
        ArgumentNullException.ThrowIfNull( engines );
        _engines = [.. engines];
        _byAlias = new Dictionary<string, WebEngineOptions>( StringComparer.OrdinalIgnoreCase );
        foreach ( var engine in _engines )
        {
            if ( !string.IsNullOrWhiteSpace( engine.Alias ) )
            {
                _byAlias[engine.Alias.Trim( ).TrimEnd( ':' )] = engine;
            }
        }
    }

    public string Id => ProviderIds.WebSearch;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( query );
        ArgumentNullException.ThrowIfNull( request );
        cancellationToken.ThrowIfCancellationRequested( );

        if ( _engines.Count == 0 )
        {
            return Empty;
        }

        // 1. Explicit engine prefix: "<alias>: <text>" targets exactly one engine.
        if ( TrySplitAlias( query.Original, out var alias, out var remainder ) &&
            _byAlias.TryGetValue( alias, out var engine ) )
        {
            var prefixed = remainder.Trim( );
            return prefixed.Length == 0
                ? Empty
                : Single( BuildResult( engine, prefixed, exact: true ) );
        }

        // 2. Explicit web searches include every engine; bare queries use global engines.
        //    A different explicit filter
        //    (file:, app:, tz:, ...) routes elsewhere and must not draw web rows.
        if ( query.HasExplicitFilter && !query.IncludesFilter( "web-search" ) )
        {
            return Empty;
        }

        var text = query.Text.Trim( );
        if ( text.Length == 0 )
        {
            return Empty;
        }

        List<SearchResult> results = [];
        foreach ( var candidate in _engines )
        {
            if ( candidate.IncludeInGlobal || query.IncludesFilter( SearchFilterRegistry.WebSearchFilterId ) )
            {
                results.Add( BuildResult( candidate, text, exact: false ) );
            }
        }
        return ValueTask.FromResult<IReadOnlyList<SearchResult>>( results );
    }

    private static SearchResult BuildResult( WebEngineOptions engine, string queryText, bool exact )
    {
        var url = engine.UrlTemplate.Replace(
            PlaceholderToken,
            Uri.EscapeDataString( queryText ),
            StringComparison.OrdinalIgnoreCase );

        // Stable, query-independent id so repeated use of an engine accrues usage
        // history under one key, like other results.
        var id = $"web-search:{engine.Id}";
        Dictionary<string, string> metadata = new( StringComparer.Ordinal )
        {
            ["engineId"] = engine.Id,
            ["alias"] = engine.Alias,
            ["url"] = url,
            [ResultMetadataKeys.CatalogResultId] = id
        };
        if ( exact )
        {
            // Reuse the exact-alias convention: an explicit engine query is the
            // top match, scored above ambient results by the coordinator.
            metadata[ResultMetadataKeys.ExactAlias] = engine.Alias;
        }

        return new SearchResult
        {
            Id = id,
            Kind = ResultKinds.WebSearch,
            Title = $"Search {engine.Name} for “{queryText}”",
            Subtitle = $"{engine.Name} · {engine.Alias}:",
            Icon = "internet-web-browser",
            ProviderId = ProviderIds.WebSearch,
            SourceId = SourceIds.WebSearch,
            Score = exact ? 1 : 0.1,
            CanCustomize = false,
            Section = new ResultSection { Id = "web-search", Title = "Web Search", Order = 10 },
            Details =
            [
                new DetailField("Engine", engine.Name),
                new DetailField("URL", url, Monospace: true)
            ],
            Metadata = metadata,
            Actions =
            [
                new CommandAction
                {
                    Id = "open",
                    Title = $"Search {engine.Name}",
                    Kind = ActionKinds.OpenUrl,
                    Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["urlTemplate"] = url
                    }
                }
            ]
        };
    }

    // Recover a leading "<token>:" prefix from the raw query. The token must be a
    // single whitespace-free run before the first colon; the remainder is the
    // engine's query text. Returns false for a bare query or a mid-word colon.
    private static bool TrySplitAlias( string original, out string alias, out string remainder )
    {
        alias = string.Empty;
        remainder = string.Empty;
        var trimmed = original.Trim( );
        var colon = trimmed.IndexOf( ':', StringComparison.Ordinal );
        if ( colon <= 0 )
        {
            return false;
        }
        var candidate = trimmed[..colon];
        if ( candidate.Any( char.IsWhiteSpace ) )
        {
            return false;
        }
        alias = candidate;
        remainder = trimmed[(colon + 1)..];
        return true;
    }

    private static ValueTask<IReadOnlyList<SearchResult>> Single( SearchResult result ) =>
        ValueTask.FromResult<IReadOnlyList<SearchResult>>( [result] );

    private static ValueTask<IReadOnlyList<SearchResult>> Empty =>
        ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
}
