using System.Collections.Concurrent;

namespace CommandDeck;

public sealed partial class AgentConversationCoordinator : IAsyncDisposable
{
    public const string OutputOwnerId = "agent-conversation";
    private const string LeaseExpiredReason = "Unrestricted lease expired.";
    private const string AuthorityChangedReason = "Assistant permissions changed.";
    private readonly AgentAuthority _authority;
    private readonly IAgentBackendRegistry _backends;
    private readonly AgentContextBroker _context;
    private readonly AgentCommandTool _commands;
    private readonly IAgentLauncherHost _launcher;
    private readonly PacmanCatalog? _packages;
    private readonly IPackageHelper? _helper;
    private readonly bool _autoApproveReadOnlyCommands;
    private readonly TimeProvider _clock;
    private readonly object _sync = new( );
    // Serializes conversation preparation so a prewarm and the first real turn
    // cannot both spawn a process and start a thread; the backend has its own
    // gate, but this stops two callers from each creating a Conversation.
    private readonly SemaphoreSlim _prepareGate = new( 1, 1 );
    private readonly ConcurrentDictionary<long, Subscriber> _subscribers = new( );
    private readonly AiOutputCache _outputs = new( maximumEntries: 8 );
    private readonly ConcurrentDictionary<string, AgentOperationSummary> _operations = new( StringComparer.Ordinal );
    private Conversation? _conversation;
    // Last time a prewarm or turn touched the backend; the process stays warm
    // for AgentLimits.DefaultBackendKeepAliveMinutes past this even after the
    // conversation transcript is swept.
    private DateTimeOffset _lastActivityUtc;
    private long _generation;
    // The newest authority epoch whose stale work has been revoked. Epochs start
    // at 1, and nothing can be admitted under an epoch that is no longer current.
    private long _revokedThroughEpoch = 1;
    private long _subscriberSequence;
    private long _outputGeneration;
    private int _disposed;

    private static AgentBackendRegistry CreateBackendRegistry( IAgentBackend backend )
    {
        if ( backend is null )
        {
            throw new ArgumentNullException( nameof( backend ) );
        }

        return new AgentBackendRegistry( [backend], backend.Id );
    }

    public AgentConversationCoordinator(
        AgentAuthority authority,
        IAgentBackend backend,
        AgentContextBroker context,
        AgentCommandTool commands,
        IAgentLauncherHost launcher,
        PacmanCatalog? packages = null,
        IPackageHelper? helper = null,
        bool autoApproveReadOnlyCommands = false,
        TimeProvider? clock = null )
        : this(
            authority,
            CreateBackendRegistry( backend ),
            context, commands, launcher, packages, helper, autoApproveReadOnlyCommands, clock )
    {
    }

    public AgentConversationCoordinator(
        AgentAuthority authority,
        IAgentBackendRegistry backends,
        AgentContextBroker context,
        AgentCommandTool commands,
        IAgentLauncherHost launcher,
        PacmanCatalog? packages = null,
        IPackageHelper? helper = null,
        bool autoApproveReadOnlyCommands = false,
        TimeProvider? clock = null )
    {
        _authority = authority ?? throw new ArgumentNullException( nameof( authority ) );
        if ( backends is null )
        {
            throw new ArgumentNullException( nameof( backends ) );
        }

        _backends = backends;
        _context = context;
        _commands = commands;
        _launcher = launcher;
        _packages = packages;
        _helper = helper;
        _autoApproveReadOnlyCommands = autoApproveReadOnlyCommands;
        _clock = clock ?? TimeProvider.System;
        _lastActivityUtc = _clock.GetUtcNow( );
    }

    /// <summary>The default backend; conversations bind to the backend the preferences select.</summary>
    public IAgentBackend Backend
    {
        get
        {
            var defaultBackend = _backends.Resolve( null );
            if ( defaultBackend is null )
            {
                throw new InvalidOperationException( "No assistant backend is registered." );
            }

            return defaultBackend;
        }
    }

    private IAgentBackend ResolveBackend( string? backendId )
    {
        var selectedBackend = _backends.Resolve( backendId );
        if ( selectedBackend is null )
        {
            throw new AiOperationException(
            AiErrorCategories.Unsupported,
            safeSummary: "Only assistant backends support conversations; direct providers answer through the AI preflight." );
        }

        return selectedBackend;
    }

    public bool HasActiveTurn
    {
        get
        {
            lock ( _sync )
            {
                return _conversation?.TurnRunning == true;
            }
        }
    }

    public int PendingRequestCount
    {
        get
        {
            lock ( _sync )
            {
                return _conversation?.Pending is null ? 0 : 1;
            }
        }
    }

    public string? TryGetOutput( string owner, long generation ) =>
        owner == OutputOwnerId && _outputs.TryGet( OutputOwnerId, generation, out var text ) ? text : null;

    public AgentConversationSnapshot Snapshot( string? requesterOwnerId = null )
    {
        lock ( _sync )
        {
            var conversation = _conversation;
            if ( conversation is null )
            {
                return new AgentConversationSnapshot { Generation = _generation };
            }
            return new AgentConversationSnapshot
            {
                Summary = Summarize( conversation, requesterOwnerId ),
                Entries = [.. conversation.Entries],
                ActiveText = conversation.ActiveText.ToString( ),
                ActiveReasoning = conversation.ActiveReasoning.ToString( ),
                ActiveStatus = conversation.ActiveStatus,
                Tools = [.. conversation.Tools],
                Pending = CanResolve( conversation.Pending, requesterOwnerId )
                    ? conversation.Pending?.Request
                    : null,
                Generation = conversation.Generation,
                LastSequence = conversation.Sequence,
                ActiveTurnId = conversation.TurnRunning ? conversation.ActiveTurnId : 0,
                Usage = conversation.Usage,
                LastError = conversation.LastError,
                OutputGeneration = conversation.OutputGeneration,
                OutputActions = conversation.OutputGeneration > 0 ? OutputActions( ) : []
            };
        }
    }

    public AgentConversationSummary? Summary( )
    {
        lock ( _sync )
        {
            return _conversation is null ? null : Summarize( _conversation, requesterOwnerId: null );
        }
    }

    public IReadOnlyList<AgentOperationSummary> Operations( ) => [.. _operations.Values.OrderByDescending( static value => value.UpdatedUnixMilliseconds )];

    public async ValueTask<AgentConversationSnapshot> StartTurnAsync(
        string text,
        string? backendId,
        string? modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken )
        => await StartTurnAsync(
            text,
            backendId,
            modelId,
            reasoningEffort,
            clientOwnerId: null,
            cancellationToken ).ConfigureAwait( false );

    public async ValueTask<AgentConversationSnapshot> StartTurnAsync(
        string text,
        string? backendId,
        string? modelId,
        string? reasoningEffort,
        string? clientOwnerId,
        CancellationToken cancellationToken )
    {
        AgentContractValidator.ValidatePromptText( text );
        var authority = await LoadSnapshotAsync( cancellationToken ).ConfigureAwait( false );
        var preferences = authority.Preferences;
        if ( !preferences.Enabled )
        {
            throw new AiOperationException( AiErrorCategories.Disabled, safeSummary: "The assistant is disabled. Enable it first." );
        }
        var backend = ResolveBackend( backendId ?? preferences.BackendId );
        var login = await backend.GetLoginStateAsync( cancellationToken ).ConfigureAwait( false );
        if ( login.State != AgentAuthenticationStates.Authenticated )
        {
            throw new AiOperationException(
                AiErrorCategories.Authentication,
                safeSummary: $"Sign in to {backend.Describe( ).DisplayName} before starting a conversation." );
        }
        var conversation = await PrepareConversationAsync( backend, authority, modelId, reasoningEffort, cancellationToken )
            .ConfigureAwait( false );
        long turnId;
        lock ( _sync )
        {
            // Turn admission: the epoch the turn was prepared under must still be
            // current when the turn becomes active. A revocation that follows an
            // epoch change takes this lock afterwards, so it finds any turn
            // admitted just before the change.
            if ( !_authority.IsCurrent( authority.SessionEpoch ) || !ReferenceEquals( _conversation, conversation ) )
            {
                throw new AgentAuthorityChangedException( );
            }
            if ( conversation.TurnRunning )
            {
                throw new AiOperationException( AiErrorCategories.Busy, safeSummary: "A turn is already running." );
            }
            // A real turn is the strongest keep-alive signal: refresh it so the
            // warm process survives the full idle window after the turn ends.
            _lastActivityUtc = _clock.GetUtcNow( );
            turnId = ++conversation.TurnCount;
            conversation.ActiveTurnId = turnId;
            conversation.ActiveText.Clear( );
            conversation.ActiveReasoning.Clear( );
            conversation.ActiveTextBytes = 0;
            conversation.ActiveReasoningBytes = 0;
            conversation.Tools.Clear( );
            conversation.LastError = null;
            conversation.State = AgentConversationStates.Running;
            conversation.ActiveStatus = "Starting";
            conversation.ContextBytesUsed = 0;
            conversation.ToolCalls = 0;
            conversation.ExpiresAt = null;
            conversation.ClientOwnerId = clientOwnerId;
            foreach ( (var id, var subscriber) in _subscribers )
            {
                if ( subscriber.Generation == 0 )
                {
                    _subscribers.TryUpdate( id, subscriber with { Generation = conversation.Generation }, subscriber );
                }
            }
            AppendEntry( conversation, AgentTranscriptRoles.User, text, turnId );
            conversation.TurnCancellation = new CancellationTokenSource( );
            conversation.TurnFinished = false;
            conversation.TurnTask = RunTurnAsync( conversation, turnId, text, modelId, reasoningEffort, conversation.TurnCancellation.Token );
        }
        return Snapshot( clientOwnerId );
    }

    // Best-effort intent prewarm: spawn the sandbox, connect, and start a thread
    // WITHOUT running a turn, so the first prompt after the user signals intent
    // reuses a warm session instead of paying cold start. It must never sign in
    // or surface an error, so a disabled or unauthenticated assistant no-ops.
    public async ValueTask PrewarmAsync( CancellationToken cancellationToken )
    {
        try
        {
            await PrewarmCoreAsync( cancellationToken ).ConfigureAwait( false );
        }
        catch ( AgentAuthorityChangedException )
        {
            // An authority change overtook the prewarm; the next prompt prepares
            // under the new authority. A prewarm never surfaces an error.
        }
    }

    private async ValueTask PrewarmCoreAsync( CancellationToken cancellationToken )
    {
        var authority = await LoadSnapshotAsync( cancellationToken ).ConfigureAwait( false );
        var preferences = authority.Preferences;
        if ( !preferences.Enabled )
        {
            return;
        }
        var backend = _backends.Resolve( preferences.BackendId );
        if ( backend is null )
        {
            return;
        }
        var login = await backend.GetLoginStateAsync( cancellationToken ).ConfigureAwait( false );
        if ( login.State != AgentAuthenticationStates.Authenticated )
        {
            return;
        }
        var conversation = await PrepareConversationAsync(
            backend, authority, preferences.ModelId, preferences.ReasoningEffort, cancellationToken ).ConfigureAwait( false );
        lock ( _sync )
        {
            _lastActivityUtc = _clock.GetUtcNow( );
            // A conversation prewarmed but never used would keep ExpiresAt null
            // and never be swept. Give a fresh, turn-free conversation the same
            // idle expiry MarkIdle would, but never clobber one that has run (or
            // is running) a real turn — its own TTL already governs it.
            if ( ReferenceEquals( _conversation, conversation ) &&
                conversation.TurnCount == 0 &&
                conversation.TurnTask is null &&
                conversation.ExpiresAt is null )
            {
                conversation.UpdatedAt = _clock.GetUtcNow( );
                conversation.ExpiresAt = conversation.TtlMinutes == 0
                    ? conversation.UpdatedAt
                    : conversation.UpdatedAt.AddMinutes( conversation.TtlMinutes );
            }
        }
    }

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        await ShutdownBackendAsync( CancellationToken.None ).ConfigureAwait( false );
        _prepareGate.Dispose( );
    }
}
