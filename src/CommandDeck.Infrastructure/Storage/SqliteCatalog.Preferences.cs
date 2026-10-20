namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    public ValueTask<LauncherPreferences> GetPreferencesAsync(
        CancellationToken cancellationToken ) =>
        _launcherPreferences.GetPreferencesAsync( cancellationToken );

    public ValueTask<PreferencesSnapshot> GetSnapshotAsync(
        bool includeShortcuts,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.GetSnapshotAsync( includeShortcuts, cancellationToken );

    public ValueTask<LauncherPreferences> ReplacePreferencesAsync(
        LauncherPreferences preferences,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.ReplacePreferencesAsync( preferences, cancellationToken );
    public ValueTask<CustomizationResult> SetFavoriteAsync(
        SearchResult result,
        bool enabled,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.SetFavoriteAsync( result, enabled, cancellationToken );

    public ValueTask<CustomizationResult> SetAliasAsync(
        SearchResult result,
        string? aliasValue,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.SetAliasAsync( result, aliasValue, cancellationToken );

    public ValueTask<CustomizationResult> RemoveOrphanAsync(
        string customizationId,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.RemoveOrphanAsync( customizationId, cancellationToken );

    public ValueTask ReorderFavoritesAsync(
        IReadOnlyList<string> stableIds,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.ReorderFavoritesAsync( stableIds, cancellationToken );

    public ValueTask MoveFavoriteAsync(
        SearchResult result,
        SearchResult target,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.MoveFavoriteAsync( result, target, cancellationToken );

    public ValueTask<SearchResult?> FindAliasAsync(
        string aliasValue,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.FindAliasAsync( aliasValue, cancellationToken );
    public ValueTask<IReadOnlySet<string>> GetHiddenStableIdsAsync( CancellationToken cancellationToken ) =>
        _launcherPreferences.GetHiddenStableIdsAsync( cancellationToken );

    public ValueTask<CustomizationResult> SetHiddenAsync(
        SearchResult result,
        bool hidden,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.SetHiddenAsync( result, hidden, cancellationToken );

    public ValueTask<CustomizationResult> RestoreHiddenAsync(
        string customizationId,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.RestoreHiddenAsync( customizationId, cancellationToken );

    public ValueTask<HiddenItemsResult> ListHiddenAsync(
        string? query,
        int limit,
        int offset,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.ListHiddenAsync( query, limit, offset, cancellationToken );
    public ValueTask<IReadOnlyList<FilterAlias>> GetFilterAliasesAsync(
        CancellationToken cancellationToken ) =>
        _launcherPreferences.GetFilterAliasesAsync( cancellationToken );

    public ValueTask<IReadOnlyList<FilterAlias>> SetFilterAliasAsync(
        string aliasToken,
        string? filterId,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.SetFilterAliasAsync( aliasToken, filterId, cancellationToken );
    public ValueTask<IReadOnlyList<SavedSearch>> GetSavedSearchesAsync(
        CancellationToken cancellationToken ) =>
        _launcherPreferences.GetSavedSearchesAsync( cancellationToken );

    public ValueTask<IReadOnlyList<SavedSearch>> SaveSavedSearchAsync(
        SavedSearch search,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.SaveSavedSearchAsync( search, cancellationToken );

    public ValueTask<IReadOnlyList<SavedSearch>> DeleteSavedSearchAsync(
        string id,
        CancellationToken cancellationToken ) =>
        _launcherPreferences.DeleteSavedSearchAsync( id, cancellationToken );
    public ValueTask<LauncherPreferences> PatchSourcePreferencesAsync( SourcePreferencePatch patch, CancellationToken cancellationToken ) =>
        _launcherPreferences.PatchSourcePreferencesAsync( patch, cancellationToken );
}
