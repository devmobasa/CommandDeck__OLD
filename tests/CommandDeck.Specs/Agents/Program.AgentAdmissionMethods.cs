namespace CommandDeck.Specs;

public static partial class Program
{
    private const string AuthorityChangedWhileStarting = "Assistant permissions changed while starting; send again.";
    private const string AuthorityChangedWhileSaving = "Assistant permissions changed while saving; review the settings and save again.";

    // A turn that read level 6 and was still preparing when a downgrade finished
    // must not start a session or a turn at the authority the downgrade revoked.
    private static async Task AgentTurnPreparedAcrossADowngradeNeverStartsAsync( )
    {
        ScriptedAgentBackend backend = new( );
        await WithAgentRuntimeAsync( backend, null, async runtime =>
        {
            await EnableAtUnrestrictedAsync( runtime );
            TaskCompletionSource<bool> releaseLogin = new( TaskCreationOptions.RunContinuationsAsynchronously );
            backend.BlockNextLogin = releaseLogin;
            var turn = runtime.StartAgentTurnAsync( "work across a downgrade", null, null, null, "owner", default ).AsTask( );
            await backend.LoginEntered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            try
            {
                await LowerTrustAsync( runtime, AgentTrustLevels.Default );
            }
            finally
            {
                releaseLogin.TrySetResult( true );
            }

            var rejected = await CatchAsync<AiOperationException>( turn );
            Equal( AiErrorCategories.Unavailable, rejected.Category );
            Equal( AuthorityChangedWhileStarting, rejected.SafeSummary );
            Equal( 0, backend.Sessions );
            Equal( 0L, runtime.GetAgentConversation( "owner" ).ActiveTurnId );
        } );
    }

    // A prewarm in the same window stays quiet and prepares nothing.
    private static async Task AgentPrewarmPreparedAcrossADowngradeStaysQuietAsync( )
    {
        ScriptedAgentBackend backend = new( );
        await WithAgentRuntimeAsync( backend, null, async runtime =>
        {
            await EnableAtUnrestrictedAsync( runtime );
            TaskCompletionSource<bool> releaseLogin = new( TaskCreationOptions.RunContinuationsAsynchronously );
            backend.BlockNextLogin = releaseLogin;
            var prewarm = runtime.PrewarmAgentAsync( default ).AsTask( );
            await backend.LoginEntered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            try
            {
                await LowerTrustAsync( runtime, AgentTrustLevels.Default );
            }
            finally
            {
                releaseLogin.TrySetResult( true );
            }

            await prewarm.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            Equal( 0, backend.Sessions );
            True( runtime.GetAgentConversation( "owner" ).Summary is null, "a stale prewarm installs no conversation" );
        } );
    }

    // A settings save that captured its snapshot before a reset or disable and
    // wrote after it is rejected; a fresh save afterwards is not.
    private static async Task AgentSettingsSaveRejectedAfterResetAsync( ) =>
        await AgentStaleSettingsSaveIsRejectedAsync( static runtime => runtime.ResetCustomizationsAsync( default ).AsTask( ) );

    private static async Task AgentSettingsSaveRejectedAfterDisableAsync( ) =>
        await AgentStaleSettingsSaveIsRejectedAsync( static runtime =>
            runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = false }, default ).AsTask( ) );

    private static async Task AgentStaleSettingsSaveIsRejectedAsync( Func<CommandDeckRuntime, Task> revoke )
    {
        ScriptedAgentBackend backend = new( );
        AdjustableTimeProvider clock = new( new DateTimeOffset( 2026, 10, 9, 12, 0, 0, TimeSpan.Zero ) );
        await WithAgentRuntimeAsync( backend, clock, async runtime =>
        {
            await EnableAtUnrestrictedAsync( runtime );
            clock.Advance( TimeSpan.FromMinutes( AgentLimits.TrustLeaseHourMinutes + 1 ) );

            // The save's own load ends the expired lease, captures its snapshot,
            // then revokes; the held stop keeps it between snapshot and write.
            var pendingSave = HoldSaveAfterItsSnapshotAsync( runtime, backend, out var releaseStop );
            await backend.StopEntered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            try
            {
                await revoke( runtime );
            }
            finally
            {
                releaseStop.TrySetResult( true );
            }

            var rejected = await CatchAsync<AiOperationException>( pendingSave );
            Equal( AiErrorCategories.Conflict, rejected.Category );
            Equal( AuthorityChangedWhileSaving, rejected.SafeSummary );
            var persisted = await runtime.GetAgentPreferencesAsync( default );
            True( !persisted.Enabled && !persisted.WebSearchEnabled, "the stale save wrote nothing" );
            Equal( AgentTrustLevels.Default, persisted.TrustLevel );
            True( persisted.TrustLeaseExpiresUnixMilliseconds is null, "no lease survives" );

            var fresh = await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
            True( fresh.Enabled, "a fresh enable after the rejection is allowed" );
        } );
    }

    // The rejection reaches a socket client with its category, a readable
    // message, and Done for the same request.
    private static async Task AgentStaleSettingsSaveReportsConflictOverTheSocketAsync( )
    {
        ScriptedAgentBackend backend = new( );
        AdjustableTimeProvider clock = new( new DateTimeOffset( 2026, 10, 9, 12, 0, 0, TimeSpan.Zero ) );
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) = await StartAgentRuntimeAsync(
            backend, serverCancellation.Token, new RuntimeConstructionOptions { AgentClock = clock } );
        try
        {
            await using ( runtime )
            {
                await EnableAtUnrestrictedAsync( runtime );
                clock.Advance( TimeSpan.FromMinutes( AgentLimits.TrustLeaseHourMinutes + 1 ) );
                TaskCompletionSource<bool> releaseStop = new( TaskCreationOptions.RunContinuationsAsynchronously );
                backend.BlockNextStop = releaseStop;
                await using var client = await ConnectWithRetryAsync( Path.Combine( root, "commanddeck.sock" ) );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 7,
                    Operation = ProtocolOperations.AgentPreferencesUpdate,
                    AgentPreferences = new AgentPreferences { Enabled = true, WebSearchEnabled = true }
                }, default );
                var messages = CollectUntilDoneAsync( client, 7 );
                await backend.StopEntered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
                try
                {
                    await runtime.ResetCustomizationsAsync( default );
                }
                finally
                {
                    releaseStop.TrySetResult( true );
                }

                var received = await messages;
                Equal( 2, received.Count );
                Equal( ProtocolMessageTypes.Error, received[0].Type );
                Equal( $"{AiErrorCategories.Conflict}: {AuthorityChangedWhileSaving}", received[0].Error );
                Equal( ProtocolMessageTypes.Done, received[1].Type );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    // A turn started after a save's write, while its revocation is still held,
    // was admitted under the saved settings and survives that revocation.
    private static async Task AgentTurnAdmittedDuringARevocationSurvivesItAsync( )
    {
        ScriptedAgentBackend backend = new( );
        TaskCompletionSource turnStarted = new( TaskCreationOptions.RunContinuationsAsynchronously );
        backend.Script = async ( _, _, token ) =>
        {
            turnStarted.TrySetResult( );
            await Task.Delay( Timeout.Infinite, token ).ConfigureAwait( false );
        };
        await WithAgentRuntimeAsync( backend, null, async runtime =>
        {
            await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
            TaskCompletionSource<bool> releaseStop = new( TaskCreationOptions.RunContinuationsAsynchronously );
            backend.BlockNextStop = releaseStop;
            var save = runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true, WebSearchEnabled = true }, default ).AsTask( );
            await backend.StopEntered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            try
            {
                await runtime.StartAgentTurnAsync( "work during a save", null, null, null, "owner", default );
                await turnStarted.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            }
            finally
            {
                releaseStop.TrySetResult( true );
            }

            var saved = await save.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            True( saved.WebSearchEnabled );
            True( runtime.GetAgentConversation( "owner" ).ActiveTurnId != 0, "the turn admitted after the write survives the revocation" );
            True( backend.LastOptions!.WebSearchEnabled, "that turn runs with the saved settings" );
            True( backend.IsRunning, "its process survives the revocation" );
        } );
    }

    // A turn admitted before a save that ends sessions is no longer running
    // once the save returns: write and revocation are both complete by then.
    private static async Task AgentNoTurnFromBeforeASavedChangeSurvivesItAsync( )
    {
        ScriptedAgentBackend backend = new( );
        TaskCompletionSource turnStarted = new( TaskCreationOptions.RunContinuationsAsynchronously );
        backend.Script = async ( _, _, token ) =>
        {
            turnStarted.TrySetResult( );
            await Task.Delay( Timeout.Infinite, token ).ConfigureAwait( false );
        };
        await WithAgentRuntimeAsync( backend, null, async runtime =>
        {
            await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
            await runtime.StartAgentTurnAsync( "work before a save", null, null, null, "owner", default );
            await turnStarted.Task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            True( !backend.LastOptions!.WebSearchEnabled );

            await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true, WebSearchEnabled = true }, default );
            Equal( 0L, runtime.GetAgentConversation( "owner" ).ActiveTurnId );
            True( !backend.IsRunning, "the process admitted before the save is stopped" );
        } );
    }

    private static Task<AgentPreferences> HoldSaveAfterItsSnapshotAsync(
        CommandDeckRuntime runtime,
        ScriptedAgentBackend backend,
        out TaskCompletionSource<bool> releaseStop )
    {
        releaseStop = new( TaskCreationOptions.RunContinuationsAsynchronously );
        backend.BlockNextStop = releaseStop;
        return runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true, WebSearchEnabled = true }, default ).AsTask( );
    }

    private static async Task EnableAtUnrestrictedAsync( CommandDeckRuntime runtime )
    {
        await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
        var raise = await runtime.CreateAgentTrustChallengeAsync( "owner", AgentTrustLevels.Unrestricted, default );
        await runtime.ApplyAgentTrustAsync(
            "owner", raise.Id, AgentTrustLevels.Unrestricted, AgentLimits.UnrestrictedPhrase, AgentTrustDurations.OneHour, default );
    }

    private static async Task LowerTrustAsync( CommandDeckRuntime runtime, int level )
    {
        var lower = await runtime.CreateAgentTrustChallengeAsync( "owner", level, default );
        var lowered = await runtime.ApplyAgentTrustAsync( "owner", lower.Id, level, null, null, default );
        Equal( level, lowered.Level );
    }

    private static async Task WithAgentRuntimeAsync(
        ScriptedAgentBackend backend,
        TimeProvider? clock,
        Func<CommandDeckRuntime, Task> body )
    {
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) = await StartAgentRuntimeAsync(
            backend, serverCancellation.Token, new RuntimeConstructionOptions { AgentClock = clock } );
        try
        {
            await using ( runtime )
            {
                await body( runtime );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task<TException> CatchAsync<TException>( Task task ) where TException : Exception
    {
        try
        {
            await task.WaitAsync( TimeSpan.FromSeconds( 10 ) );
        }
        catch ( TException expected )
        {
            return expected;
        }
        throw new InvalidOperationException( $"Expected {typeof( TException ).Name}, but the operation completed." );
    }
}
