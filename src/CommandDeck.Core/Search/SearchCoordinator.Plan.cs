namespace CommandDeck;

public sealed partial class SearchCoordinator
{
    // The plan freezes the inputs used for eligibility and ranking. Preferences,
    // aliases, and hidden IDs are separate store reads, not one atomic database snapshot.
    private sealed record SearchPlan(
        ParsedQuery Parsed,
        LauncherPreferences Preferences,
        SearchRequest EffectiveRequest,
        SearchRequest ProviderRequest,
        SearchRequest FolderRequest,
        IReadOnlyList<ICommandProvider> SelectedProviders,
        HashSet<string> EnabledSources,
        int Limit,
        long PreferenceRevision,
        long RegistryRevision );

    private async ValueTask<SearchPlan> BuildPlanAsync(
        SearchRequest request,
        SearchPurpose purpose,
        CancellationToken cancellationToken )
    {
        request.IsPrivacyClassified = false;
        request.ContainsPrivateQuery = false;
        var limit = Math.Clamp(
            request.Limit <= 0 ? _options.DefaultLimit : request.Limit,
            1,
            _options.MaximumLimit );
        var preferences = _preferences is null
            ? LauncherPreferences.CreateDefault( )
            : await _preferences.GetPreferencesAsync( cancellationToken ).ConfigureAwait( false );
        var registry = _contributions?.Snapshot;
        for ( var attempt = 0; registry is not null && preferences.RegistryRevision != registry.Revision; attempt++ )
        {
            if ( attempt == 3 )
            {
                throw new InvalidOperationException( "The source registry changed during search. Try again." );
            }

            preferences = await _preferences!.GetPreferencesAsync( cancellationToken ).ConfigureAwait( false );
            registry = _contributions?.Snapshot;
        }

        var filterAliases = _preferences is null
            ? []
            : await _preferences.GetFilterAliasesAsync( cancellationToken ).ConfigureAwait( false );
        var aliasMap = filterAliases.ToDictionary(
            static value => value.Alias,
            static value => value.FilterId,
            StringComparer.OrdinalIgnoreCase );
        foreach ( var engine in _options.WebEngines )
        {
            if ( !string.IsNullOrWhiteSpace( engine.Alias ) )
            {
                aliasMap.TryAdd( engine.Alias.Trim( ).TrimEnd( ':' ), SearchFilterRegistry.WebSearchFilterId );
            }
        }

        var parsed = QueryParser.Parse( request.Query, request.Expression, aliasMap, registry );
        request.ContainsPrivateQuery = PrivateSources.ReferencesPrivateSource( parsed );
        request.IsPrivacyClassified = true;

        var enabledSources = preferences.Sources
            .Where( static value => value.EnabledInSearch )
            .Select( static value => value.SourceId )
            .ToHashSet( StringComparer.Ordinal );
        var effectiveRequest = WithoutDisabledContext( request );
        var providerLimit = Math.Min( _options.MaximumLimit, Math.Max( limit, limit * 4 ) );
        var providerRequest = CreateProviderRequest( request, effectiveRequest, enabledSources,
            registry, providerLimit );
        var folderRequest = CreateProviderRequest( request, effectiveRequest, enabledSources,
            registry, request.Limit );
        var selected = SelectProviders( parsed, preferences, request, purpose ).ToArray( );

        return new SearchPlan( parsed, preferences, effectiveRequest, providerRequest,
            folderRequest, selected, enabledSources, limit, preferences.PreferenceRevision,
            registry?.Revision ?? 0 );
    }

    private static SearchRequest CreateProviderRequest(
        SearchRequest request,
        SearchRequest effectiveRequest,
        HashSet<string> enabledSources,
        ContributionRegistrySnapshot? registry,
        int limit ) => new( )
        {
            SearchOwner = request.SearchOwner,
            SearchGeneration = request.SearchGeneration,
            EnabledSearchSources = enabledSources.ToArray( ),
            ContributionSnapshot = registry,
            BrowseDirectory = request.BrowseDirectory,
            HerdrWorkspaceId = request.HerdrWorkspaceId,
            OmarchyMenuId = request.OmarchyMenuId,
            BrowseOffset = request.BrowseOffset,
            Query = request.Query,
            Expression = request.Expression,
            Limit = limit,
            ActiveApplication = effectiveRequest.ActiveApplication,
            ActiveWorkspace = effectiveRequest.ActiveWorkspace,
            CurrentDirectory = effectiveRequest.CurrentDirectory,
            SelectedProject = effectiveRequest.SelectedProject
        };
}
