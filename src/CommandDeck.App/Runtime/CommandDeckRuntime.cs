namespace CommandDeck;

public sealed partial class CommandDeckRuntime : IAsyncDisposable, IAgentLauncherHost
{
    private readonly RuntimeResources _resources;
    private readonly SearchCoordinator _search;
    private readonly ISearchCatalog _catalog;
    private readonly IUsageHistoryStore _usageHistory;
    private readonly ICatalogOperations _catalogOperations;
    private readonly IIndexCoordinator _indexer;
    private readonly IActionExecutor _actions;
    private readonly InvocationWorkflow _invocations;
    private readonly FilePreviewWorkflow _filePreviews;
    private HerdrClient _herdr = new( );
    private CurrencyRateService? _exchangeRates;
    private readonly FileSelectionService _fileSelection;
    private readonly AiProviderSettingsService _aiSettings;
    private readonly AiPreflightService _aiPreflights;
    private readonly AiGenerationCoordinator _aiGenerations;
    private readonly AiOutputService _aiOutputs;
    private readonly ILauncherPreferenceStore _preferences;
    // What may be remembered and learned from; shared with the catalog and the
    // search coordinator, and refreshed whenever preferences are read or saved.
    private readonly HistoryPolicy _history;
    private readonly IAiPreferenceStore _aiPreferences;
    private readonly OpenWithDetection? _openWith;
    private readonly SearchResultCache _cache = new( );
    private readonly CommandDeckOptions _options;
    private readonly RuntimeConstructionOptions _constructionOptions;
    private ClipboardHistoryStore? _clipboardStore;
    private ClipboardCapture? _clipboardCapture;

    public void StartClipboardCapture( string executable, IReadOnlyList<string> prefix ) => _clipboardCapture?.Start( executable, prefix );

    internal CommandDeckRuntime(
        CommandDeckOptions options,
        ISearchCatalog catalog,
        IUsageHistoryStore usageHistory,
        ICatalogOperations catalogOperations,
        IIndexCoordinator indexer,
        SearchCoordinator search,
        IActionExecutor actions,
        AiProviderSettingsService aiSettings,
        AiPreflightService aiPreflights,
        AiGenerationCoordinator aiGenerations,
        AiOutputService aiOutputs,
        FilePreviewService previews,
        ILauncherPreferenceStore preferences,
        IAiPreferenceStore aiPreferences,
        RuntimeResources resources,
        RuntimeConstructionOptions? constructionOptions = null,
        HistoryPolicy? history = null )
    {
        ArgumentNullException.ThrowIfNull( resources );

        _options = options;
        _constructionOptions = constructionOptions ?? new RuntimeConstructionOptions( );
        _catalog = catalog;
        _usageHistory = usageHistory;
        QueryHistory = new QueryHistoryWorkflow( usageHistory );
        _catalogOperations = catalogOperations;
        _indexer = indexer;
        _search = search;
        _actions = actions;
        _history = history ?? new HistoryPolicy( options.Privacy, options.Search );
        _invocations = new InvocationWorkflow( _cache, actions, usageHistory, options.Search, _history );
        _filePreviews = new FilePreviewWorkflow( _cache, previews, preferences, ( ) => Contributions );
        _fileSelection = new FileSelectionService( actions, ResolveOwnedCachedResult );
        _aiSettings = aiSettings;
        _aiPreflights = aiPreflights;
        _aiGenerations = aiGenerations;
        _aiOutputs = aiOutputs;
        _preferences = preferences;
        _aiPreferences = aiPreferences;
        _resources = resources;

        // These runtime-owned resources drain before the injected dependencies.
        resources.OwnDisposable( _aiSettingsMutex );
        resources.Own( _search );
        resources.Add( DisposeAgentAsync );

        if ( catalog is IFileHandlerCatalog fileHandlers )
        {
            _openWith = resources.Own( new OpenWithDetection( fileHandlers, _constructionOptions.OpenWithWait ) );
        }
    }

    public List<FileSelectionItem> SelectFile( string client, string resultId, bool enabled ) => _fileSelection.Select( client, resultId, enabled );
    public void ClearFileSelection( string client ) => _fileSelection.Clear( client );
    public ValueTask<ActionExecutionResult> ExecuteFileSelectionAsync( string client, string action, CancellationToken cancellationToken ) =>
        _fileSelection.ExecuteAsync( client, action, cancellationToken );

    public CommandDeckOptions Options => _options;
    public ISearchCatalog Catalog => _catalog;
    public ICatalogOperations CatalogOperations => _catalogOperations;
    public IIndexCoordinator Indexer => _indexer;
    public ILauncherPreferenceStore Preferences => _preferences;
    public HerdrClient Herdr => _herdr;
    internal CurrencyRateService? ExchangeRates => _exchangeRates;
    public QueryHistoryWorkflow QueryHistory { get; }

    public static ValueTask<CommandDeckRuntime> CreateAsync( string? configPath, CancellationToken cancellationToken,
        bool includeClipboardHistory = true ) =>
        RuntimeFactory.CreateAsync( configPath, includeClipboardHistory, new RuntimeConstructionOptions( ), cancellationToken );

    internal static ValueTask<CommandDeckRuntime> CreateAsync( string? configPath,
        RuntimeConstructionOptions constructionOptions, CancellationToken cancellationToken ) =>
        RuntimeFactory.CreateAsync( configPath, true, constructionOptions, cancellationToken );

    internal static ValueTask<CommandDeckRuntime> CreateAsync( string? configPath,
        RuntimeConstructionOptions constructionOptions, bool includeClipboardHistory,
        CancellationToken cancellationToken ) =>
        RuntimeFactory.CreateAsync( configPath, includeClipboardHistory, constructionOptions, cancellationToken );

    public async ValueTask<BootstrapSnapshot> GetBootstrapAsync( CancellationToken cancellationToken )
    {
        var preferences = await _preferences.GetPreferencesAsync( cancellationToken )
            .ConfigureAwait( false );
        var registry = Contributions;
        var quicklinks = await GetQuicklinksAsync( cancellationToken ).ConfigureAwait( false );
        for ( var attempt = 0; preferences.RegistryRevision != registry.Revision ||
            preferences.WebFallbackQuicklinkId != quicklinks.WebFallbackQuicklinkId; attempt++ )
        {
            if ( attempt == 3 )
            {
                throw new InvalidOperationException( "The source registry or quicklinks changed. Refresh the launcher." );
            }
            preferences = await _preferences.GetPreferencesAsync( cancellationToken ).ConfigureAwait( false );
            registry = Contributions;
            quicklinks = await GetQuicklinksAsync( cancellationToken ).ConfigureAwait( false );
        }
        var filterAliases = await _preferences.GetFilterAliasesAsync(
            cancellationToken ).ConfigureAwait( false );
        var savedSearches = await _preferences.GetSavedSearchesAsync(
            cancellationToken ).ConfigureAwait( false );
        return new BootstrapSnapshot
        {
            Preferences = preferences,
            Sources = [.. registry.Sources],
            Filters = [.. registry.WithAliases( filterAliases )],
            FilterAliases = [.. filterAliases],
            SavedSearches = [.. savedSearches],
            FallbackQuicklinks = quicklinks.Quicklinks
                .Where( entry => entry.Enabled ).Select( entry => entry.Quicklink ).Select( static value => new QuicklinkChoice
                {
                    Id = value.Id,
                    Title = value.Title
                } ).ToList( )
        };
    }

    internal void CancelBackgroundIndexing( )
    {
        if ( Indexer is IndexCoordinator indexer )
        {
            indexer.CancelBackgroundWork( );
        }
    }

    public ValueTask DisposeAsync( ) => _resources.DisposeAsync( );
}
