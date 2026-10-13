using System.Text;

namespace CommandDeck;

public interface IExtensionSecretResolver
{
    ValueTask<AiCredentialLease?> ResolveAsync( string name, CancellationToken cancellationToken );
}

public sealed class ExtensionSecretResolver : IExtensionSecretResolver
{
    private readonly AiCredentialResolver _resolver = new( );
    public ValueTask<AiCredentialLease?> ResolveAsync( string name, CancellationToken cancellationToken ) =>
        _resolver.ResolveAsync( new AiCredentialOptions { Kind = AiCredentialKinds.Systemd, Name = name }, cancellationToken );
}

internal interface ICredentialSource : IDisposable
{
    string Status { get; }
    ValueTask<string?> GetAsync( CancellationToken cancellationToken );
    ValueTask<string> BeginLoginAsync( CancellationToken cancellationToken );
    ValueTask<string> DisconnectAsync( bool remoteRevoke, CancellationToken cancellationToken );
}

internal static class CredentialSourceFactory
{
    public static async ValueTask<ICredentialSource> CreateAsync( ConnectionPreparation setup, IExtensionSecretResolver secrets, CancellationToken token )
    {
        switch ( setup.Credential )
        {
            case NoCredentialModel:
                return new NoCredentialSource( );
            case OAuthCredentialModel oauth:
                return new OAuthCredentialSource( setup.Policy.ToWire( ) with { OAuth = oauth.Setup } );
            case SessionBearerModel bearer:
                return new StaticBearerSource( CredentialTokens.Lease( bearer.Token ), CredentialStatusText.SessionBearer );
            case SystemdBearerModel systemd:
                var credential = await secrets.ResolveAsync( systemd.Name, token ).ConfigureAwait( false );
                if ( credential is null )
                {
                    throw new InvalidOperationException( "The credential is unavailable or invalid." );
                }
                try
                {
                    CredentialTokens.Validate( credential.ReadUtf8( ) );
                    return new StaticBearerSource( credential, CredentialStatusText.SystemdBearer );
                }
                catch
                {
                    credential.Dispose( );
                    throw;
                }
            default:
                throw new InvalidOperationException( "Invalid credential kind." );
        }
    }
}

internal static class CredentialStatusText
{
    public const string None = "No credential · daemon session only";
    public const string SessionBearer = "Bearer credential · memory only";
    public const string SystemdBearer = "Systemd credential · host attached";
    public const string OAuthInitial = "Not connected · OAuth tokens will remain in memory";
    public const string OAuthWaiting = "Waiting for browser · 3 minute deadline";
    public const string OAuthConnected = "Connected · OAuth tokens in memory only";
    public const string OAuthExpired = "Login expired · reconnect explicitly";
    public const string OAuthFailed = "Login failed or cancelled · reconnect explicitly";
    public const string Disconnected = "Disconnected";
    public const string LocalDisconnect = "Local credential disconnected; remote credentials may remain valid.";
}

internal static class CredentialTokens
{
    public static AiCredentialLease Lease( string value )
    {
        Validate( value );
        return new( Encoding.UTF8.GetBytes( value ) );
    }

    public static void Validate( string value )
    {
        if ( value.Length is < 1 or > 8192 || value.Any( character => character <= 32 || character > 126 ) )
        {
            throw new InvalidOperationException( "The credential is unavailable or invalid." );
        }
    }
}

internal sealed class NoCredentialSource : ICredentialSource
{
    public string Status => CredentialStatusText.None;
    public ValueTask<string?> GetAsync( CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        return ValueTask.FromResult<string?>( null );
    }
    public ValueTask<string> BeginLoginAsync( CancellationToken cancellationToken ) => throw new InvalidOperationException( "OAuth is unavailable or already in progress." );
    public ValueTask<string> DisconnectAsync( bool remoteRevoke, CancellationToken cancellationToken ) => ValueTask.FromResult( CredentialStatusText.LocalDisconnect );
    public void Dispose( ) { }
}

internal sealed class StaticBearerSource( AiCredentialLease credential, string status ) : ICredentialSource
{
    private readonly object _gate = new( );
    private bool _disposed;
    public string Status => _disposed ? CredentialStatusText.Disconnected : status;
    public ValueTask<string?> GetAsync( CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        lock ( _gate )
        {
            ObjectDisposedException.ThrowIf( _disposed, this );
            return ValueTask.FromResult<string?>( credential.ReadUtf8( ) );
        }
    }
    public ValueTask<string> BeginLoginAsync( CancellationToken cancellationToken ) => throw new InvalidOperationException( "OAuth is unavailable or already in progress." );
    public ValueTask<string> DisconnectAsync( bool remoteRevoke, CancellationToken cancellationToken )
    {
        Dispose( );
        return ValueTask.FromResult( CredentialStatusText.LocalDisconnect );
    }
    public void Dispose( )
    {
        lock ( _gate )
        {
            if ( !_disposed )
            {
                _disposed = true;
                credential.Dispose( );
            }
        }
    }
}
