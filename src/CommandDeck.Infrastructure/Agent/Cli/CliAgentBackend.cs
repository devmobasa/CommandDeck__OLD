using System.Text;

namespace CommandDeck;

public sealed record CliAgentBinary( string CanonicalPath, string InstallPath, string IdentityToken );

/// <summary>
/// Locates a CLI agent binary: an explicit configured path wins, then PATH by
/// name, then the profile's fallback locations. The canonical target must be a
/// regular file owned by the user or root and not group- or world-writable,
/// the same rule the Codex launcher applies.
/// </summary>
public static class CliAgentBinaryLocator
{
    public static CliAgentBinary Locate( CliAgentProfile profile, string? configuredPath )
    {
        ArgumentNullException.ThrowIfNull( profile );
        var candidate = string.IsNullOrWhiteSpace( configuredPath ) ? null : PathExpansion.Expand( configuredPath );
        candidate ??= profile.ExecutableNames.Select( CodexBinaryLocator.FindOnPath ).FirstOrDefault( static value => value is not null );
        candidate ??= profile.FallbackPaths.Select( PathExpansion.Expand ).FirstOrDefault( File.Exists );
        if ( candidate is null )
        {
            throw new AiOperationException(
                AiErrorCategories.Unavailable,
                safeSummary: $"The {profile.DisplayName} CLI is not installed or not on the daemon's PATH." );
        }
        var canonical = CodexBinaryLocator.ResolveCanonical( candidate );
        var identity = CodexBinaryLocator.ValidateOwnership( canonical );
        var install = Path.GetDirectoryName( canonical );
        if ( install is null )
        {
            throw new AiOperationException( AiErrorCategories.Unavailable, safeSummary: "The CLI install path is invalid." );
        }

        return new CliAgentBinary( canonical, install, identity );
    }
}

/// <summary>
/// An answer-only assistant backend driven through a CLI agent (Claude Code,
/// Cursor, Grok). CommandDeck owns the home the CLI signs into, runs every
/// process inside Bubblewrap, and mediates nothing else: these backends expose
/// no CommandDeck tools, so their own tools are disabled or confined by flag.
/// </summary>
public sealed partial class CliAgentBackend : IAgentBackend, IAgentCodeLoginBackend
{
    private static readonly TimeSpan StatusCacheLifetime = TimeSpan.FromSeconds( 10 );
    private static readonly TimeSpan LoginHarvestBound = TimeSpan.FromSeconds( 15 );
    private static readonly TimeSpan LoginLifetime = TimeSpan.FromMinutes( 15 );
    private static readonly TimeSpan ProbeBound = TimeSpan.FromSeconds( 20 );
    private static readonly TimeSpan ModelsBound = TimeSpan.FromSeconds( 30 );
    private const int MaximumProbeBytes = 64 * 1024;
    private readonly AgentOptions _options;
    private readonly ICliAgentLauncher _launcher;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new( 1, 1 );
    private readonly object _sync = new( );
    private CliAgentBinary? _binary;
    private AgentLoginState _login;
    private DateTimeOffset _loginProbedAt = DateTimeOffset.MinValue;
    private PendingLogin? _pendingLogin;
    private ICliAgentProcess? _activeTurn;
    private long? _activeTurnEpoch;
    private AiError? _lastError;
    private int _disposed;

    public CliAgentBackend(
        CliAgentProfile profile,
        AgentOptions options,
        ICliAgentLauncher? launcher = null,
        TimeProvider? clock = null )
    {
        if ( profile is null )
        {
            throw new ArgumentNullException( nameof( profile ) );
        }

        Profile = profile;
        if ( options is null )
        {
            throw new ArgumentNullException( nameof( options ) );
        }

        _options = options;
        _launcher = launcher ?? new BubblewrapCliAgentLauncher( options.BubblewrapPath );
        _clock = clock ?? TimeProvider.System;
        Home = new CliAgentHome( profile, options.StateDirectory, options.RuntimeDirectory );
        // Until a probe runs, the owned home's credential files are the only
        // evidence; nothing is spawned to paint a descriptor.
        _login = BuildLoginState(
            Home.CredentialExists ? AgentAuthenticationStates.Authenticated : AgentAuthenticationStates.Unauthenticated,
            null );
    }

    public CliAgentProfile Profile { get; }

    public CliAgentHome Home { get; }

    public string Id => Profile.BackendId;

    public bool IsRunning
    {
        get
        {
            lock ( _sync )
            {
                return _activeTurn is not null || _pendingLogin is not null;
            }
        }
    }

    public string? BinaryPath => _binary?.CanonicalPath;

    public AgentBackendDescriptor Describe( )
    {
        var available = _binary is not null || TryLocate( out _ );
        return new AgentBackendDescriptor
        {
            Id = Id,
            DisplayName = Profile.DisplayName,
            Kind = Profile.Kind,
            ProcessLocality = AiLocality.Local,
            InferenceLocality = AiLocality.Remote,
            Available = available,
            HealthState = !available
                ? AgentHealthStates.Unavailable
                : IsRunning ? AgentHealthStates.Ready : AgentHealthStates.Configured,
            AuthenticationState = _login.State,
            Capabilities = new AgentBackendCapabilities
            {
                Conversation = true,
                Tools = false,
                Reasoning = true,
                ModelDiscovery = true,
                PreflightRequired = false,
                Usage = true,
                Login = true,
                ContractVersion = Profile.ContractVersion
            },
            Explanation = _lastError?.Summary ?? (available
                ? $"{Profile.DisplayName} runs locally in a sandbox; inference uses {Profile.AccountDescription}. Answers only: no CommandDeck tools."
                : $"Install the {Profile.DisplayName} CLI to use this backend."),
            Error = _lastError
        };
    }

    public async ValueTask<AgentModelInventory> DiscoverModelsAsync( bool includeHidden, CancellationToken cancellationToken )
    {
        try
        {
            if ( Profile.ModelsArguments is null )
            {
                return new AgentModelInventory
                {
                    BackendId = Id,
                    Models = [.. Profile.StaticModels],
                    DefaultModelId = Profile.StaticModels.FirstOrDefault( static model => model.IsDefault )?.Id
                };
            }
            var output = await RunBoundedAsync( Profile.ModelsArguments, ModelsBound, cancellationToken ).ConfigureAwait( false );
            var models = Profile.ParseModels( output.StandardOutput );
            if ( models.Count == 0 )
            {
                return new AgentModelInventory
                {
                    BackendId = Id,
                    Error = new AiError
                    {
                        Category = output.ExitCode == 0 ? AiErrorCategories.MalformedResponse : AiErrorCategories.Authentication,
                        Summary = output.ExitCode == 0
                            ? $"{Profile.DisplayName} returned no usable model list."
                            : $"{Profile.DisplayName} could not list models; sign in first."
                    }
                };
            }
            return new AgentModelInventory
            {
                BackendId = Id,
                Models = [.. models],
                DefaultModelId = models.FirstOrDefault( static model => model.IsDefault )?.Id
            };
        }
        catch ( AiOperationException exception )
        {
            return new AgentModelInventory
            {
                BackendId = Id,
                Error = new AiError { Category = exception.Category, Summary = exception.SafeSummary }
            };
        }
    }

    public ValueTask<IAgentSession> StartSessionAsync( AgentSessionOptions options, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( options );
        ObjectDisposedException.ThrowIf( _disposed != 0, this );
        _ = Locate( );
        return ValueTask.FromResult<IAgentSession>( new CliAgentSession( this, options ) );
    }

    public async ValueTask StopAsync( CancellationToken cancellationToken )
    {
        ICliAgentProcess? turn;
        PendingLogin? pending;
        lock ( _sync )
        {
            turn = _activeTurn;
            _activeTurn = null;
            pending = _pendingLogin;
            _pendingLogin = null;
        }
        turn?.Terminate( );
        if ( pending is not null )
        {
            await pending.DisposeAsync( ).ConfigureAwait( false );
        }
        Home.WipeScratch( );
    }

    // Ends a turn admitted before the epoch; a turn admitted at or after it
    // belongs to the newer authority and keeps running.
    public async ValueTask StopBeforeAsync( long authorityEpoch, CancellationToken cancellationToken )
    {
        ICliAgentProcess? turn = null;
        PendingLogin? pending;
        lock ( _sync )
        {
            if ( _activeTurn is not null && (_activeTurnEpoch is null || _activeTurnEpoch < authorityEpoch) )
            {
                turn = _activeTurn;
                _activeTurn = null;
            }
            pending = _pendingLogin;
            _pendingLogin = null;
        }
        turn?.Terminate( );
        if ( pending is not null )
        {
            await pending.DisposeAsync( ).ConfigureAwait( false );
        }
        // Each session and pending login deletes its own scratch directory.
        // A shared wipe could reach work started while login disposal awaited.
    }

    // Sessions own their per-turn processes, so disposing a session already
    // ends its work; there is no shared process generation to stop.
    public ValueTask StopGenerationAsync( string processGeneration, CancellationToken cancellationToken ) =>
        ValueTask.CompletedTask;

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        await StopAsync( CancellationToken.None ).ConfigureAwait( false );
        _gate.Dispose( );
    }

    internal CliAgentBinary Locate( )
    {
        if ( _binary is not null && CodexBinaryLocator.HasSameIdentity( _binary.CanonicalPath, _binary.IdentityToken ) )
        {
            return _binary;
        }
        try
        {
            _binary = CliAgentBinaryLocator.Locate( Profile, ConfiguredBinaryPath( ) );
            _lastError = null;
            return _binary;
        }
        catch ( AiOperationException exception )
        {
            _lastError = new AiError { Category = exception.Category, Summary = exception.SafeSummary };
            throw;
        }
    }

    private DateTimeOffset _locateFailedAt = DateTimeOffset.MinValue;

    private bool TryLocate( out CliAgentBinary? binary )
    {
        // A missing CLI is re-checked at most every 30 seconds so descriptors
        // stay cheap while the launcher searches on every keystroke.
        if ( _binary is null && _clock.GetUtcNow( ) - _locateFailedAt < TimeSpan.FromSeconds( 30 ) )
        {
            binary = null;
            return false;
        }
        try
        {
            binary = Locate( );
            return true;
        }
        catch ( AiOperationException )
        {
            _locateFailedAt = _clock.GetUtcNow( );
            binary = null;
            return false;
        }
    }

    private string? ConfiguredBinaryPath( ) => Id switch
    {
        AgentBackendIds.ClaudeCode => _options.ClaudeBinaryPath,
        AgentBackendIds.Cursor => _options.CursorBinaryPath,
        AgentBackendIds.Grok => _options.GrokBinaryPath,
        _ => null
    };

    internal ICliAgentLauncher Launcher => _launcher;

    internal void RegisterTurn( ICliAgentProcess process, long? authorityEpoch )
    {
        lock ( _sync )
        {
            _activeTurn = process;
            _activeTurnEpoch = authorityEpoch;
        }
    }

    internal void UnregisterTurn( ICliAgentProcess process )
    {
        lock ( _sync )
        {
            if ( ReferenceEquals( _activeTurn, process ) )
            {
                _activeTurn = null;
            }
        }
    }

    internal void ReportTurnError( AiError? error ) => _lastError = error;

    private async ValueTask ProbeLoginAsync( CancellationToken cancellationToken )
    {
        if ( !TryLocate( out _ ) )
        {
            _login = BuildLoginState( AgentAuthenticationStates.Unauthenticated, $"Install the {Profile.DisplayName} CLI to sign in." );
            _loginProbedAt = _clock.GetUtcNow( );
            return;
        }
        if ( Profile.StatusArguments is null )
        {
            _login = BuildLoginState(
                Home.CredentialExists ? AgentAuthenticationStates.Authenticated : AgentAuthenticationStates.Unauthenticated,
                null );
            _loginProbedAt = _clock.GetUtcNow( );
            return;
        }
        try
        {
            var output = await RunBoundedAsync( Profile.StatusArguments, ProbeBound, cancellationToken ).ConfigureAwait( false );
            var parsed = Profile.ParseStatus( output.StandardOutput );
            _login = parsed is null
                ? BuildLoginState( AgentAuthenticationStates.Unauthenticated, $"{Profile.DisplayName} did not report its sign-in state." )
                : BuildLoginState(
                    parsed.Value.LoggedIn ? AgentAuthenticationStates.Authenticated : AgentAuthenticationStates.Unauthenticated,
                    null,
                    parsed.Value.Method );
        }
        catch ( AiOperationException exception )
        {
            _login = BuildLoginState( AgentAuthenticationStates.Unauthenticated, exception.SafeSummary );
        }
        _loginProbedAt = _clock.GetUtcNow( );
    }

    private static AgentLoginState CopyLogin(
        AgentLoginState source,
        string? message = null,
        string? verificationUrl = null,
        string? userCode = null ) => new( )
        {
            State = source.State,
            BackendId = source.BackendId,
            Method = source.Method,
            RequiresCode = source.RequiresCode,
            UserCode = userCode ?? source.UserCode,
            VerificationUrl = verificationUrl ?? source.VerificationUrl,
            ExpiresUnixMilliseconds = source.ExpiresUnixMilliseconds,
            Message = message ?? source.Message,
            SupportedMethods = source.SupportedMethods
        };

    private AgentLoginState BuildLoginState( string state, string? message, string? accountMethod = null ) => new( )
    {
        State = state,
        BackendId = Id,
        Method = accountMethod,
        Message = message,
        SupportedMethods = [Profile.LoginMethod]
    };

    private async ValueTask<(int ExitCode, string StandardOutput)> RunBoundedAsync(
        IReadOnlyList<string> arguments,
        TimeSpan bound,
        CancellationToken cancellationToken )
    {
        var binary = Locate( );
        Home.Ensure( );
        var scratch = Home.CreateScratch( );
        try
        {
            await using var process = await _launcher.LaunchAsync( new CliAgentLaunchRequest
            {
                BinaryPath = binary.CanonicalPath,
                InstallPath = binary.InstallPath,
                HomePath = Home.Path,
                WorkingDirectory = scratch,
                Arguments = arguments
            }, cancellationToken ).ConfigureAwait( false );
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            deadline.CancelAfter( bound );
            try
            {
                var output = await ReadBoundedAsync( process.Output, MaximumProbeBytes, deadline.Token ).ConfigureAwait( false );
                var exit = await process.Exited.WaitAsync( deadline.Token ).ConfigureAwait( false );
                return (exit, output);
            }
            catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
            {
                process.Terminate( );
                throw new AiOperationException( AiErrorCategories.Timeout, safeSummary: $"{Profile.DisplayName} did not respond in time." );
            }
        }
        finally
        {
            TryDeleteDirectory( scratch );
        }
    }

    private static async ValueTask<string> ReadBoundedAsync( Stream stream, int maximumBytes, CancellationToken cancellationToken )
    {
        using MemoryStream captured = new( );
        var buffer = new byte[8 * 1024];
        while ( captured.Length < maximumBytes )
        {
            var read = await stream.ReadAsync( buffer, cancellationToken ).ConfigureAwait( false );
            if ( read <= 0 )
            {
                break;
            }
            captured.Write( buffer, 0, ( int ) Math.Min( read, maximumBytes - captured.Length ) );
        }
        return Encoding.UTF8.GetString( captured.GetBuffer( ), 0, ( int ) captured.Length );
    }

    internal static void TryDeleteDirectory( string path )
    {
        try
        {
            if ( Directory.Exists( path ) )
            {
                Directory.Delete( path, recursive: true );
            }
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
        {
        }
    }
}
