using System.Text;
using System.Text.RegularExpressions;

namespace CommandDeck;

public sealed partial class CliAgentBackend
{

    private async Task DrainLoginOutputAsync( PendingLogin pending )
    {
        var buffer = new byte[4 * 1024];
        StringBuilder text = new( );
        try
        {
            while ( true )
            {
                var read = await pending.Process.Output.ReadAsync( buffer ).ConfigureAwait( false );
                if ( read <= 0 )
                {
                    break;
                }
                if ( text.Length < MaximumProbeBytes )
                {
                    text.Append( Encoding.UTF8.GetString( buffer, 0, read ) );
                }
                HarvestLoginOutput( pending, text.ToString( ) );
            }
        }
        catch ( Exception exception ) when ( exception is IOException or ObjectDisposedException or InvalidOperationException )
        {
        }
        finally
        {
            pending.Harvested.TrySetResult( );
        }
    }

    private void HarvestLoginOutput( PendingLogin pending, string text )
    {
        var url = VerificationUrl( ).Match( text );
        var code = UserCode( ).Match( text );
        lock ( _sync )
        {
            if ( !ReferenceEquals( _pendingLogin, pending ) )
            {
                return;
            }
            if ( url.Success && _login.VerificationUrl is null )
            {
                _login = CopyLogin(
                    _login,
                    message: Profile.RequiresPastedCode
                        ? $"Open the page, sign in to {Profile.AccountDescription}, then paste the code it shows."
                        : $"Open the page and finish signing in to {Profile.AccountDescription}.",
                    verificationUrl: url.Value.TrimEnd( '.', ',' ) );
            }
            if ( code.Success && _login.UserCode is null && Profile.LoginMethod == AgentLoginMethods.DeviceCode )
            {
                _login = CopyLogin( _login, message: "Confirm this code on the sign-in page.", userCode: code.Value );
            }
        }
        if ( url.Success )
        {
            pending.Harvested.TrySetResult( );
        }
    }

    private async Task WatchLoginExitAsync( PendingLogin pending )
    {
        int exitCode;
        try
        {
            exitCode = await pending.Process.Exited.ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is InvalidOperationException or IOException )
        {
            exitCode = -1;
        }
        lock ( _sync )
        {
            if ( !ReferenceEquals( _pendingLogin, pending ) )
            {
                return;
            }
            _pendingLogin = null;
        }
        await _gate.WaitAsync( ).ConfigureAwait( false );
        try
        {
            _loginProbedAt = DateTimeOffset.MinValue;
            await ProbeLoginAsync( CancellationToken.None ).ConfigureAwait( false );
            if ( _login.State == AgentAuthenticationStates.Authenticated )
            {
                _login = CopyLogin( _login, message: $"Signed in to {Profile.DisplayName}." );
            }
            else
            {
                _login = BuildLoginState(
                    exitCode == 0 ? AgentAuthenticationStates.Unauthenticated : AgentAuthenticationStates.Failed,
                    exitCode == 0 ? "Sign-in did not complete." : $"{Profile.DisplayName} sign-in failed." );
            }
        }
        finally
        {
            _gate.Release( );
            await pending.DisposeAsync( ).ConfigureAwait( false );
        }
    }

    [GeneratedRegex( @"https://[^\s""'<>]+", RegexOptions.CultureInvariant )]
    private static partial Regex VerificationUrl( );

    [GeneratedRegex( @"\b[A-Z0-9]{4}-[A-Z0-9]{4}\b", RegexOptions.CultureInvariant )]
    private static partial Regex UserCode( );

    private sealed class PendingLogin( ICliAgentProcess process, string scratch ) : IAsyncDisposable
    {
        public ICliAgentProcess Process { get; } = process;
        public TaskCompletionSource Harvested { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public Task? Drain { get; set; }
        public Task? Watch { get; set; }

        public async ValueTask DisposeAsync( )
        {
            await Process.DisposeAsync( ).ConfigureAwait( false );
            TryDeleteDirectory( scratch );
        }
    }
}
