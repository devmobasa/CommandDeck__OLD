namespace CommandDeck;

// Product participation rules are separate from scheduling and final result filtering.
public static class ProviderEligibilityPolicy
{
    public static IEnumerable<ICommandProvider> Select(
        IReadOnlyList<ICommandProvider> providers,
        ParsedQuery query,
        LauncherPreferences preferences,
        string? herdrWorkspaceId )
    {
        var liveSourcesEnabled = preferences.Sources.Any( static source =>
            source.SourceId == SourceIds.WindowsWorkspaces && source.EnabledInSearch );

        // A disabled Herdr source never schedules the provider, even through an
        // explicit herdr: filter; the provider and final gate repeat the check.
        var herdrEnabled = preferences.Sources.Any( static source =>
            source.SourceId == SourceIds.Herdr && source.EnabledInSearch );
        var herdrExcluded = !herdrEnabled || !IncludesHerdr( query );
        var candidates = providers.Where( provider => !herdrExcluded || provider.Id != SearchCoordinator.HerdrProviderId );

        // HEY mail is searched only when asked for (hey:, mail:, or @hey); plain
        // words get one trailing "Search HEY for" row. A disabled HEY source stops both.
        var heyEnabled = preferences.Sources.Any( static source =>
            source.SourceId == SourceIds.Hey && source.EnabledInSearch );
        candidates = candidates.Where( provider => provider.Id != ProviderIds.Hey || (heyEnabled && (HeySearch.IsRequested( query ) || HeySearch.OffersFallback( query ))) );
        candidates = candidates.Where( provider => herdrWorkspaceId is null || provider.Id == SearchCoordinator.HerdrProviderId );

        var includedProviders = query.IncludedFilters
            .Select( static value => value.ProviderId )
            .Where( static value => !string.IsNullOrWhiteSpace( value ) )
            .Select( static value => value! )
            .Distinct( StringComparer.OrdinalIgnoreCase )
            .ToArray( );
        if ( includedProviders.Length > 0 )
        {
            return candidates.Where( provider =>
                includedProviders.Contains( provider.Id, StringComparer.OrdinalIgnoreCase ) );
        }
        if ( !string.IsNullOrWhiteSpace( query.ProviderFilter ) )
        {
            return candidates.Where( provider =>
                string.Equals( provider.Id, query.ProviderFilter, StringComparison.OrdinalIgnoreCase ) );
        }

        if ( query.IsCommandMode )
        {
            return candidates.Where( provider =>
                provider.Id is ProviderIds.Catalog or ProviderIds.Extensions or ProviderIds.DesktopControls ||
                (liveSourcesEnabled && provider.Id == ProviderIds.Hyprland) );
        }

        var applicationsIncluded = IncludesApplications( query );
        return candidates.Where( provider =>
            provider.Id != ProviderIds.Assistant &&
            (applicationsIncluded || provider.Id != SearchCoordinator.ApplicationFastPathProviderId) &&
            (query.HasExplicitFilter || liveSourcesEnabled || provider.Id != ProviderIds.Hyprland) );
    }

    // A negative herdr: filter, or -hspace: together with -htab:, discards every
    // Herdr row, so the two list processes are not worth starting.
    private static bool IncludesHerdr( ParsedQuery query )
    {
        var excluded = query.ExcludedFilters
            .Where( static filter => filter.SourceId == SourceIds.Herdr && filter.Id != HerdrSearch.AttentionFilterId )
            .ToArray( );
        if ( excluded.Any( static filter => filter.Kind is null ) )
        {
            return false;
        }

        return !(excluded.Any( static filter => filter.Kind == ResultKinds.Workspace ) &&
            excluded.Any( static filter => filter.Kind == ResultKinds.Tab ));
    }

    // The application-only catalog lookup only pays off when its results survive
    // the filter gate; a `file:` or `-app:` query would discard them.
    private static bool IncludesApplications( ParsedQuery query )
    {
        if ( query.IncludedFilters.Count > 0 &&
            !query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Applications ) )
        {
            return false;
        }

        return !query.ExcludedFilters.Any( static filter =>
            filter.SourceId == SourceIds.Applications &&
            filter.Kind is null or ResultKinds.Application );
    }

}
