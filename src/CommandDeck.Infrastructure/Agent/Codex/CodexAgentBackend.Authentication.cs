namespace CommandDeck;

public sealed partial class CodexAgentBackend
{
    public async ValueTask<AgentLoginState> GetLoginStateAsync( CancellationToken cancellationToken )
    {
        CodexAppServerClient? client;
        lock ( _authenticationLock )
        {
            client = _client;
        }

        if ( client is { IsFaulted: false } )
        {
            try
            {
                var account = await client.ReadAccountAsync( cancellationToken ).ConfigureAwait( false );
                lock ( _authenticationLock )
                {
                    if ( ReferenceEquals( _client, client ) && _login.State != AgentAuthenticationStates.Pending )
                    {
                        _login = BuildLoginState( account.Authenticated ? AgentAuthenticationStates.Authenticated : AgentAuthenticationStates.Unauthenticated, null, account.Type );
                    }
                }
            }
            catch ( Exception exception ) when ( exception is JsonRpcException or OperationCanceledException or ObjectDisposedException )
            {
            }
        }

        lock ( _authenticationLock )
        {
            return _login;
        }
    }

    public async ValueTask<AgentLoginState> StartLoginAsync(
        string method,
        AiCredentialOptions? credential,
        CancellationToken cancellationToken )
    {
        var client = await EnsureStartedAsync( cancellationToken ).ConfigureAwait( false );
        if ( method == AgentLoginMethods.ApiKey )
        {
            return await StartApiKeyLoginAsync( client, credential, cancellationToken ).ConfigureAwait( false );
        }

        var start = await client.StartDeviceCodeLoginAsync( cancellationToken ).ConfigureAwait( false );
        lock ( _authenticationLock )
        {
            EnsureCurrentClient( client );
            _pendingLoginId = start.LoginId;
            _login = new AgentLoginState
            {
                State = AgentAuthenticationStates.Pending,
                Method = AgentLoginMethods.DeviceCode,
                UserCode = start.UserCode,
                VerificationUrl = start.VerificationUrl,
                ExpiresUnixMilliseconds = DateTimeOffset.UtcNow.AddMinutes( 15 ).ToUnixTimeMilliseconds( ),
                Message = "Enter the code at the verification page to finish signing in.",
                SupportedMethods = [AgentLoginMethods.DeviceCode, AgentLoginMethods.ApiKey]
            };
            return _login;
        }
    }

    public async ValueTask<AgentLoginState> CancelLoginAsync( CancellationToken cancellationToken )
    {
        CodexAppServerClient? client;
        string? loginId;
        lock ( _authenticationLock )
        {
            client = _client;
            loginId = _pendingLoginId;
        }

        if ( loginId is not null && client is { IsFaulted: false } )
        {
            try
            {
                await client.CancelLoginAsync( loginId, cancellationToken ).ConfigureAwait( false );
            }
            catch ( Exception exception ) when ( exception is JsonRpcException or ObjectDisposedException )
            {
            }
        }

        lock ( _authenticationLock )
        {
            if ( ReferenceEquals( _client, client ) && _pendingLoginId == loginId )
            {
                _pendingLoginId = null;
                _login = BuildLoginState(
                    _credentials.Exists ? AgentAuthenticationStates.Authenticated : AgentAuthenticationStates.Unauthenticated,
                    "Login cancelled.",
                    null );
            }

            return _login;
        }
    }

    public async ValueTask<AgentLoginState> LogoutAsync( CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            try
            {
                await StopUnderGateAsync( logout: true, cancellationToken ).ConfigureAwait( false );
            }
            finally
            {
                lock ( _authenticationLock )
                {
                    _credentials.Delete( );
                    _pendingLoginId = null;
                    _login = BuildLoginState( AgentAuthenticationStates.Unauthenticated, "Signed out of Codex.", null );
                }
            }

            return _login;
        }
        finally
        {
            _gate.Release( );
        }
    }

    private async ValueTask RefreshLoginAsync( CodexAppServerClient client, CancellationToken cancellationToken )
    {
        try
        {
            var account = await client.ReadAccountAsync( cancellationToken ).ConfigureAwait( false );
            lock ( _authenticationLock )
            {
                _login = BuildLoginState(
                    account.Authenticated ? AgentAuthenticationStates.Authenticated : AgentAuthenticationStates.Unauthenticated,
                    null,
                    account.Type );
            }
        }
        catch ( JsonRpcException )
        {
            lock ( _authenticationLock )
            {
                _login = BuildLoginState( AgentAuthenticationStates.Unauthenticated, "Codex could not report its login state.", null );
            }
        }
    }

    private async ValueTask<AgentLoginState> StartApiKeyLoginAsync(
        CodexAppServerClient client,
        AiCredentialOptions? credential,
        CancellationToken cancellationToken )
    {
        if ( credential is null || credential.Kind == AiCredentialKinds.None )
        {
            throw new AiOperationException( AiErrorCategories.CredentialMissing, safeSummary: "An API key credential reference is required." );
        }

        using var lease = await _credentialResolver.ResolveAsync( credential, cancellationToken ).ConfigureAwait( false );
        if ( lease is null )
        {
            throw new AiOperationException( AiErrorCategories.CredentialMissing, safeSummary: "The API key credential is unavailable." );
        }

        await client.StartApiKeyLoginAsync( lease.ReadUtf8( ), cancellationToken ).ConfigureAwait( false );
        lock ( _authenticationLock )
        {
            EnsureCurrentClient( client );
            CopyCredentialBack( );
            _login = BuildLoginState( AgentAuthenticationStates.Authenticated, "Signed in with an API key.", "apiKey" );
            return _login;
        }
    }

    // Call only while holding _authenticationLock.
    private void EnsureCurrentClient( CodexAppServerClient client )
    {
        if ( !ReferenceEquals( _client, client ) )
        {
            throw new OperationCanceledException( "The Codex runtime was stopped." );
        }
    }

    private void OnAccountUpdated( CodexAppServerClient client, string? authMode )
    {
        lock ( _authenticationLock )
        {
            if ( !ReferenceEquals( _client, client ) )
            {
                return;
            }

            CopyCredentialBack( );
            if ( authMode is null )
            {
                _login = BuildLoginState( AgentAuthenticationStates.Unauthenticated, null, null );
            }
        }
    }

    private void OnLoginCompleted( CodexAppServerClient client, bool success, string? loginId )
    {
        lock ( _authenticationLock )
        {
            if ( !ReferenceEquals( _client, client ) ||
                (loginId is not null && _pendingLoginId is not null && loginId != _pendingLoginId) )
            {
                return;
            }

            _pendingLoginId = null;
            if ( success )
            {
                CopyCredentialBack( );
                _login = BuildLoginState( AgentAuthenticationStates.Authenticated, "Signed in to Codex.", null );
            }
            else if ( _login.State == AgentAuthenticationStates.Pending )
            {
                _login = BuildLoginState( AgentAuthenticationStates.Failed, "Login did not complete.", null );
            }
        }
    }

    // Call only while holding _authenticationLock, so retirement and credential writes are ordered.
    private void CopyCredentialBack( )
    {
        var exported = _runtimeHome?.ExportCredential( );
        if ( exported is null )
        {
            return;
        }

        try
        {
            _credentials.Write( exported );
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidOperationException )
        {
        }
        finally
        {
            CodexCredentialStore.Clear( exported );
        }
    }
}
