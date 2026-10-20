namespace CommandDeck;

public interface ICommandProvider
{
    string Id { get; }

    ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken );
}

public interface ICatalogQueryStore
{
    ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        int limit,
        CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<SearchResult>> SearchSourcesAsync(
        ParsedQuery query,
        IReadOnlyList<string> sourceIds,
        int limit,
        CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<SearchResult>> GetHomeResultsAsync(
        LauncherPreferences preferences,
        int limit,
        CancellationToken cancellationToken );

    // The given sources that own at least one catalog row now. Emoji, timezone,
    // web search and other live-only sources never do, so callers need not query
    // the catalog for them. A store that cannot tell reports every source.
    ValueTask<IReadOnlySet<string>> GetPopulatedSourcesAsync(
        IReadOnlyList<string> sourceIds,
        CancellationToken cancellationToken ) =>
        ValueTask.FromResult<IReadOnlySet<string>>( sourceIds.ToHashSet( StringComparer.Ordinal ) );

    // Bookmark, alias and accepted-use state for rows of one kind that a
    // provider builds from their own source under catalog stable IDs. Usage is
    // scored as a catalog search scores that kind. IDs without state are absent.
    ValueTask<IReadOnlyDictionary<string, CatalogItemState>> GetItemStatesAsync(
        string kind,
        IReadOnlyList<string> stableIds,
        CancellationToken cancellationToken ) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, CatalogItemState>>(
            new Dictionary<string, CatalogItemState>( StringComparer.Ordinal ) );
}

public sealed record CatalogItemState( bool IsFavorite, List<string> Aliases, double UsageScore );

public interface ICatalogProjectionStore
{
    ValueTask InitializeAsync( CancellationToken cancellationToken );
    ValueTask ReplaceAllAsync(
        IAsyncEnumerable<IndexedItem> items,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken );
    ValueTask ReplaceGenerationAsync(
        IAsyncEnumerable<IndexedItem> items,
        Func<IReadOnlyList<IndexExclusionDiagnostic>> diagnostics,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken );
    ValueTask ReplaceGenerationAsync(
        IAsyncEnumerable<IndexedItem> items,
        Func<IReadOnlyList<IndexExclusionDiagnostic>> diagnostics,
        Func<IReadOnlyList<IndexWorkItem>> workItems,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken );
    ValueTask<CatalogStats> GetStatsAsync( CancellationToken cancellationToken );
    ValueTask<bool> VerifyFts5Async( CancellationToken cancellationToken );
    ValueTask<bool> RequiresReindexAsync( CancellationToken cancellationToken );
    ValueTask ApplyPathChangesAsync(
        IReadOnlyList<IndexedPathChange> changes,
        CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<string>> GetPathsRequiringExtractorVersionAsync(
        string extractorVersion,
        int limit,
        CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<string>> GetIndexedPathsAfterAsync(
        string? afterPath,
        int limit,
        CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<IndexedFileState>> GetIndexedFileStatesAsync(
        string rootPath,
        CancellationToken cancellationToken );
    ValueTask ReplaceExclusionDiagnosticsAsync(
        IReadOnlyList<IndexExclusionDiagnostic> diagnostics,
        CancellationToken cancellationToken );
}

public interface IUsageSignalStore
{
    ValueTask<IReadOnlyDictionary<string, UsageSignal>> GetUsageSignalsAsync(
        IReadOnlyList<string> stableIds, CancellationToken cancellationToken );
}

public interface ILearnedQueryStore
{
    // Stable IDs accepted after a search matching this query since the given
    // time, with the best match for each; empty while query history is off.
    ValueTask<IReadOnlyDictionary<string, LearnedQuery>> GetLearnedQueriesAsync(
        string query, long sinceUnixMilliseconds, CancellationToken cancellationToken );
}

public interface IQueryHistoryStore
{
    ValueTask<QueryHistorySnapshot> GetQueryHistoryAsync( int limit, CancellationToken cancellationToken );
    ValueTask<bool> DeleteQueryHistoryAsync( long id, CancellationToken cancellationToken );
    ValueTask ResetQueryHistoryAsync( CancellationToken cancellationToken );
}

public interface IUsageHistoryStore : IUsageSignalStore, IQueryHistoryStore
{
    ValueTask RecordInvocationAsync(
        string resultId,
        string actionId,
        string? query,
        CancellationToken cancellationToken );
    ValueTask<RankingLearningSnapshot> GetRankingLearningAsync(
        int limit,
        CancellationToken cancellationToken );
    ValueTask ResetRankingLearningAsync( CancellationToken cancellationToken );
    // Forgets one stable ID's accepted uses; false when it had none.
    ValueTask<bool> ResetRankingForAsync( string stableId, CancellationToken cancellationToken );
}

public interface ISearchCatalog :
    ICatalogQueryStore,
    ICatalogProjectionStore,
    IAsyncDisposable;

public interface ICatalogOperations
{
    ValueTask<CatalogInspection> InspectAsync( CancellationToken cancellationToken );
    ValueTask<CatalogInspection> InspectAsync(
        int workItemOffset,
        int workItemLimit,
        CancellationToken cancellationToken );
    ValueTask<CatalogMaintenanceResult> CheckIntegrityAsync(
        CancellationToken cancellationToken );
    ValueTask<CatalogMaintenanceResult> OptimizeAsync( CancellationToken cancellationToken );
    ValueTask<CatalogMaintenanceResult> RepairAsync( CancellationToken cancellationToken );
    ValueTask<CatalogMaintenanceResult> PurgeProjectionAsync(
        CancellationToken cancellationToken );
    ValueTask<PrivacyAuditResult> AuditPrivacyAsync( CancellationToken cancellationToken );
}

public interface ILauncherSearchPreferences
{
    ValueTask<LauncherPreferences> GetPreferencesAsync( CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<FilterAlias>> GetFilterAliasesAsync( CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<SavedSearch>> GetSavedSearchesAsync( CancellationToken cancellationToken );
    ValueTask<IReadOnlySet<string>> GetHiddenStableIdsAsync( CancellationToken cancellationToken );
    ValueTask<SearchResult?> FindAliasAsync( string aliasValue, CancellationToken cancellationToken );
}

public interface ILauncherPreferenceStore : ILauncherSearchPreferences
{
    ValueTask<LauncherPreferences> PatchSourcePreferencesAsync( SourcePreferencePatch patch, CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<SavedSearch>> SaveSavedSearchAsync(
        SavedSearch search,
        CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<SavedSearch>> DeleteSavedSearchAsync(
        string id,
        CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<FilterAlias>> SetFilterAliasAsync(
        string aliasToken,
        string? filterId,
        CancellationToken cancellationToken );
    ValueTask<PreferencesSnapshot> GetSnapshotAsync(
        bool includeShortcuts,
        CancellationToken cancellationToken );
    ValueTask<LauncherPreferences> ReplacePreferencesAsync(
        LauncherPreferences preferences,
        CancellationToken cancellationToken );
    ValueTask<CustomizationResult> SetFavoriteAsync(
        SearchResult result,
        bool enabled,
        CancellationToken cancellationToken );
    ValueTask<CustomizationResult> SetAliasAsync(
        SearchResult result,
        string? aliasValue,
        CancellationToken cancellationToken );
    ValueTask ReorderFavoritesAsync(
        IReadOnlyList<string> stableIds,
        CancellationToken cancellationToken );
    // Swaps two bookmarks' places, so a result moves one place among the
    // bookmarks a view shows; bookmarks between them keep theirs.
    ValueTask MoveFavoriteAsync(
        SearchResult result,
        SearchResult target,
        CancellationToken cancellationToken );
    ValueTask<CustomizationResult> RemoveOrphanAsync(
        string customizationId,
        CancellationToken cancellationToken );
    ValueTask<CustomizationExport> ExportAsync( CancellationToken cancellationToken );
    ValueTask<LauncherPreferences> ResetAsync( CancellationToken cancellationToken );
    ValueTask<CustomizationResult> SetHiddenAsync(
        SearchResult result,
        bool hidden,
        CancellationToken cancellationToken );
    ValueTask<CustomizationResult> RestoreHiddenAsync(
        string customizationId,
        CancellationToken cancellationToken );
    ValueTask<HiddenItemsResult> ListHiddenAsync(
        string? query,
        int limit,
        int offset,
        CancellationToken cancellationToken );
    // The coordinator drops hidden candidates that never touch SQL (extension
    // commands, launch variants). Implementations keep this cheap: a cached set.
}

public interface IApplicationIconResolver
{
    ValueTask<IReadOnlyDictionary<string, string>> ResolveApplicationIconsAsync(
        IReadOnlyList<string> windowClasses,
        CancellationToken cancellationToken );
}

public interface IFileHandlerCatalog
{
    ValueTask<IReadOnlyDictionary<string, IReadOnlyList<CommandAction>>> GetOpenWithActionsAsync(
        IReadOnlyList<string> paths,
        int maximumHandlersPerFile,
        CancellationToken cancellationToken );
}

public interface IAiPreferenceStore
{
    ValueTask<AiSelection> GetAiSelectionAsync( CancellationToken cancellationToken );
    ValueTask<AiSelection> SetAiSelectionAsync(
        string? providerId,
        string? modelId,
        CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<AiTemplate>> GetAiTemplatesAsync( CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<AiTemplate>> SaveAiTemplateAsync(
        AiTemplate promptTemplate,
        CancellationToken cancellationToken );
    ValueTask<IReadOnlyList<AiTemplate>> DeleteAiTemplateAsync(
        string templateId,
        CancellationToken cancellationToken );
}

public interface IIndexCoordinator : IAsyncDisposable, ICatalogOperations
{
    ValueTask StartAsync( CancellationToken cancellationToken );
    ValueTask<ReindexReport> ReindexAsync(
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken );
    ValueTask<ReindexReport> ReindexAsync(
        IndexReindexScope scope,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken );
    ValueTask<IncrementalIndexStatus> GetIncrementalStatusAsync(
        CancellationToken cancellationToken );
    ValueTask PauseAsync( string? rootId, CancellationToken cancellationToken );
    ValueTask ResumeAsync( string? rootId, CancellationToken cancellationToken );
    ValueTask<bool> RunMaintenanceIfDueAsync( CancellationToken cancellationToken );
}

public interface IActionExecutor
{
    ValueTask<ActionExecutionResult> ExecuteAsync(
        SearchResult result,
        CommandAction action,
        string query,
        CancellationToken cancellationToken );
}

public sealed class ActionExecutionResult
{
    public bool Succeeded { get; init; }
    public string? Message { get; init; }
    public string? DetailMarkdown { get; init; }
    public string? ConfirmationToken { get; init; }
    public string? UndoInvocationReference { get; init; }

    public static ActionExecutionResult Success( string? message = null, string? detailMarkdown = null ) =>
        new( )
        {
            Succeeded = true,
            Message = message,
            DetailMarkdown = detailMarkdown
        };

    public static ActionExecutionResult Failure(
        string message,
        string? detailMarkdown = null,
        string? confirmationToken = null ) => new( )
        {
            Succeeded = false,
            Message = message,
            DetailMarkdown = detailMarkdown,
            ConfirmationToken = confirmationToken
        };
}

public interface IAiProvider
{
    string Id { get; }
    AiProviderDescriptor Describe( );
    ValueTask<IReadOnlyList<AiModelDescriptor>> DiscoverModelsAsync(
        CancellationToken cancellationToken );
    AiPreparedProviderRequest PrepareRequest( AiPrompt prompt );
    IAsyncEnumerable<AiProviderStreamEvent> StreamAsync(
        AiPreparedProviderRequest request,
        CancellationToken cancellationToken );
}

public sealed class AiPrompt
{
    public required string Text { get; init; }
    public string? SystemPrompt { get; init; }
    public string? ModelOverride { get; init; }
}

public sealed class AiResponse
{
    public required string ProviderId { get; init; }
    public required string Model { get; init; }
    public required string Text { get; init; }
}
