namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{
    private static class RuntimeFactory
    {
        public static async ValueTask<CommandDeckRuntime> CreateAsync(
        string? configPath,
        bool includeClipboardHistory, RuntimeConstructionOptions constructionOptions,
        CancellationToken cancellationToken )
        {
            RuntimeResources resources = new( );
            try
            {
                var options =
                    await ConfigurationLoader.LoadAsync( configPath, cancellationToken ).ConfigureAwait( false );
                var executable = Environment.ProcessPath;
                if ( executable is null )
                {
                    throw new InvalidOperationException( "Cannot locate the CommandDeck executable." );
                }

                string[] executablePrefix = Path.GetFileNameWithoutExtension( executable ) == "dotnet"
                    ? [Environment.GetCommandLineArgs( )[0]]
                    : [];

                SqliteCatalog catalog = new(
                    options.Storage,
                    privacy: options.Privacy,
                    searchOptions: options.Search );
                resources.Own( catalog );
                await catalog.InitializeAsync( cancellationToken ).ConfigureAwait( false );

                var extensions = await ExtensionPackageRegistry.OpenAsync( catalog,
                    new ExtensionArtifactStore( PathExpansion.Expand( options.Storage.DatabasePath ) + ".extensions" ), cancellationToken ).ConfigureAwait( false );
                resources.OwnDisposable( extensions );
                catalog.Contributions = extensions;
                IExtensionLauncher extensionLauncher = new ExtensionSupervisor( executable, executablePrefix );
                var extensionJournal = new FileRemoteEffectJournal( Path.Combine( extensions.ArtifactRoot, "effect-journal" ) );
                resources.OwnDisposable( extensionJournal );
                var extensionConnections = new ExtensionConnections( extensionJournal, new ExtensionSecretResolver( ) );
                resources.OwnDisposable( extensionConnections );
                var extensionDevelopment = new ExtensionDevelopment( );
                resources.OwnDisposable( extensionDevelopment );
                IndexCoordinator indexer = new(
                    catalog,
                    catalog,
                    options,
                    diagnostic: SearchLogging.LogIndexDiagnostic );
                resources.Own( indexer );
                if ( constructionOptions.StartIndexer )
                {
                    await indexer.StartAsync( cancellationToken ).ConfigureAwait( false );
                }
                ClipboardHistoryStore? clipboardStore = null;
                ClipboardCapture? clipboardCapture = null;
                ClipboardHistoryProvider? clipboardProvider = null;
                ClipboardPayloadProjection? clipboardProjections = null;
                ClipboardActionWorker? clipboardActions = null;
                if ( includeClipboardHistory )
                {
                    clipboardStore = new( PathExpansion.Expand( options.Storage.DatabasePath ) + ".clipboard.db", options.ClipboardHistory );
                    resources.OwnDisposable( clipboardStore );
                    clipboardProjections = new( new ClipboardHtmlProjectionService( executable ) );
                    clipboardActions = new( executable, executablePrefix,
                        ClipboardResolverEnvironment.Capture( ) );
                    resources.OwnDisposable( clipboardActions );
                    clipboardCapture = new( clipboardStore, clipboardProjections );
                    resources.Own( clipboardCapture );
                    clipboardProvider = new( clipboardStore, ( ) => clipboardCapture.Status );
                }

                CommandDeckRuntime? runtime = null;
                var extensionSearch = new ExtensionSearchService( extensions, new ExtensionSearchWorker( extensions, extensionLauncher ) );
                resources.Own( extensionSearch );
                HerdrClient herdr = new( );
                HeyClient hey = new( );
                List<ICommandProvider> providers =
                [
                    new CalculatorProvider(
                runner: null,
                available: null,
                options: options.Search.Calculator),
            new ApplicationSearchProvider(catalog),
            new CatalogSearchProvider(catalog, catalog.LauncherPreferences),
            new ExtensionCommandProvider(extensions, catalog.LauncherPreferences),
            extensionSearch,
            new HyprlandProvider(catalog),
            new HerdrProvider(herdr),
            new HeyProvider(hey),
            new AiCommandProvider(() => runtime?.EffectiveAiOptions ?? options.Ai, () => runtime?.SelectedAnswerBackendId()),
            new AgentCommandProvider(() => runtime?.AgentRowState()),
            new FolderBrowserProvider(),
            new ScriptCommandProvider(options.ScriptDirectories),
            new SnippetProvider(options.Snippets),
            new ProjectProvider(options.Projects),
            new SystemdUserServiceProvider(),
            new TimezoneProvider(),
            new WebSearchProvider(options.Search.WebEngines),
            new EmojiProvider(),
            new DesktopControlsProvider(),
            new ProcessProvider()
                ];
                if ( clipboardProvider is not null )
                {
                    providers.Add( clipboardProvider );
                }

                SearchCoordinator search = new(
                    providers,
                    options.Search,
                    catalog,
                    catalog.LauncherPreferences,
                    catalog.UsageHistory,
                    privacy: options.Privacy,
                    contributions: extensions,
                    providerDiagnostic: SearchLogging.LogProviderDiagnostic );

                // The managed provider document lives beside the effective configuration
                // file; it overrides the JSONC AI section once a management write exists.
                AiProviderSettingsService aiSettings = new(
                    options.Ai,
                    new AiProviderSettingsStore( AiProviderSettingsStore.ResolvePath( configPath ) ) );
                resources.Own( aiSettings );
                FilePreviewService previews = new( options.Indexing, options.Privacy );
                AiPreflightService preflights = new(
                    aiSettings,
                    previews,
                    catalog.AiPreferences,
                    ( owner, resultId ) => runtime?.ResolveOwnedCachedResult( owner, resultId ) );
                resources.OwnDisposable( preflights );
                AiGenerationCoordinator generations = new( preflights );
                resources.OwnDisposable( generations );
                LinuxActionExecutor actions = new(
                    catalog,
                    clipboardProvider,
                    herdr,
                    new HerdrWindowActivator( herdr ),
                    hey: hey );
                AiOutputService outputs = new(
                    generations,
                    actions,
                    additionalOutputSource: ( owner, generation ) => runtime?.TryGetAgentOutput( owner, generation ) );
                resources.OwnDisposable( outputs );
                runtime = new CommandDeckRuntime(
                    options,
                    catalog,
                    catalog.UsageHistory,
                    indexer,
                    indexer,
                    search,
                    actions,
                    aiSettings,
                    preflights,
                    generations,
                    outputs,
                    previews,
                    catalog.LauncherPreferences, catalog.AiPreferences, resources, constructionOptions );
                runtime._extensions = extensions;
                runtime._extensionSearch = extensionSearch;
                runtime._extensionConnections = extensionConnections;
                runtime._extensionDevelopment = extensionDevelopment;
                runtime._herdr = herdr;
                runtime._extensionSessions = new ExtensionSessionService( extensions, extensionLauncher, actions,
                    extensionDevelopment, extensionConnections );
                resources.Own( runtime._extensionSessions );
                runtime._extensionManagement = new ExtensionManagementService( extensions, runtime._extensionSessions,
                    extensionConnections, extensionDevelopment, extensionJournal, actions );
                extensions.RegisterRevocation( ExtensionRevocationStage.Search, ( _, _ ) => extensionSearch.RevokeAsync( ) );
                runtime._clipboardStore = clipboardStore;
                if ( clipboardStore is not null )
                {
                    runtime._clipboardHistory = new( clipboardStore, copyText: ( text, token ) => actions.ExecuteAsync(
                        new SearchResult { Id = "clipboard:copy", Title = "Copy", Kind = ResultKinds.CustomCommand, SourceId = SourceIds.Clipboard },
                        new CommandAction { Id = "copy", Title = "Copy", Kind = ActionKinds.CopyText, Arguments = new( ) { ["text"] = text } },
                        "", token ), captureStatus: ( ) => clipboardCapture!.Status, projections: clipboardProjections,
                        serve: ( offers, token ) => clipboardCapture!.ServeAsync( offers, token ),
                        resolveHandlers: ( key, target, token ) => clipboardActions!.ResolveAsync( key, target, token ) );
                }

                runtime._clipboardCapture = clipboardCapture;
                runtime.AttachAgent( catalog, search );
                return runtime;
            }
            catch ( Exception startupFailure )
            {
                try
                {
                    await resources.DisposeAsync( ).ConfigureAwait( false );
                }
                catch ( Exception cleanupFailure ) { throw new AggregateException( "Runtime startup and rollback failed.", startupFailure, cleanupFailure ); }
                throw;
            }
        }

    }
}
