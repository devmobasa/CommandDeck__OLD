namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{

    public ValueTask<PreferencesSnapshot> GetPreferencesAsync(
        bool includeShortcuts,
        CancellationToken cancellationToken ) =>
        _preferences.GetSnapshotAsync( includeShortcuts, cancellationToken );

    public ValueTask<LauncherPreferences> UpdatePreferencesAsync(
        LauncherPreferences preferences,
        CancellationToken cancellationToken )
    {
        if ( preferences.WebFallbackQuicklinkId is not null &&
            !_options.Quicklinks.Any( value =>
                string.Equals( value.Id, preferences.WebFallbackQuicklinkId, StringComparison.Ordinal ) ) )
        {
            throw new InvalidOperationException( "The selected fallback quicklink is not configured." );
        }

        return _preferences.ReplacePreferencesAsync( preferences, cancellationToken );
    }

    public ValueTask<CustomizationResult> SetFavoriteAsync(
        string resultId,
        bool enabled,
        CancellationToken cancellationToken )
    {
        var result = GetCustomizableCachedResult( resultId );
        return _preferences.SetFavoriteAsync( result, enabled, cancellationToken );
    }

    public ValueTask<CustomizationResult> SetAliasAsync(
        string resultId,
        string? alias,
        CancellationToken cancellationToken )
    {
        var result = GetCustomizableCachedResult( resultId );
        return _preferences.SetAliasAsync( result, alias, cancellationToken );
    }

    public async ValueTask<FilterAliasResult> SetFilterAliasAsync(
        string aliasToken,
        string? filterId,
        CancellationToken cancellationToken )
    {
        var aliases = await _preferences.SetFilterAliasAsync(
            aliasToken,
            filterId,
            cancellationToken ).ConfigureAwait( false );
        return new FilterAliasResult
        {
            Aliases = [.. aliases],
            Filters = [.. Contributions.WithAliases( aliases )],
            Message = filterId is null ? "Filter alias removed" : "Filter alias saved"
        };
    }

    public async ValueTask<SavedSearchResult> GetSavedSearchesAsync(
        CancellationToken cancellationToken ) => new( )
        {
            Searches = [.. await _preferences.GetSavedSearchesAsync(cancellationToken)
                .ConfigureAwait(false)]
        };

    public async ValueTask<SavedSearchResult> SaveSavedSearchAsync(
        SavedSearch search,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( search );
        var existing = await _preferences.GetSavedSearchesAsync(
            cancellationToken ).ConfigureAwait( false );
        var inventory = string.IsNullOrWhiteSpace( search.Shortcut )
            ? new ShortcutInventory { Available = true }
            : await HyprlandShortcutInspector.InspectInventoryAsync( cancellationToken )
                .ConfigureAwait( false );
        SavedSearchValidator.EnsureShortcutAvailable(
            search.Shortcut, search.Id, existing, inventory );
        return new SavedSearchResult
        {
            Searches = [.. await _preferences.SaveSavedSearchAsync(search, cancellationToken)
                .ConfigureAwait(false)],
            Message = "Saved search stored"
        };
    }

    public async ValueTask<SavedSearchResult> DeleteSavedSearchAsync(
        string id,
        CancellationToken cancellationToken ) => new( )
        {
            Searches = [.. await _preferences.DeleteSavedSearchAsync(id, cancellationToken)
                .ConfigureAwait(false)],
            Message = "Saved search deleted"
        };

    public ValueTask<CustomizationResult> RemoveOrphanAsync(
        string customizationId,
        CancellationToken cancellationToken ) =>
        _preferences.RemoveOrphanAsync( customizationId, cancellationToken );

    // Hide resolves the target from the daemon-cached result, exactly like
    // bookmarks: the client cannot name a path or title of its own.
    public ValueTask<CustomizationResult> SetHiddenAsync(
        string resultId,
        bool hidden,
        CancellationToken cancellationToken )
    {
        var result = GetCustomizableCachedResult( resultId );
        return _preferences.SetHiddenAsync( result, hidden, cancellationToken );
    }

    public ValueTask<CustomizationResult> RestoreHiddenAsync(
        string customizationId,
        CancellationToken cancellationToken ) =>
        _preferences.RestoreHiddenAsync( customizationId, cancellationToken );

    public ValueTask<HiddenItemsResult> ListHiddenAsync(
        string? query,
        int limit,
        int offset,
        CancellationToken cancellationToken ) =>
        _preferences.ListHiddenAsync( query, limit, offset, cancellationToken );

    public ValueTask ReorderFavoritesAsync(
        IReadOnlyList<string> stableIds,
        CancellationToken cancellationToken ) =>
        _preferences.ReorderFavoritesAsync( stableIds, cancellationToken );

    public ValueTask<CustomizationExport> ExportCustomizationsAsync(
        CancellationToken cancellationToken ) =>
        _preferences.ExportAsync( cancellationToken );

    // The assistant's authority is reset first, through its owner; launcher
    // storage no longer touches the assistant's records.
    public async ValueTask<LauncherPreferences> ResetCustomizationsAsync(
        CancellationToken cancellationToken )
    {
        await Assistant.ResetAuthorityAsync( cancellationToken ).ConfigureAwait( false );
        return await _preferences.ResetAsync( cancellationToken ).ConfigureAwait( false );
    }
}
