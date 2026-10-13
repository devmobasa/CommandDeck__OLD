
namespace CommandDeck;

public sealed partial class SearchCoordinator
{

    private async ValueTask<SearchOutcome> BuildHomeAsync(
        SearchRequest request,
        LauncherPreferences preferences,
        int limit,
        CancellationToken cancellationToken )
    {
        // The catalog rows and the live Windows & Workspaces and Herdr sections do
        // not depend on each other, so they run together and Home waits for the
        // slowest instead of their sum. The live providers start first: they run
        // on worker threads, while the catalog query completes synchronously.
        // Rows are still assembled in section order.
        var windowSection = ReadWindowHomeSectionAsync( request, preferences, cancellationToken );
        var herdrSection = ReadHerdrHomeSectionAsync( preferences, cancellationToken );
        var catalogRows = _catalog!.GetHomeResultsAsync( preferences, limit, cancellationToken ).AsTask( );
        await Task.WhenAll( catalogRows, windowSection, herdrSection ).ConfigureAwait( false );

        List<SearchResult> home = [.. await catalogRows.ConfigureAwait( false )];
        if ( _preferences is not null )
        {
            var savedSearches = await _preferences.GetSavedSearchesAsync(
                cancellationToken ).ConfigureAwait( false );
            home.AddRange( savedSearches
                .Where( static value => value.ShowOnHome )
                .Select( static value => new SearchResult
                {
                    Id = value.Id,
                    Kind = ResultKinds.SavedSearch,
                    Title = value.Name,
                    Subtitle = value.Shortcut is null
                        ? "Saved search"
                        : $"Saved search · {value.Shortcut}",
                    Icon = "system-search",
                    ProviderId = ProviderIds.SavedSearch,
                    SourceId = SourceIds.CommandsQuicklinks,
                    Section = new ResultSection
                    {
                        Id = "saved-searches",
                        Title = "Saved Searches",
                        Order = 0
                    },
                    SavedSearch = value,
                    Actions =
                    [
                        new CommandAction
                        {
                            Id = "apply",
                            Title = "Apply Search",
                            Kind = ActionKinds.ApplySavedSearch
                        }
                    ]
                } ) );
        }
        var ids = home.Select( static value => value.Id ).ToHashSet( StringComparer.Ordinal );

        var windows = await windowSection.ConfigureAwait( false );
        AddHomeSection( home, ids, windows.Results, new ResultSection
        {
            Id = "windows-workspaces",
            Title = "Windows & Workspaces",
            Order = 3
        } );
        var herdr = await herdrSection.ConfigureAwait( false );
        AddHomeSection( home, ids, herdr.Results, new ResultSection { Id = "herdr", Title = "Herdr", Order = 3 } );
        var incompleteProviderCount = windows.IncompleteProviderCount + herdr.IncompleteProviderCount;

        var omarchyPreference = preferences.Sources.FirstOrDefault( static value =>
            value.SourceId == SourceIds.OmarchyControl );
        if ( omarchyPreference?.EnabledOnHome == true )
        {
            var omarchy = await _catalog.SearchAsync(
                QueryParser.Parse( "oma:" ),
                8,
                cancellationToken ).ConfigureAwait( false );
            foreach ( var result in omarchy
                .Where( static value => value.Id == "omarchy-route:root" )
                .Take( 1 ) )
            {
                if ( ids.Add( result.Id ) )
                {
                    home.Add( WithSection(
                        result,
                        new ResultSection { Id = "omarchy", Title = "Omarchy", Order = 4 } ) );
                }
            }
        }

        if ( HomeSupplement is { } supplement )
        {
            foreach ( var result in await supplement( cancellationToken ).ConfigureAwait( false ) )
            {
                if ( ids.Add( result.Id ) )
                {
                    home.Add( result );
                }
            }
        }

        // SQL-backed Home rows are already scoped; the supplement and live rows
        // are not, so the hidden set is applied once more before the budget.
        var hidden = await HiddenStableIdsAsync( cancellationToken ).ConfigureAwait( false );
        return new SearchOutcome(
            home.Where( value => !hidden.Contains( UsageId( value ) ) )
                .OrderBy( static value => value.Section?.Order ?? int.MaxValue )
                .Take( limit )
                .ToArray( ),
            incompleteProviderCount );
    }

    private static void AddHomeSection(
        List<SearchResult> home,
        HashSet<string> ids,
        IReadOnlyList<SearchResult> results,
        ResultSection section )
    {
        foreach ( var result in results )
        {
            if ( ids.Add( result.Id ) )
            {
                home.Add( WithSection( result, section ) );
            }
        }
    }

    private async Task<SearchOutcome> ReadWindowHomeSectionAsync(
        SearchRequest request,
        LauncherPreferences preferences,
        CancellationToken cancellationToken )
    {
        var livePreference = preferences.Sources.FirstOrDefault( static value =>
            value.SourceId == SourceIds.WindowsWorkspaces );
        var hyprland = _providers.FirstOrDefault( static value => value.Id == ProviderIds.Hyprland );
        if ( livePreference?.EnabledOnHome != true || hyprland is null )
        {
            return new SearchOutcome( [], 0 );
        }

        var live = await RunProviderAsync(
            hyprland,
            QueryParser.Parse( string.Empty ),
            new SearchRequest
            {
                Limit = 4,
                ActiveApplication = request.ActiveApplication,
                ActiveWorkspace = request.ActiveWorkspace,
                CurrentDirectory = request.CurrentDirectory,
                SelectedProject = request.SelectedProject
            },
            cancellationToken ).ConfigureAwait( false );
        var liveUsage =
            await ReadLiveUsageAsync( live.Results, cancellationToken ).ConfigureAwait( false );

        return new SearchOutcome(
            [.. live.Results.Take( 4 ).Select( result => ApplyLiveTimeAwareScore( result, liveUsage ) )],
            live.IncompleteProviderCount );
    }

    // Off by default; only an explicit Home preference runs the Herdr inventory
    // on the launcher-open path, and it is bounded to four rows.
    private async Task<SearchOutcome> ReadHerdrHomeSectionAsync(
        LauncherPreferences preferences,
        CancellationToken cancellationToken )
    {
        var preference = preferences.Sources.FirstOrDefault( static value =>
            value.SourceId == SourceIds.Herdr );
        var herdr = _providers.FirstOrDefault( static value => value.Id == HerdrProviderId );
        if ( preference?.EnabledOnHome != true || herdr is null )
        {
            return new SearchOutcome( [], 0 );
        }

        var live = await RunProviderAsync(
            herdr,
            QueryParser.Parse( string.Empty ),
            new SearchRequest
            {
                Limit = 4,
                EnabledSearchSources = [SourceIds.Herdr]
            },
            cancellationToken ).ConfigureAwait( false );

        return new SearchOutcome( [.. live.Results.Take( 4 )], live.IncompleteProviderCount );
    }
}
