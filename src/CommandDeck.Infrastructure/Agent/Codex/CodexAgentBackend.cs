
namespace CommandDeck;

public sealed partial class CodexAgentBackend : IAgentBackend
{
    private readonly AgentOptions _options;
    private readonly CodexCredentialStore _credentials;
    private readonly Func<int, IAgentProcessLauncher> _launcherFactory;
    private readonly IAiCredentialResolver _credentialResolver;
    private readonly string _clientVersion;
    private readonly SemaphoreSlim _gate = new( 1, 1 );
    private readonly object _authenticationLock = new( );
    private CodexBinaryInfo? _binary;
    private CodexAppServerClient? _client;
    private CodexRuntimeHome? _runtimeHome;
    // The authority epoch the running process was admitted under; null for a
    // restricted process started for login or model discovery.
    private long? _clientEpoch;
    private IAgentAuthorityEpoch? _clientAuthority;
    private AgentLoginState _login;
    private string _health = AgentHealthStates.Configured;
    private AiError? _lastError;
    private string? _pendingLoginId;
    private Action<string?>? _accountUpdated;
    private Action<bool, string?, string?>? _loginCompleted;
    private int _disposed;

    public CodexAgentBackend(
        AgentOptions options,
        CodexCredentialStore credentials,
        Func<int, IAgentProcessLauncher>? launcherFactory = null,
        IAiCredentialResolver? credentialResolver = null,
        string? clientVersion = null )
    {
        if ( options is null )
        {
            throw new ArgumentNullException( nameof( options ) );
        }

        _options = options;
        if ( credentials is null )
        {
            throw new ArgumentNullException( nameof( credentials ) );
        }

        _credentials = credentials;
        _launcherFactory = launcherFactory ?? CreateDefaultLauncher;
        _credentialResolver = credentialResolver ?? new AiCredentialResolver( );
        _clientVersion = clientVersion ?? "0.1.0";
        _login = new AgentLoginState
        {
            State = _credentials.Exists ? AgentAuthenticationStates.Authenticated : AgentAuthenticationStates.Unauthenticated,
            SupportedMethods = [AgentLoginMethods.DeviceCode, AgentLoginMethods.ApiKey]
        };
    }

    public string Id => AgentBackendIds.Codex;

    public int TrustLevel { get; private set; } = AgentTrustLevels.Default;

    public bool WebSearchEnabled { get; private set; }

    public bool IsRunning => _client is not null && !_client.IsFaulted;

    public string? BinaryPath => _binary?.CanonicalPath;

    public string? BinaryVersion => _binary?.Version;

    public bool? BinaryReviewed => _binary?.Reviewed;

    public string RuntimeGeneration => _runtimeHome?.Generation ?? string.Empty;

    public AgentBackendDescriptor Describe( ) => new( )
    {
        Id = Id,
        DisplayName = "Codex",
        Kind = AgentBackendKinds.CodexAppServer,
        ProcessLocality = AiLocality.Local,
        InferenceLocality = AiLocality.Remote,
        Available = _health is not AgentHealthStates.Unavailable and not AgentHealthStates.Incompatible,
        HealthState = IsRunning ? AgentHealthStates.Ready : _health,
        AuthenticationState = _login.State,
        Capabilities = new AgentBackendCapabilities
        {
            Conversation = true,
            Tools = true,
            Reasoning = true,
            ModelDiscovery = true,
            PreflightRequired = false,
            Usage = true,
            Login = true,
            ContractVersion = "codex-app-server-v2"
        },
        Explanation = _lastError?.Summary ?? "Codex runs locally; inference uses your OpenAI account.",
        Error = _lastError
    };

    public async ValueTask<AgentModelInventory> DiscoverModelsAsync( bool includeHidden, CancellationToken cancellationToken )
    {
        try
        {
            var client = await EnsureStartedAsync( cancellationToken ).ConfigureAwait( false );
            return await client.ListModelsAsync( includeHidden, cancellationToken ).ConfigureAwait( false );
        }
        catch ( AiOperationException exception )
        {
            return new AgentModelInventory
            {
                BackendId = Id,
                Error = new AiError { Category = exception.Category, Summary = exception.SafeSummary ?? "Model discovery failed." }
            };
        }
        catch ( Exception exception ) when ( exception is JsonRpcException or InvalidDataException or EndOfStreamException )
        {
            return new AgentModelInventory
            {
                BackendId = Id,
                Error = new AiError { Category = AiErrorCategories.MalformedResponse, Summary = "Codex returned an unusable model list." }
            };
        }
    }

    public async ValueTask<IAgentSession> StartSessionAsync( AgentSessionOptions options, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( options );
        AgentContractValidator.ValidateTrustLevel( options.TrustLevel );
        var admission = options.Admission;
        if ( admission is null && options.TrustLevel >= AgentTrustLevels.Unrestricted )
        {
            throw new InvalidOperationException( "An unrestricted session requires an authority admission." );
        }

        CodexAppServerClient client;
        string cwd;
        string generation;
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            // Process admission: the authority the session was prepared under
            // must still be current where a process is launched or reused. A
            // revocation stops processes under this same gate, so a launch that
            // passes here is either newer than the change or stopped by it.
            if ( admission is { IsCurrent: false } )
            {
                throw new AgentAuthorityChangedException( );
            }
            TrustLevel = options.TrustLevel;
            WebSearchEnabled = options.WebSearchEnabled;
            client = await EnsureStartedUnderGateAsync( admission, cancellationToken ).ConfigureAwait( false );
            generation = _runtimeHome!.Generation;
            cwd = options.TrustLevel >= AgentTrustLevels.Unrestricted
                ? Environment.GetFolderPath( Environment.SpecialFolder.UserProfile )
                : _runtimeHome.Path;
        }
        finally
        {
            _gate.Release( );
        }

        var session = await client.StartThreadAsync( options, cwd, cancellationToken ).ConfigureAwait( false );
        lock ( _authenticationLock )
        {
            if ( ReferenceEquals( _client, client ) )
            {
                session.ProcessGeneration = generation;
                return session;
            }
        }

        await session.DisposeAsync( ).ConfigureAwait( false );
        throw new OperationCanceledException( "The Codex runtime was stopped." );
    }

    public async ValueTask StopAsync( CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            await StopUnderGateAsync( cancellationToken: CancellationToken.None ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask StopBeforeAsync( long authorityEpoch, CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _client is not null && (_clientEpoch is null || _clientEpoch < authorityEpoch) )
            {
                await StopUnderGateAsync( cancellationToken: CancellationToken.None ).ConfigureAwait( false );
            }
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask StopGenerationAsync( string processGeneration, CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _runtimeHome is not null && string.Equals( _runtimeHome.Generation, processGeneration, StringComparison.Ordinal ) )
            {
                await StopUnderGateAsync( cancellationToken: CancellationToken.None ).ConfigureAwait( false );
            }
        }
        finally
        {
            _gate.Release( );
        }
    }

    // Login and model discovery: no session, so no admission.
    private async ValueTask<CodexAppServerClient> EnsureStartedAsync( CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            return await EnsureStartedUnderGateAsync( admission: null, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    // A process serves the authority it was admitted under. A session reuses it
    // only at that same epoch, or adopts a restricted process that login or model
    // discovery started; anything else is stopped and relaunched. Without an
    // admission a process is reused only while its epoch is current, and a new
    // one always launches restricted.
    private async ValueTask<CodexAppServerClient> EnsureStartedUnderGateAsync(
        AgentAdmission? admission,
        CancellationToken cancellationToken )
    {
        if ( _client is { IsFaulted: false } running && _runtimeHome is not null &&
            SameTrustClass( _runtimeHome.TrustLevel, TrustLevel ) )
        {
            if ( admission is null )
            {
                if ( _clientEpoch is not { } epoch || _clientAuthority?.IsCurrent( epoch ) == true )
                {
                    return running;
                }
            }
            else if ( _clientEpoch == admission.Epoch || _clientEpoch is null && TrustLevel < AgentTrustLevels.Unrestricted )
            {
                _clientEpoch = admission.Epoch;
                _clientAuthority = admission.Authority;
                return running;
            }
        }

        await StopUnderGateAsync( cancellationToken: CancellationToken.None ).ConfigureAwait( false );
        if ( admission is null )
        {
            TrustLevel = AgentTrustLevels.Default;
            WebSearchEnabled = false;
        }
        var started = await StartUnderGateAsync( cancellationToken ).ConfigureAwait( false );
        _clientEpoch = admission?.Epoch;
        _clientAuthority = admission?.Authority;
        return started;
    }

    private async ValueTask<CodexAppServerClient> StartUnderGateAsync( CancellationToken cancellationToken )
    {
        _health = AgentHealthStates.Starting;
        try
        {
            _binary = await ResolveBinaryAsync( cancellationToken ).ConfigureAwait( false );
            _runtimeHome = CodexRuntimeHome.Create( _options.RuntimeDirectory, TrustLevel, WebSearchEnabled );
            var imported = _credentials.Read( );
            try
            {
                _runtimeHome.ImportCredential( imported );
            }
            finally
            {
                if ( imported is not null )
                {
                    CodexCredentialStore.Clear( imported );
                }
            }
            CodexLaunchRequest request = new( )
            {
                BinaryPath = _binary.CanonicalPath,
                RuntimeHome = _runtimeHome.Path,
                Generation = _runtimeHome.Generation,
                TrustLevel = TrustLevel,
                WorkingDirectory = TrustLevel >= AgentTrustLevels.Unrestricted
                    ? Environment.GetFolderPath( Environment.SpecialFolder.UserProfile )
                    : _runtimeHome.Path,
                SocketPath = TrustLevel >= AgentTrustLevels.Unrestricted ? _runtimeHome.SocketPath : null
            };
            var handle = await _launcherFactory( TrustLevel ).LaunchAsync( request, cancellationToken )
                .ConfigureAwait( false );
            CodexAppServerClient client;
            try
            {
                client = await CodexAppServerClient.ConnectAsync( handle, _clientVersion, cancellationToken ).ConfigureAwait( false );
            }
            catch
            {
                await handle.DisposeAsync( ).ConfigureAwait( false );
                throw;
            }
            lock ( _authenticationLock )
            {
                _accountUpdated = authMode => OnAccountUpdated( client, authMode );
                _loginCompleted = ( success, _, loginId ) => OnLoginCompleted( client, success, loginId );
                client.AccountUpdated += _accountUpdated;
                client.LoginCompleted += _loginCompleted;
                _client = client;
            }
            _health = AgentHealthStates.Ready;
            _lastError = null;

            using var refreshTimeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            refreshTimeout.CancelAfter( TimeSpan.FromSeconds( AgentLimits.StartupDeadlineSeconds ) );
            try
            {
                await RefreshLoginAsync( client, refreshTimeout.Token ).ConfigureAwait( false );
            }
            catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
            {
                throw new AiOperationException( AiErrorCategories.Timeout, safeSummary: "Codex did not report its login state in time." );
            }

            return client;
        }
        catch ( AiOperationException exception )
        {
            _health = exception.Category == AiErrorCategories.Unsupported
                ? AgentHealthStates.Incompatible
                : AgentHealthStates.Unavailable;
            _lastError = new AiError { Category = exception.Category, Summary = exception.SafeSummary ?? "Codex could not start." };
            await StopUnderGateAsync( cancellationToken: CancellationToken.None ).ConfigureAwait( false );
            throw;
        }
        catch
        {
            await StopUnderGateAsync( cancellationToken: CancellationToken.None ).ConfigureAwait( false );
            throw;
        }
    }

    private async ValueTask<CodexBinaryInfo> ResolveBinaryAsync( CancellationToken cancellationToken )
    {
        if ( _binary is not null && CodexBinaryLocator.HasSameIdentity( _binary.CanonicalPath, _binary.IdentityToken ) )
        {
            return _binary;
        }
        return await CodexBinaryLocator.LocateAsync( _options, cancellationToken ).ConfigureAwait( false );
    }

    private async ValueTask StopUnderGateAsync( bool logout = false, CancellationToken cancellationToken = default )
    {
        CodexAppServerClient? client;
        CodexRuntimeHome? runtimeHome;
        lock ( _authenticationLock )
        {
            client = _client;
            runtimeHome = _runtimeHome;
            if ( client is not null )
            {
                client.AccountUpdated -= _accountUpdated;
                client.LoginCompleted -= _loginCompleted;
                if ( !logout )
                {
                    CopyCredentialBack( );
                }
            }

            _client = null;
            _runtimeHome = null;
            _clientEpoch = null;
            _clientAuthority = null;
            _accountUpdated = null;
            _loginCompleted = null;
            if ( _health == AgentHealthStates.Ready )
            {
                _health = AgentHealthStates.Configured;
            }

            if ( _login.State == AgentAuthenticationStates.Pending )
            {
                _pendingLoginId = null;
                _login = BuildLoginState(
                    _credentials.Exists ? AgentAuthenticationStates.Authenticated : AgentAuthenticationStates.Unauthenticated,
                    null,
                    null );
            }
        }

        try
        {
            if ( logout && client is { IsFaulted: false } )
            {
                try
                {
                    await client.LogoutAsync( cancellationToken ).ConfigureAwait( false );
                }
                catch ( JsonRpcException )
                {
                }
            }
        }
        finally
        {
            try
            {
                if ( client is not null )
                {
                    await client.DisposeAsync( ).ConfigureAwait( false );
                }
            }
            finally
            {
                runtimeHome?.Dispose( );
            }
        }
    }

    private static bool SameTrustClass( int running, int requested ) =>
        (running >= AgentTrustLevels.Unrestricted) == (requested >= AgentTrustLevels.Unrestricted);

    private static AgentLoginState BuildLoginState( string state, string? message, string? accountType ) => new( )
    {
        State = state,
        Method = accountType == "apiKey" ? AgentLoginMethods.ApiKey : null,
        Message = message,
        SupportedMethods = [AgentLoginMethods.DeviceCode, AgentLoginMethods.ApiKey]
    };

    private IAgentProcessLauncher CreateDefaultLauncher( int trustLevel ) =>
        trustLevel >= AgentTrustLevels.Unrestricted
            ? new TransientUnitCodexLauncher( _options.SystemdRunPath )
            : new BubblewrapCodexLauncher( _options.BubblewrapPath );

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        await StopAsync( CancellationToken.None ).ConfigureAwait( false );
        _gate.Dispose( );
    }
}
