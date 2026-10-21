namespace CommandDeck;

public sealed partial class SearchCoordinator
{
    public const string ApplicationFastPathProviderId = ProviderIds.ApplicationsFastPath;
    public const string HerdrProviderId = ProviderIds.Herdr;

    private static readonly string[] ApplicationContextKeys =
        ["desktopId", "startupWmClass", "applicationName"];
    private readonly IReadOnlyList<ICommandProvider> _providers;
    private readonly Dictionary<ICommandProvider, SemaphoreSlim> _providerSlots;
    private readonly SearchOptions _options;
    private readonly ICatalogQueryStore? _catalog;
    private readonly ILauncherSearchPreferences? _preferences;
    private readonly IUsageSignalStore? _usageHistory;
    private readonly TimeProvider _timeProvider;
    private readonly HistoryPolicy _history;
    private readonly IContributionRegistry? _contributions;
    private readonly Action<SearchProviderDiagnostic>? _providerDiagnostic;

    public Func<CancellationToken, ValueTask<IReadOnlyList<SearchResult>>>? HomeSupplement { get; set; }
    private string SourceLabel( string sourceId ) => _contributions?.Snapshot.Label( sourceId ) ?? LauncherSourceRegistry.Label( sourceId );

    public SearchCoordinator(
        IEnumerable<ICommandProvider> providers,
        SearchOptions options,
        ICatalogQueryStore? catalog = null,
        ILauncherSearchPreferences? preferences = null,
        IUsageSignalStore? usageHistory = null,
        TimeProvider? timeProvider = null,
        PrivacyOptions? privacy = null,
        IContributionRegistry? contributions = null,
        Action<SearchProviderDiagnostic>? providerDiagnostic = null,
        HistoryPolicy? history = null )
    {
        _providers = providers.ToArray( );
        _providerSlots = _providers.Distinct( ).ToDictionary( static provider => provider, static _ => new SemaphoreSlim( 2, 2 ) );
        _options = options;
        _catalog = catalog;
        _preferences = preferences;
        _usageHistory = usageHistory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _history = history ?? new HistoryPolicy( privacy, options );
        _contributions = contributions;
        _providerDiagnostic = providerDiagnostic;
    }

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken ) => SearchAsync( request, SearchPurpose.Interactive, cancellationToken );

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest request,
        SearchPurpose purpose,
        CancellationToken cancellationToken )
    {
        var outcome = await SearchWithOutcomeAsync( request, purpose, cancellationToken )
            .ConfigureAwait( false );
        return outcome.Results;
    }

    public async ValueTask<SearchOutcome> SearchWithOutcomeAsync(
        SearchRequest request,
        SearchPurpose purpose,
        CancellationToken cancellationToken )
    {
        var plan = await BuildPlanAsync( request, purpose, cancellationToken ).ConfigureAwait( false );
        var parsed = plan.Parsed;
        var preferences = plan.Preferences;
        var effectiveRequest = plan.EffectiveRequest;
        var limit = plan.Limit;

        if ( IsBareHome( parsed, request ) && _catalog is not null )
        {
            var home = await BuildHomeAsync( effectiveRequest, preferences, limit, cancellationToken )
                .ConfigureAwait( false );
            return new SearchOutcome(
                OfferRankingReset( home.Results.Where( static result => !PrivateSources.IsPrivate( result ) ), purpose ),
                home.IncompleteProviderCount );
        }

        var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        timeout.CancelAfter( ProviderDeadline( parsed ) );

        List<ProviderRun> runs = [];
        foreach ( var provider in plan.SelectedProviders )
        {
            var providerRequest = provider.Id == ProviderIds.Folders ? plan.FolderRequest : plan.ProviderRequest;
            runs.Add( ProviderRun.Start(
                this,
                provider,
                parsed,
                providerRequest,
                timeout.Token,
                cancellationToken ) );
        }

        var collection = await CollectWithinDeadlineAsync( runs, timeout, cancellationToken )
            .ConfigureAwait( false );
        var now = _timeProvider.GetUtcNow( );
        // Catalog paths already exclude hidden rows in SQL; this catches the
        // providers that never touch SQL (extension commands, launch variants)
        // before dedup, Top Matches, and section budgets can spend a slot on them.
        var hidden = await HiddenStableIdsAsync( cancellationToken ).ConfigureAwait( false );
        var candidates = collection.Batches.SelectMany( static batch => batch )
            .Where( result => purpose == SearchPurpose.Interactive || !PrivateSources.IsPrivate( result ) )
            .Where( result => IsAllowedResult( result, parsed, plan.EnabledSources, request ) )
            .Where( result => !hidden.Contains( UsageId( result ) ) ).ToArray( );
        var learnedQueries = UseTopMatches( request, parsed )
            ? await ReadLearnedQueriesAsync( effectiveRequest, purpose, now, cancellationToken ).ConfigureAwait( false )
            : new Dictionary<string, LearnedQuery>( StringComparer.Ordinal );
        var liveUsage = await ReadSearchUsageAsync( candidates, parsed,
            IsFolderBrowsing( request, parsed ) || request.OmarchyMenuId is not null,
            learnedQueries, cancellationToken ).ConfigureAwait( false );
        Dictionary<string, SearchResult> deduplicated = new( StringComparer.Ordinal );
        foreach ( var rawResult in candidates )
        {
            var result = ApplyLiveTimeAwareScore( rawResult, liveUsage, now, parsed );
            var scored = ApplyFinalScore( result, parsed, effectiveRequest );
            if ( !deduplicated.TryGetValue( scored.Id, out var existing ) ||
                Outranks( scored, existing ) )
            {
                deduplicated[scored.Id] = scored;
            }
        }

        IEnumerable<SearchResult> ranked = deduplicated.Values
            .OrderByDescending( static item => item.Score )
            .ThenBy( static item => ResultTitleOrderKey( item ), StringComparer.OrdinalIgnoreCase )
            .ThenBy( static item => item.Id, StringComparer.Ordinal );
        var collapsed = CollapseConfigurationResults( ranked ).ToArray( );

        // Trailing rows (such as "Search HEY for …") follow the ranked list and
        // take no Top Match or first-page slot.
        var trailing = collapsed.Where( ResultPlacements.IsTrailing ).Take( ResultPlacements.MaximumTrailing ).ToArray( );
        var ordinary = collapsed.Where( static result => !ResultPlacements.IsTrailing( result ) ).ToArray( );
        var results = request.OmarchyMenuId is not null
            ? AssembleOmarchyMenu( ordinary, limit )
            : UseTopMatches( request, parsed )
            ? AssembleTopMatches( ordinary, parsed, preferences, limit, liveUsage, learnedQueries, now )
            : AssembleSections( ordinary, parsed, preferences, limit );
        return new SearchOutcome( OfferRankingReset( [.. results, .. trailing], purpose ), collection.IncompleteProviderCount );
    }

    // Duplicate stable IDs arrive from the application-only and full catalog paths. A
    // clamped score can tie the fixed exact-alias score, so ties keep the alias copy
    // and its explicit-tier metadata.
    private static bool Outranks( SearchResult candidate, SearchResult existing ) =>
        candidate.Score > existing.Score ||
        (candidate.Score == existing.Score &&
            candidate.Metadata.ContainsKey( ResultMetadataKeys.ExactAlias ) &&
            !existing.Metadata.ContainsKey( ResultMetadataKeys.ExactAlias ));

    // An Omarchy submenu keeps the provider's order in one section, without Top
    // Matches or the per-source cap, so a long menu is listed whole.
    private static SearchResult[] AssembleOmarchyMenu( IReadOnlyList<SearchResult> results, int limit ) =>
        [.. results.Take( limit ).Select( static result => result.Section is null
            ? WithSection( result, new ResultSection { Id = OmarchyMenuSearch.SectionId, Title = "Omarchy", Order = 0 } )
            : result )];

    private static bool IsAllowedResult( SearchResult result, ParsedQuery query, HashSet<string> enabledSources, SearchRequest request )
    {
        // These sources have their own switches and are not listed in source preferences.
        if ( result.SourceId is not SourceIds.Calculator and not SourceIds.Status and not SourceIds.Ai &&
            !enabledSources.Contains( result.SourceId ) )
        {
            return false;
        }
        if ( query.IncludedFilters.Count > 0 &&
                    !query.IncludedFilters.Any( filter => FilterMatches( filter, result ) ) )
        {
            return false;
        }
        return HerdrSearch.MatchesScope( result, request.HerdrWorkspaceId ) &&
                    OmarchyMenuSearch.MatchesScope( result, request.OmarchyMenuId ) &&
                    !query.ExcludedFilters.Any( filter => FilterMatches( filter, result ) ) &&
                    ClausesMatch( query.Expression.Clauses, result );
    }

    private async ValueTask<IReadOnlySet<string>> HiddenStableIdsAsync( CancellationToken cancellationToken ) =>
        _preferences is null
            ? new HashSet<string>( StringComparer.Ordinal )
            : await _preferences.GetHiddenStableIdsAsync( cancellationToken ).ConfigureAwait( false );

    private IEnumerable<ICommandProvider> SelectProviders(
        ParsedQuery query, LauncherPreferences preferences, SearchRequest request, SearchPurpose purpose ) =>
        ProviderEligibilityPolicy.Select( _providers, query, preferences, request.HerdrWorkspaceId, request.OmarchyMenuId )
            .Where( provider => purpose == SearchPurpose.Interactive || !PrivateSources.ReadsPrivateContent( provider.Id ) );

    private bool UseTopMatches( SearchRequest request, ParsedQuery query ) =>
        _history.TopMatches && !IsFolderBrowsing( request, query ) && !HeySearch.IsRequested( query ) &&
        request.OmarchyMenuId is null &&
        query.Text.Length > 0;

    private static bool IsFolderBrowsing( SearchRequest request, ParsedQuery query ) =>
        request.BrowseDirectory is not null || query.ProviderFilter == "folders" ||
        query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Folders );

    private SearchRequest WithoutDisabledContext( SearchRequest request ) => new( )
    {
        Query = request.Query,
        Expression = request.Expression,
        HerdrWorkspaceId = request.HerdrWorkspaceId,
        OmarchyMenuId = request.OmarchyMenuId,
        Limit = request.Limit,
        ActiveApplication = _options.Ranking.Context.Enabled ? request.ActiveApplication : null,
        ActiveWorkspace = _options.Ranking.Context.Enabled ? request.ActiveWorkspace : null,
        CurrentDirectory = _options.Ranking.Context.Enabled ? request.CurrentDirectory : null,
        SelectedProject = _options.Ranking.Context.Enabled ? request.SelectedProject : null
    };
}
