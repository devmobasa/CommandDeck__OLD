using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CommandDeck;

/// <summary>Tokens stay in the host. OAuth is explicit, session-only and refreshed only for an admitted request.</summary>
internal sealed class OAuthCredentialSource : ICredentialSource
{
    private readonly SemaphoreSlim _gate = new( 1, 1 );
    private readonly CancellationTokenSource _lifetime = new( );
    private readonly CancellationToken _token;
    private AiCredentialLease? _access, _refresh;
    private readonly ExtensionConnectionSetup _setup;
    private readonly OAuthTokenClient _client;
    private DateTimeOffset _expires = DateTimeOffset.MaxValue;
    private bool _loginPending;
    private int _disposed;
    private enum CredentialState { Initial, Waiting, Connected, Expired, Failed, Disconnected }
    private int _state;
    public string Status => ( CredentialState ) Volatile.Read( ref _state ) switch
    {
        CredentialState.Initial => CredentialStatusText.OAuthInitial,
        CredentialState.Waiting => CredentialStatusText.OAuthWaiting,
        CredentialState.Connected => CredentialStatusText.OAuthConnected,
        CredentialState.Expired => CredentialStatusText.OAuthExpired,
        CredentialState.Failed => CredentialStatusText.OAuthFailed,
        CredentialState.Disconnected => CredentialStatusText.Disconnected,
        _ => throw new InvalidOperationException( "Invalid credential state." )
    };
    public OAuthCredentialSource( ExtensionConnectionSetup setup )
    {
        ValidateOAuth( setup );
        _setup = setup with { Credential = null };
        _client = new OAuthTokenClient( _setup );
        _token = _lifetime.Token;
    }

    private static void ValidateOAuth( ExtensionConnectionSetup setup )
    {
        var oauth = setup.OAuth;
        if ( oauth is null )
        {
            throw new InvalidOperationException( "OAuth setup is missing." );
        }

        var origin = ExtensionHttpTransport.Origin( oauth.Issuer, setup.PrivateNetwork );
        if ( oauth.ClientId is not { Length: > 0 and <= 256 } || oauth.Scope is not { Length: <= 1024 } || oauth.ClientId.Any( char.IsControl ) || oauth.Scope.Any( char.IsControl ) )
        {
            throw new InvalidOperationException( "Invalid OAuth client or scopes." );
        }
        foreach ( var value in new[] { oauth.AuthorizationEndpoint, oauth.TokenEndpoint, oauth.RevocationEndpoint }.Where( value => value is not null ) )
        {
            if ( !Uri.TryCreate( value, UriKind.Absolute, out var endpoint ) || endpoint.GetLeftPart( UriPartial.Authority ) != origin.GetLeftPart( UriPartial.Authority ) ||
                endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0 )
            {
                throw new InvalidOperationException( "OAuth endpoints must belong to the configured issuer origin without queries or credentials." );
            }
            ExtensionHttpTransport.PathValue( endpoint.AbsolutePath );
        }
    }

    public async ValueTask<string?> GetAsync( CancellationToken token )
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( token, _token );
        await _gate.WaitAsync( lifetime.Token ).ConfigureAwait( false );
        try
        {
            lifetime.Token.ThrowIfCancellationRequested( );
            if ( DateTimeOffset.UtcNow >= _expires )
            {
                if ( _refresh is null )
                {
                    throw new InvalidOperationException( "Login expired. Connect again." );
                }
                try
                {
                    using var response = await _client.RequestAsync( new( )
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = _refresh.ReadUtf8( ),
                        ["client_id"] = _setup.OAuth!.ClientId
                    }, lifetime.Token ).ConfigureAwait( false );
                    AcceptTokens( response.RootElement, retainRefresh: true );
                }
                catch
                {
                    Clear( );
                    Volatile.Write( ref _state, ( int ) CredentialState.Expired );
                    throw;
                }
            }
            var bearer = _access?.ReadUtf8( );
            if ( bearer is null )
            {
                throw new InvalidOperationException( "Connect this service first." );
            }

            return bearer;
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask<string> BeginLoginAsync( CancellationToken token )
    {
        await _gate.WaitAsync( token ).ConfigureAwait( false );
        try
        {
            _token.ThrowIfCancellationRequested( );
            if ( _loginPending )
            {
                throw new InvalidOperationException( "OAuth is unavailable or already in progress." );
            }
            var oauth = _setup.OAuth!;
            var verifier = Base64Url( RandomNumberGenerator.GetBytes( 32 ) );
            var state = Base64Url( RandomNumberGenerator.GetBytes( 32 ) );
            var listener = new TcpListener( IPAddress.Loopback, 0 );
            listener.Start( 4 );
            var redirect = "http://127.0.0.1:" + (( IPEndPoint ) listener.LocalEndpoint).Port + "/callback";
            var parameters = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = oauth.ClientId,
                ["redirect_uri"] = redirect,
                ["scope"] = oauth.Scope,
                ["state"] = state,
                ["code_challenge_method"] = "S256",
                ["code_challenge"] = Base64Url( SHA256.HashData( Encoding.ASCII.GetBytes( verifier ) ) )
            };
            Volatile.Write( ref _state, ( int ) CredentialState.Waiting );
            _loginPending = true;
            _ = CompleteLoginAsync( listener, redirect, state, verifier );
            return oauth.AuthorizationEndpoint + "?" + string.Join( "&", parameters.Select( pair => Uri.EscapeDataString( pair.Key ) + "=" + Uri.EscapeDataString( pair.Value ) ) );
        }
        finally
        {
            _gate.Release( );
        }
    }

    private async Task CompleteLoginAsync( TcpListener listener, string redirect, string state, string verifier )
    {
        var failed = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource( _token );
        deadline.CancelAfter( TimeSpan.FromMinutes( 3 ) );
        try
        {
            using var socket = await listener.AcceptTcpClientAsync( deadline.Token ).ConfigureAwait( false );
            listener.Stop( );
            await using var stream = socket.GetStream( );
            var code = await OAuthLoopbackReceiver.ReadCodeAsync( stream, redirect, state, _setup.OAuth!.Issuer, deadline.Token ).ConfigureAwait( false );
            await _gate.WaitAsync( deadline.Token ).ConfigureAwait( false );
            try
            {
                using var tokens = await _client.RequestAsync( new( )
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirect,
                    ["client_id"] = _setup.OAuth.ClientId,
                    ["code_verifier"] = verifier
                }, deadline.Token ).ConfigureAwait( false );
                deadline.Token.ThrowIfCancellationRequested( );
                AcceptTokens( tokens.RootElement, false );
            }
            finally
            {
                _gate.Release( );
            }
            await stream.WriteAsync( "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\nConnected. You can return to CommandDeck.\n"u8.ToArray( ), deadline.Token ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidDataException or HttpRequestException or SocketException or OperationCanceledException or InvalidOperationException or JsonException or FormatException )
        {
            failed = true;
        }
        finally
        {
            listener.Stop( );
            // Release admission before publishing failure. A retry that sees
            // that failure can start immediately, under the same login gate.
            await _gate.WaitAsync( ).ConfigureAwait( false );
            try
            {
                _loginPending = false;
                if ( failed )
                {
                    Volatile.Write( ref _state, ( int ) CredentialState.Failed );
                }
            }
            finally
            {
                _gate.Release( );
            }
        }
    }

    private void AcceptTokens( JsonElement response, bool retainRefresh )
    {
        if ( !response.TryGetProperty( "token_type", out var type ) || !string.Equals( type.GetString( ), "Bearer", StringComparison.OrdinalIgnoreCase ) ||
            !response.TryGetProperty( "access_token", out var access ) || !response.TryGetProperty( "expires_in", out var expires ) ||
            !expires.TryGetInt32( out var seconds ) || seconds is < 1 or > 86400 )
        {
            throw new InvalidOperationException( "Unsupported token response." );
        }
        var lease = CredentialTokens.Lease( access.GetString( ) ?? "" );
        AiCredentialLease? refresh = null;
        try
        {
            if ( response.TryGetProperty( "refresh_token", out var value ) )
            {
                refresh = CredentialTokens.Lease( value.GetString( ) ?? "" );
            }
        }
        catch
        {
            lease.Dispose( );
            throw;
        }
        _access?.Dispose( );
        _access = lease;
        if ( refresh is not null || !retainRefresh )
        {
            _refresh?.Dispose( );
            _refresh = refresh;
        }
        _expires = DateTimeOffset.UtcNow.AddSeconds( seconds );
        Volatile.Write( ref _state, ( int ) CredentialState.Connected );
    }
    public async ValueTask<string> DisconnectAsync( bool remote, CancellationToken token )
    {
        _lifetime.Cancel( );
        await _gate.WaitAsync( token ).ConfigureAwait( false );
        try
        {
            var result = CredentialStatusText.LocalDisconnect;
            if ( remote && _setup?.OAuth?.RevocationEndpoint is { } endpoint && (_refresh ?? _access) is { } credential )
            {
                try
                {
                    var response = await _client.PostAsync( endpoint, new( )
                    {
                        ["token"] = credential.ReadUtf8( ),
                        ["client_id"] = _setup.OAuth.ClientId
                    }, token ).ConfigureAwait( false );
                    result = response.StatusCode == 200 ? "Local credential disconnected; remote revocation acknowledged." : "Local credential disconnected; remote revocation was not acknowledged.";
                }
                catch ( Exception exception ) when ( exception is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException )
                {
                    result = "Local credential disconnected; remote revocation outcome is unknown.";
                }
            }
            Clear( );
            return result;
        }
        finally
        {
            _gate.Release( );
        }
    }
    private static string Base64Url( byte[] value ) => Convert.ToBase64String( value ).TrimEnd( '=' ).Replace( '+', '-' ).Replace( '/', '_' );
    private void Clear( )
    {
        _access?.Dispose( );
        _refresh?.Dispose( );
        _access = null;
        _refresh = null;
    }
    public void Dispose( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        _lifetime.Cancel( );
        // An active refresh/login owns the leases until cancellation is observed.
        _ = ClearAfterUseAsync( );
    }
    private async Task ClearAfterUseAsync( )
    {
        await _gate.WaitAsync( ).ConfigureAwait( false );
        try
        {
            Clear( );
            Volatile.Write( ref _state, ( int ) CredentialState.Disconnected );
        }
        finally
        {
            _gate.Release( );
            _lifetime.Dispose( );
        }
    }
}
