namespace CommandDeck;

public sealed class CatalogSearchProvider : ICommandProvider
{
    private readonly ICatalogQueryStore _catalog;
    private readonly ILauncherSearchPreferences? _preferences;
    private readonly OmarchyMenuState? _omarchyState;

    public CatalogSearchProvider(
        ICatalogQueryStore catalog,
        ILauncherSearchPreferences? preferences = null,
        OmarchyMenuState? omarchyState = null )
    {
        _catalog = catalog;
        _preferences = preferences;
        _omarchyState = omarchyState;
    }

    public string Id => ProviderIds.Catalog;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        if ( request.EnabledSearchSources is { Count: 0 } )
        {
            return [];
        }

        var alias = await FindEligibleAliasAsync( query, request, cancellationToken ).ConfigureAwait( false );
        if ( alias?.Kind == ResultKinds.Quicklink )
        {
            return [alias];
        }

        var isApplicationInvocation = ApplicationInvocation.TryParse(
            query,
            out var invocation );
        if ( isApplicationInvocation &&
            invocation is not null &&
            alias?.Kind == ResultKinds.Application )
        {
            return [WithLaunchArguments( alias, invocation.Arguments )];
        }

        if ( !isApplicationInvocation || invocation is null )
        {
            var results = query.IncludedFilters.Count == 0 &&
                          query.ExcludedFilters.Count == 0 &&
                          query.Terms.Count > 0 &&
                          !query.HasExplicitFilter &&
                          (request.EnabledSearchSources is not null || _preferences is not null)
                    ? await SearchEnabledSourcesAsync( query, request, cancellationToken )
                        .ConfigureAwait( false )
                    : await _catalog.SearchAsync( query, request.Limit, cancellationToken )
                        .ConfigureAwait( false );
            IReadOnlyList<SearchResult> found = alias is null
                ? results
                : [alias, .. results.Where( result => result.Id != alias.Id )];
            return _omarchyState is null
                ? found
                : await _omarchyState.ApplyAsync( found, cancellationToken ).ConfigureAwait( false );
        }

        var applicationQuery = QueryParser.Parse( $"app: {invocation.ApplicationQuery}" );
        var applications = await _catalog.SearchAsync(
            applicationQuery,
            request.Limit,
            cancellationToken ).ConfigureAwait( false );
        return applications
            .Where( static result => result.Kind == ResultKinds.Application )
            .Select( result => WithLaunchArguments( result, invocation.Arguments ) )
            .ToArray( );
    }

    private async ValueTask<SearchResult?> FindEligibleAliasAsync( ParsedQuery query, SearchRequest request,
        CancellationToken cancellationToken )
    {
        if ( _preferences is null || query.Terms.Count == 0 )
        {
            return null;
        }

        var alias = await _preferences.FindAliasAsync( query.Terms[0], cancellationToken ).ConfigureAwait( false );
        if ( alias?.Kind == ResultKinds.Quicklink &&
            !await IsAliasEligibleAsync( alias, query, request, cancellationToken ).ConfigureAwait( false ) )
        {
            return null;
        }

        return alias;
    }

    private async ValueTask<bool> IsAliasEligibleAsync( SearchResult alias, ParsedQuery query, SearchRequest request,
        CancellationToken cancellationToken )
    {
        var enabled = request.EnabledSearchSources;
        if ( enabled is null )
        {
            var preferences = await _preferences!.GetPreferencesAsync( cancellationToken ).ConfigureAwait( false );
            enabled = preferences.Sources.Where( source => source.EnabledInSearch ).Select( source => source.SourceId ).ToArray( );
        }

        return enabled.Contains( alias.SourceId, StringComparer.Ordinal ) &&
            (query.IncludedFilters.Count == 0 || query.IncludedFilters.Any( filter => SearchFilterRegistry.Matches( filter, alias ) )) &&
            !query.ExcludedFilters.Any( filter => SearchFilterRegistry.Matches( filter, alias ) );
    }

    private async ValueTask<IReadOnlyList<SearchResult>> SearchEnabledSourcesAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        var enabledSources = request.EnabledSearchSources;
        if ( enabledSources is null )
        {
            var preferences = await _preferences!.GetPreferencesAsync( cancellationToken )
                .ConfigureAwait( false );
            enabledSources = preferences.Sources
                .Where( static value => value.EnabledInSearch )
                .Select( static value => value.SourceId )
                .ToArray( );
        }

        var sources = enabledSources
            .Where( static source => source is not SourceIds.WindowsWorkspaces and not SourceIds.Herdr )
            .Distinct( StringComparer.Ordinal )
            .ToArray( );
        if ( sources.Length == 0 )
        {
            return [];
        }

        var perSourceLimit = Math.Clamp(
            (request.Limit + sources.Length - 1) / sources.Length,
            8,
            32 );
        var initialLimit = Math.Clamp( perSourceLimit * sources.Length, 50, 500 );

        // Live-only sources (emoji, timezone, web search, snippets, ...) own no
        // catalog rows, so their FTS terms and supplement queries can never add a
        // row. Limits still come from every enabled source, so the rows and their
        // order stay exactly those of querying them all.
        var populated = await _catalog.GetPopulatedSourcesAsync( sources, cancellationToken )
            .ConfigureAwait( false );
        var catalogSources = sources.Where( populated.Contains ).ToArray( );
        if ( catalogSources.Length == 0 )
        {
            return [];
        }

        var initialBatch = await _catalog.SearchSourcesAsync(
            query,
            catalogSources,
            initialLimit,
            cancellationToken ).ConfigureAwait( false );
        var enabledSourceSet = sources.ToHashSet( StringComparer.Ordinal );
        var initial = initialBatch
            .Where( result => enabledSourceSet.Contains( result.SourceId ) )
            .ToArray( );
        var underrepresented = initialBatch.Count < initialLimit
            ? []
            : catalogSources
            .Where( source => initial.Count( result => result.SourceId == source ) < 8 )
            .ToArray( );
        if ( underrepresented.Length == 0 )
        {
            return initial;
        }

        var searches = underrepresented
            .Select( source => _catalog.SearchSourcesAsync(
                query,
                [source],
                perSourceLimit,
                cancellationToken ).AsTask( ) )
            .ToArray( );
        var supplements = await Task.WhenAll( searches )
            .ConfigureAwait( false );
        return initial
            .Concat( supplements.SelectMany( static batch => batch ) )
            .DistinctBy( static result => result.Id, StringComparer.Ordinal )
            .ToArray( );
    }

    internal static SearchResult WithLaunchArguments(
        SearchResult result,
        IReadOnlyList<string> arguments )
    {
        var actions = result.Actions
            .Select( action => action.Kind == ActionKinds.LaunchDesktop
                ? WithArguments( action, arguments )
                : action )
            .ToList( );

        var encodedArguments = LaunchArgumentCodec.Encode( arguments );
        Dictionary<string, string> metadata = new( result.Metadata, StringComparer.Ordinal )
        {
            [ResultMetadataKeys.CatalogResultId] = result.Id
        };
        return new SearchResult( result )
        {
            Id = StableId.Create( "invocation", $"{result.Id}\n{encodedArguments}" ),
            Subtitle = $"Open {string.Join( ' ', arguments )}",
            Actions = actions,
            Metadata = metadata
        };
    }

    private static CommandAction WithArguments(
        CommandAction action,
        IReadOnlyList<string> arguments )
    {
        Dictionary<string, string> values = new( action.Arguments, StringComparer.Ordinal )
        {
            ["launchArguments"] = LaunchArgumentCodec.Encode( arguments )
        };
        return new CommandAction
        {
            Id = action.Id,
            Title = "Open with Arguments",
            Kind = action.Kind,
            Arguments = values,
            Inputs = action.Inputs,
            Hidden = action.Hidden,
            IsDestructive = action.IsDestructive,
            RequiresConfirmation = action.RequiresConfirmation,
            CloseAfterAction = action.CloseAfterAction
        };
    }
}
