namespace CommandDeck;

/// <summary>
/// Lists one Omarchy submenu for a search scoped to it. It reads the parsed
/// menu rather than the catalog, so every entry is listed in Omarchy's order
/// whatever the catalog's candidate limits, and builds each row as the catalog
/// does: the same stable ID, actions and details, with the catalog's bookmark,
/// alias and usage state. Typed text filters the entries; Omarchy's live menu
/// state marks them as it marks catalog rows.
/// </summary>
public sealed class OmarchyMenuProvider : ICommandProvider
{
    public const int MaximumResults = 512;
    private const double BrowseBaseline = 0.36;
    private const double BrowseStep = 0.0001;
    private const double TermWeight = 0.9;

    private readonly Func<CancellationToken, ValueTask<OmarchyMenu>> _loadMenu;
    private readonly ICatalogQueryStore? _catalog;
    private readonly OmarchyMenuState? _state;

    public OmarchyMenuProvider(
        Func<CancellationToken, ValueTask<OmarchyMenu>> loadMenu,
        ICatalogQueryStore? catalog = null,
        OmarchyMenuState? state = null )
    {
        _loadMenu = loadMenu;
        _catalog = catalog;
        _state = state;
    }

    public string Id => ProviderIds.OmarchyMenu;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( query );
        ArgumentNullException.ThrowIfNull( request );

        var menuId = request.OmarchyMenuId;
        if ( menuId is null || request.EnabledSearchSources?.Contains( SourceIds.OmarchyControl, StringComparer.Ordinal ) != true )
        {
            return [];
        }

        var menu = await _loadMenu( cancellationToken ).ConfigureAwait( false );
        var children = menu.Children( menuId );
        if ( children is null || children.Count == 0 )
        {
            return [];
        }

        var states = _catalog is null
            ? new Dictionary<string, CatalogItemState>( StringComparer.Ordinal )
            : await _catalog.GetItemStatesAsync(
                ResultKinds.Omarchy,
                [.. children.Select( static child => child.Item.StableId )],
                cancellationToken ).ConfigureAwait( false );
        var clauses = MatchingClauses( query );
        ResultSection section = new( ) { Id = OmarchyMenuSearch.SectionId, Title = menu.Label( menuId ) ?? "Omarchy", Order = 0 };

        List<(SearchResult Result, int Index)> matches = [];
        for ( var index = 0; index < children.Count && matches.Count < MaximumResults; index++ )
        {
            var child = children[index];
            var state = states.GetValueOrDefault( child.Item.StableId );
            var score = clauses.Count == 0 ? BrowseScore( index ) : MatchScore( clauses, child, state );
            if ( score > 0 )
            {
                matches.Add( (new SearchResult( SqliteCatalog.ProjectIndexedItem( child.Item, score, state ) ) { Section = section }, index) );
            }
        }

        IReadOnlyList<SearchResult> ordered = [.. matches
            .OrderByDescending( static match => match.Result.Score )
            .ThenBy( static match => match.Index )
            .Select( static match => match.Result )];
        if ( _state is not null )
        {
            ordered = await _state.ApplyAsync( ordered, cancellationToken ).ConfigureAwait( false );
        }

        // Browsing keeps Omarchy's order; an unavailable entry is dimmed in place.
        return clauses.Count == 0
            ? [.. ordered.Select( static ( result, index ) => new SearchResult( result ) { Score = BrowseScore( index ) } )]
            : ordered;
    }

    // Positive unqualified clauses are matched here; field clauses and
    // exclusions are applied by the coordinator to every listed entry.
    private static List<QueryClause> MatchingClauses( ParsedQuery query )
    {
        if ( query.Text.Trim( ).Length == 0 )
        {
            return [];
        }

        return query.Expression.Clauses.Count == 0
            ? [new QueryClause { Text = query.Text.Trim( ) }]
            : [.. query.Expression.Clauses.Where( static clause => !clause.Excluded && clause.Field is null )];
    }

    private static double MatchScore( List<QueryClause> clauses, OmarchyMenuRow child, CatalogItemState? state )
    {
        var total = 0.0;
        foreach ( var clause in clauses )
        {
            var best = Score( clause.Text, child.Item.Title );
            foreach ( var term in child.Terms )
            {
                best = Math.Max( best, Score( clause.Text, term ) * TermWeight );
            }

            foreach ( var alias in state?.Aliases ?? [] )
            {
                best = Math.Max( best, Score( clause.Text, alias ) );
            }

            if ( best <= 0 )
            {
                return 0;
            }

            total += best;
        }

        return Math.Clamp( (total / clauses.Count) + (state?.UsageScore ?? 0), 0, 1.2 );
    }

    private static double Score( string text, string value ) =>
        text.Any( char.IsWhiteSpace ) ? FuzzyMatcher.ScorePhrase( text, value ) : FuzzyMatcher.Score( text, value );

    private static double BrowseScore( int index ) => BrowseBaseline - (index * BrowseStep);
}
