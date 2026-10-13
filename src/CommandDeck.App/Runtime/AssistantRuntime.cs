using System.Collections.Concurrent;
using System.Data.Common;

namespace CommandDeck;

internal sealed partial class AssistantRuntime : IAsyncDisposable
{
    private readonly AgentOptions _agentOptions;
    private readonly RuntimeConstructionOptions _constructionOptions;
    private readonly AiProviderSettingsService _aiSettings;
    private readonly AiOutputService _aiOutputs;
    private readonly IAgentLauncherHost _launcherHost;
    private readonly Func<SearchResult, string, SearchResult> _cacheSyntheticResult;

    public AssistantRuntime( AgentOptions options, RuntimeConstructionOptions constructionOptions,
        AiProviderSettingsService aiSettings, AiOutputService aiOutputs, IAgentLauncherHost launcherHost,
        Func<SearchResult, string, SearchResult> cacheSyntheticResult )
    {
        _agentOptions = options;
        _constructionOptions = constructionOptions;
        _aiSettings = aiSettings;
        _aiOutputs = aiOutputs;
        _launcherHost = launcherHost;
        _cacheSyntheticResult = cacheSyntheticResult;
        _agentResources.OwnDisposable( _agentMaintenanceCancellation );
    }

    private AgentConversationCoordinator? _agent;
    private CodexAgentBackend? _codex;
    private AgentBackendRegistry? _backends;
    private AgentTrustService? _trust;
    private AgentContextBroker? _contextBroker;
    private PacmanCatalog? _packages;
    private IPackageHelper? _helper;
    private AgentAuthority? _authority;
    private readonly RuntimeResources _agentResources = new( );
    private readonly CancellationTokenSource _agentMaintenanceCancellation = new( );
    private Task _agentMaintenance = Task.CompletedTask;
    private readonly ConcurrentDictionary<string, (PackageTransactionPreview Preview, DateTimeOffset ExpiresAt, long AuthorityEpoch)> _packagePreviews =
        new( StringComparer.Ordinal );

    private AgentConversationCoordinator Agent
    {
        get
        {
            var agent = _agent;
            if ( agent is null )
            {
                throw new InvalidOperationException( "The assistant runtime is not attached." );
            }

            return agent;
        }
    }

    // The authority owner keeps the last record it read or wrote; nothing here assigns it.
    private AgentPreferences CachedAgentPreferences => _authority?.Current ?? AgentPreferences.CreateDefault( );

    // Takes ownership of the authority. Registered before the components that
    // use it, it is disposed after them and after the maintenance loop stops.
    public void Initialize( AgentAuthority authority )
    {
        _authority = _agentResources.OwnDisposable( authority );
        CodexCredentialStore credentials = new( _agentOptions.StateDirectory );
        var backend = _constructionOptions.AgentBackendFactory?.Invoke( _agentOptions, credentials )
                      ?? new CodexAgentBackend( _agentOptions, credentials, clientVersion: "0.1.0-preview.1" );
        _codex = backend as CodexAgentBackend;
        _agentResources.Own( backend );
        List<IAgentBackend> backends = [backend];
        foreach ( var profile in CliAgentProfile.All )
        {
            var cli = _constructionOptions.CliAgentBackendFactory is null
                ? new CliAgentBackend( profile, _agentOptions )
                : _constructionOptions.CliAgentBackendFactory( profile, _agentOptions );
            if ( cli is not null )
            {
                _agentResources.Own( cli );
                backends.Add( cli );
            }
        }
        _backends = new AgentBackendRegistry( backends, backend.Id );
        _trust = new AgentTrustService( authority, _constructionOptions.AgentClock );
        _contextBroker = new AgentContextBroker( authority, options: _agentOptions );
        _packages = _constructionOptions.PackageCatalogFactory?.Invoke( ) ?? new PacmanCatalog( );
        _helper = _constructionOptions.PackageHelperFactory is { } helperFactory
            ? helperFactory( )
            : OperatingSystem.IsLinux( ) ? new PackageHelperClient( ) : null;
        if ( _helper is not null )
        {
            _agentResources.Own( _helper );
        }
        _agent = new AgentConversationCoordinator(
            authority,
            _backends,
            _contextBroker,
            new AgentCommandTool( ),
            _launcherHost,
            _packages,
            _helper,
            _agentOptions.AutoApproveReadOnlyCommands,
            _constructionOptions.AgentClock );
        _agentResources.Own( _agent );
        _agentResources.Add( async ( ) =>
        {
            await _agentMaintenanceCancellation.CancelAsync( ).ConfigureAwait( false );
            await _agentMaintenance.ConfigureAwait( false );
        } );
        _agentMaintenance = MaintainAgentAsync( );
    }

    private async Task MaintainAgentAsync( )
    {
        await WarmAgentPreferencesAsync( ).ConfigureAwait( false );
        using PeriodicTimer timer = new( TimeSpan.FromSeconds( 30 ) );
        try
        {
            while ( await timer.WaitForNextTickAsync( _agentMaintenanceCancellation.Token ).ConfigureAwait( false ) )
            {
                await SweepAgentAsync( ).ConfigureAwait( false );
            }
        }
        catch ( OperationCanceledException ) when ( _agentMaintenanceCancellation.IsCancellationRequested )
        {
        }
    }

    private async Task WarmAgentPreferencesAsync( )
    {
        try
        {
            await RefreshAgentPreferenceCacheAsync( CancellationToken.None ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is InvalidOperationException or IOException or AiOperationException )
        {
            AppLog.Debug( "Assistant preference warm-up failed: " + exception.GetType( ).Name );
        }
    }

    private async Task SweepAgentAsync( )
    {
        try
        {
            if ( _agent is not null )
            {
                await _agent.SweepAsync( CancellationToken.None ).ConfigureAwait( false );
            }
        }
        // The sweep reads the preference store to enforce the lease; a busy or
        // failed read must skip this tick, not end maintenance for the daemon.
        catch ( Exception exception ) when ( exception is InvalidOperationException or IOException or AiOperationException or DbException )
        {
            AppLog.Debug( "Assistant sweep failed: " + exception.GetType( ).Name );
        }
    }

    private async ValueTask<AgentPreferences> RefreshAgentPreferenceCacheAsync( CancellationToken cancellationToken )
    {
        var preferences = await Agent.LoadPreferencesAsync( cancellationToken ).ConfigureAwait( false );
        if ( !preferences.Enabled || preferences.TrustLevel < AgentTrustLevels.Packages )
        {
            _packagePreviews.Clear( );
        }
        return preferences;
    }

    public AgentRowState AgentRowState( )
    {
        var preferences = CachedAgentPreferences;
        var selected = SelectedAgentBackend( );
        Dictionary<string, AgentBackendDescriptor> descriptors = new( StringComparer.OrdinalIgnoreCase );
        foreach ( var registered in _backends?.All ?? [] )
        {
            descriptors[registered.Id] = registered.Describe( );
        }
        var authenticated = selected is not null &&
                            descriptors.TryGetValue( selected.Id, out var descriptor ) &&
                            descriptor.AuthenticationState == AgentAuthenticationStates.Authenticated;
        return new AgentRowState( preferences.Enabled, selected?.Id, authenticated, _agent?.Summary( ), preferences.ModelId, descriptors );
    }

    /// <summary>The assistant backend the preferences select, or null when a direct provider is selected.</summary>
    private IAgentBackend? SelectedAgentBackend( ) => _backends?.Resolve( CachedAgentPreferences.BackendId );

    private IAgentBackend ResolveAgentBackend( string? backendId )
    {
        var registry = _backends;
        if ( registry is null )
        {
            throw new InvalidOperationException( "The assistant runtime is not attached." );
        }

        var resolved = registry.Resolve( backendId ?? CachedAgentPreferences.BackendId );
        if ( resolved is not null )
        {
            return resolved;
        }
        var requested = backendId ?? CachedAgentPreferences.BackendId ?? string.Empty;
        var isDirectProvider = _aiSettings.Current.Options.Providers.Any( provider =>
            string.Equals( provider.Id, requested, StringComparison.OrdinalIgnoreCase ) );
        throw new AiOperationException(
            isDirectProvider ? AiErrorCategories.Unsupported : AiErrorCategories.NotFound,
            safeSummary: isDirectProvider
                ? "Answer-only providers have no sign-in; use the direct AI settings instead."
                : "The requested assistant backend is not registered." );
    }

    public string? SelectedAnswerBackendId( )
    {
        var selected = CachedAgentPreferences.BackendId;
        return selected is null || _backends?.Contains( selected ) == true ? null : selected;
    }

    public List<SearchResult> BuildAgentHomeRows( )
    {
        if ( _agent is null || !CachedAgentPreferences.Enabled )
        {
            return [];
        }
        List<SearchResult> rows = [];
        var conversation = _agent.Summary( );
        if ( conversation is not null )
        {
            rows.Add( AgentCommandProvider.ContinueRow( conversation, 1.0 ) );
        }
        foreach ( var operation in _agent.Operations( ).Take( 2 ) )
        {
            rows.Add( new SearchResult
            {
                Id = "agent:operation:" + operation.Id,
                Kind = ResultKinds.Assistant,
                Title = operation.Title,
                Subtitle = $"Package operation · {operation.State}",
                Icon = "system-software-install",
                ProviderId = ProviderIds.Agent,
                SourceId = SourceIds.Ai,
                Section = new ResultSection { Id = "assistant", Title = "Assistant", Order = 1 },
                Actions = [new CommandAction { Id = "open", Title = "Open", Kind = ActionKinds.AgentOpen }]
            } );
        }
        return rows;
    }

    public string? TryGetAgentOutput( string owner, long generation ) => _agent?.TryGetOutput( owner, generation );

    public void ClearAgentClient( string clientOwnerId ) => _trust?.ClearClient( clientOwnerId );

    public ValueTask DisposeAsync( ) => _agentResources.DisposeAsync( );
}
