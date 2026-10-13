using System.Text;

namespace CommandDeck;

public sealed partial class CliAgentBackend
{

    public async ValueTask<AgentLoginState> GetLoginStateAsync( CancellationToken cancellationToken )
    {
        lock ( _sync )
        {
            if ( _pendingLogin is not null )
            {
                return _login;
            }
            if ( _clock.GetUtcNow( ) - _loginProbedAt < StatusCacheLifetime )
            {
                return _login;
            }
        }
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _pendingLogin is not null )
            {
                return _login;
            }
            await ProbeLoginAsync( cancellationToken ).ConfigureAwait( false );
            return _login;
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask<AgentLoginState> StartLoginAsync(
        string method,
        AiCredentialOptions? credential,
        CancellationToken cancellationToken )
    {
        if ( method == AgentLoginMethods.ApiKey )
        {
            throw new AiOperationException(
                AiErrorCategories.Unsupported,
                safeSummary: $"{Profile.DisplayName} signs in through its own browser flow; API-key sign-in is not available here." );
        }
        if ( method is not (AgentLoginMethods.Browser or AgentLoginMethods.DeviceCode) )
        {
            throw new AiOperationException( AiErrorCategories.Unsupported, safeSummary: "The sign-in method is not supported." );
        }
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( _pendingLogin is not null )
            {
                return _login;
            }
            var binary = Locate( );
            Home.Ensure( );
            var scratch = Home.CreateScratch( );
            var process = await _launcher.LaunchAsync( new CliAgentLaunchRequest
            {
                BinaryPath = binary.CanonicalPath,
                InstallPath = binary.InstallPath,
                HomePath = Home.Path,
                WorkingDirectory = scratch,
                Arguments = Profile.LoginArguments,
                Environment = Profile.LoginEnvironment,
                StandardInput = Profile.RequiresPastedCode
            }, cancellationToken ).ConfigureAwait( false );
            PendingLogin pending = new( process, scratch );
            lock ( _sync )
            {
                _pendingLogin = pending;
                _login = new AgentLoginState
                {
                    State = AgentAuthenticationStates.Pending,
                    BackendId = Id,
                    Method = Profile.LoginMethod,
                    RequiresCode = Profile.RequiresPastedCode,
                    ExpiresUnixMilliseconds = (_clock.GetUtcNow( ) + LoginLifetime).ToUnixTimeMilliseconds( ),
                    Message = $"Waiting for {Profile.DisplayName} to open its sign-in page…",
                    SupportedMethods = [Profile.LoginMethod]
                };
            }
            pending.Drain = Task.Run( ( ) => DrainLoginOutputAsync( pending ), CancellationToken.None );
            pending.Watch = Task.Run( ( ) => WatchLoginExitAsync( pending ), CancellationToken.None );
            try
            {
                await pending.Harvested.Task.WaitAsync( LoginHarvestBound, cancellationToken ).ConfigureAwait( false );
            }
            catch ( TimeoutException )
            {
                // The process is still running; the state stays pending without a
                // URL until the drain finds one or the process exits.
            }
            return _login;
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask<AgentLoginState> CompleteLoginAsync( string code, CancellationToken cancellationToken )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( code );
        if ( code.Length > AiProviderSettingsLimits.MaximumLoginCodeCharacters || code.Any( char.IsControl ) )
        {
            throw new AiOperationException( AiErrorCategories.InvalidConfiguration, safeSummary: "The sign-in code is invalid." );
        }
        PendingLogin? pending;
        lock ( _sync )
        {
            pending = _pendingLogin;
        }
        if ( pending is null || !Profile.RequiresPastedCode )
        {
            throw new AiOperationException(
                AiErrorCategories.Unsupported,
                safeSummary: pending is null
                    ? "No sign-in is waiting for a code. Start the sign-in first."
                    : $"{Profile.DisplayName} completes sign-in in the browser; no code is needed." );
        }
        try
        {
            var bytes = Encoding.UTF8.GetBytes( code.Trim( ) + "\n" );
            await pending.Process.Input.WriteAsync( bytes, cancellationToken ).ConfigureAwait( false );
            await pending.Process.Input.FlushAsync( cancellationToken ).ConfigureAwait( false );
            pending.Process.Input.Close( );
        }
        catch ( Exception exception ) when ( exception is IOException or ObjectDisposedException or InvalidOperationException )
        {
            throw new AiOperationException( AiErrorCategories.Unavailable, safeSummary: "The sign-in process no longer accepts a code." );
        }
        try
        {
            await pending.Process.Exited.WaitAsync( TimeSpan.FromSeconds( 60 ), cancellationToken ).ConfigureAwait( false );
            await pending.Watch!.WaitAsync( TimeSpan.FromSeconds( 30 ), cancellationToken ).ConfigureAwait( false );
        }
        catch ( TimeoutException )
        {
            lock ( _sync )
            {
                _login = CopyLogin( _login, message: $"Waiting for {Profile.DisplayName} to confirm the code…" );
            }
        }
        return _login;
    }

    public async ValueTask<AgentLoginState> CancelLoginAsync( CancellationToken cancellationToken )
    {
        PendingLogin? pending;
        lock ( _sync )
        {
            pending = _pendingLogin;
            _pendingLogin = null;
        }
        if ( pending is not null )
        {
            await pending.DisposeAsync( ).ConfigureAwait( false );
        }
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            _loginProbedAt = DateTimeOffset.MinValue;
            await ProbeLoginAsync( cancellationToken ).ConfigureAwait( false );
            if ( _login.State != AgentAuthenticationStates.Authenticated )
            {
                _login = BuildLoginState( AgentAuthenticationStates.Unauthenticated, "Sign-in cancelled." );
            }
            return _login;
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask<AgentLoginState> LogoutAsync( CancellationToken cancellationToken )
    {
        await CancelLoginAsync( cancellationToken ).ConfigureAwait( false );
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            if ( TryLocate( out _ ) )
            {
                try
                {
                    _ = await RunBoundedAsync( Profile.LogoutArguments, ProbeBound, cancellationToken ).ConfigureAwait( false );
                }
                catch ( AiOperationException )
                {
                    // The owned credential files are removed regardless.
                }
            }
            Home.DeleteCredentials( );
            _loginProbedAt = _clock.GetUtcNow( );
            _login = BuildLoginState( AgentAuthenticationStates.Unauthenticated, $"Signed out of {Profile.DisplayName}." );
            return _login;
        }
        finally
        {
            _gate.Release( );
        }
    }
}
