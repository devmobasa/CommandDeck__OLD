using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task CodexLogoutDoesNotWaitForAccountReadAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-codex-logout-read-" ).FullName;
        try
        {
            var options = CreateAgentOptions( root, CreateFakeCodexBinary( root ) );
            CodexCredentialStore credentials = new( options.StateDirectory );
            FakeCodexLauncher launcher = new( );
            await using CodexAgentBackend backend = new( options, credentials, _ => launcher, clientVersion: "spec" );
            await backend.StartLoginAsync( AgentLoginMethods.DeviceCode, null, default );
            launcher.Current!.CompleteLogin( success: true );
            True( await WaitUntilAsync( ( ) => Task.FromResult( credentials.Exists ), TimeSpan.FromSeconds( 5 ) ) );

            var server = launcher.Current;
            var runtimeHome = launcher.Requests[0].RuntimeHome;
            server.HoldAccountReadReply = true;
            using CancellationTokenSource cancellation = new( );
            var read = backend.GetLoginStateAsync( cancellation.Token ).AsTask( );
            try
            {
                await server.AccountReadReceived.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                var logout = backend.LogoutAsync( default ).AsTask( );
                True( await WaitUntilAsync(
                    ( ) => Task.FromResult( !backend.IsRunning && !credentials.Exists && !Directory.Exists( runtimeHome ) ),
                    TimeSpan.FromSeconds( 2 ) ), "An unanswered account read must not block local sign-out." );
                Equal( AgentAuthenticationStates.Unauthenticated, (await logout.WaitAsync( TimeSpan.FromSeconds( 5 ) )).State );
                await read.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                Equal( AgentAuthenticationStates.Unauthenticated, (await backend.GetLoginStateAsync( default )).State );
                True( server.StopRequested );
            }
            finally
            {
                cancellation.Cancel( );
                await read.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task CodexStartupAccountReadHasDeadlineAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-codex-start-read-" ).FullName;
        try
        {
            var options = CreateAgentOptions( root, CreateFakeCodexBinary( root ) );
            FakeCodexLauncher launcher = new( )
            {
                HoldAccountReadReply = true
            };
            await using CodexAgentBackend backend = new( options, new CodexCredentialStore( options.StateDirectory ), _ => launcher, clientVersion: "spec" );
            var inventory = await backend.DiscoverModelsAsync( false, default ).AsTask( )
                .WaitAsync( TimeSpan.FromSeconds( AgentLimits.StartupDeadlineSeconds + 5 ) );
            Equal( AiErrorCategories.Timeout, inventory.Error?.Category );
            True( !backend.IsRunning );
            True( launcher.Current!.StopRequested );
            True( !Directory.Exists( launcher.Requests[0].RuntimeHome ) );

            launcher.HoldAccountReadReply = false;
            True( (await backend.DiscoverModelsAsync( false, default )).Error is null );
            True( backend.IsRunning );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task CodexLogoutRetiresCredentialsAcrossFailuresAsync( )
    {
        foreach ( var outcome in new[] { "rejected", "cancelled", "success" } )
        {
            await AssertCodexLogoutRetiresCredentialsAsync( outcome );
        }
    }

    private static async Task AssertCodexLogoutRetiresCredentialsAsync( string outcome )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-codex-logout-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            var options = CreateAgentOptions( root, CreateFakeCodexBinary( root ) );
            CodexCredentialStore credentials = new( options.StateDirectory );
            var syntheticCredential = Encoding.UTF8.GetBytes( """{"tokens":{"access_token":"synthetic-access","refresh_token":"synthetic-refresh","account_id":"synthetic-account"}}""" );
            try
            {
                credentials.Write( syntheticCredential );
            }
            finally
            {
                CodexCredentialStore.Clear( syntheticCredential );
            }

            FakeCodexLauncher launcher = new( );
            await using CodexAgentBackend backend = new(
                options, credentials, _ => launcher,
                credentialResolver: new FixedCredentialResolver( "synthetic-api-key" ), clientVersion: "spec" );
            var pending = await backend.StartLoginAsync( AgentLoginMethods.DeviceCode, null, default );
            Equal( AgentAuthenticationStates.Pending, pending.State );
            var server = launcher.Current!;
            var retiredHome = launcher.Requests[0].RuntimeHome;
            True( File.Exists( Path.Combine( retiredHome, "auth.json" ) ) );
            server.CompleteLoginDuringLogout = true;
            server.RejectLogout = outcome == "rejected";
            server.HoldLogoutReply = outcome == "cancelled";

            using CancellationTokenSource cancellation = new( );
            var logout = backend.LogoutAsync( cancellation.Token ).AsTask( );
            await server.LogoutReceived.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Task<AgentModelInventory>? concurrentDiscovery = null;
            if ( outcome == "cancelled" )
            {
                concurrentDiscovery = backend.DiscoverModelsAsync( false, default ).AsTask( );
                cancellation.Cancel( );
                await ThrowsAsync<OperationCanceledException>( async ( ) => await logout.WaitAsync( TimeSpan.FromSeconds( 5 ) ) );
            }
            else
            {
                Equal( AgentAuthenticationStates.Unauthenticated, (await logout.WaitAsync( TimeSpan.FromSeconds( 5 ) )).State );
            }

            if ( concurrentDiscovery is not null )
            {
                True( (await concurrentDiscovery.WaitAsync( TimeSpan.FromSeconds( 5 ) )).Error is null );
                Equal( 2, launcher.Requests.Count );
                True( !File.Exists( Path.Combine( launcher.Requests[1].RuntimeHome, "auth.json" ) ) );
            }

            // Stop and disposal used to copy the retired runtime's credential back after logout.
            await backend.StopAsync( default );
            True( !credentials.Exists );
            True( !Directory.Exists( retiredHome ) );
            True( server.StopRequested );
            True( !backend.IsRunning );
            Equal( string.Empty, backend.RuntimeGeneration );
            Equal( AgentAuthenticationStates.Unauthenticated, (await backend.GetLoginStateAsync( default )).State );

            var inventory = await backend.DiscoverModelsAsync( false, default );
            True( inventory.Error is null );
            True( !File.Exists( Path.Combine( launcher.Requests[^1].RuntimeHome, "auth.json" ) ) );
            Equal( AgentAuthenticationStates.Unauthenticated, (await backend.GetLoginStateAsync( default )).State );

            // A fresh login remains usable after the old process and its pending login were retired.
            if ( outcome == "success" )
            {
                var signedIn = await backend.StartLoginAsync(
                    AgentLoginMethods.ApiKey,
                    new AiCredentialOptions { Kind = AiCredentialKinds.Environment, Name = "SYNTHETIC_CODEX_KEY" }, default );
                Equal( AgentAuthenticationStates.Authenticated, signedIn.State );
                Equal( AgentLoginMethods.ApiKey, signedIn.Method );
                True( credentials.Exists );
            }
            else
            {
                await backend.StartLoginAsync( AgentLoginMethods.DeviceCode, null, default );
                launcher.Current!.CompleteLogin( success: true );
                True( await WaitUntilAsync( ( ) => Task.FromResult( credentials.Exists ), TimeSpan.FromSeconds( 5 ) ) );
            }

            Equal( AgentAuthenticationStates.Authenticated, (await backend.GetLoginStateAsync( default )).State );
            await backend.StopAsync( default );
            True( credentials.Exists );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
