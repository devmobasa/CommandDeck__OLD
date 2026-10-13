using System.Text;
namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task AgentClearFencesNonCooperativeOldGenerationsAsync( )
    {
        ScriptedAgentBackend backend = new( );
        TaskCompletionSource oldEntered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource releaseOld = new( TaskCreationOptions.RunContinuationsAsynchronously );
        backend.Script = async ( input, host, _ ) =>
        {
            if ( input.Text == "old" )
            {
                oldEntered.TrySetResult( );
                await releaseOld.Task;
                await host.OnTextAsync( "stale", default );
                return;
            }
            await host.OnTextAsync( "new", default );
        };
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) =
            await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        try
        {
            await using ( runtime )
            {
                await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
                var old = await runtime.StartAgentTurnAsync( "old", null, null, null, "owner", default );
                await oldEntered.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
                var clear = runtime.ClearAgentAsync( default ).AsTask( );
                True( runtime.GetAgentConversation( ).Generation > old.Generation );

                await runtime.StartAgentTurnAsync( "new", null, null, null, "owner", default );
                True( await WaitUntilAsync(
                    ( ) => Task.FromResult( runtime.GetAgentConversation( ).Summary?.State == AgentConversationStates.Completed ),
                    TimeSpan.FromSeconds( 2 ) ) );
                var fresh = runtime.GetAgentConversation( );
                Equal( "new", fresh.Entries[^1].Text );
                var savedPath = Path.Combine( root, "new-output.txt" );
                True( (await runtime.ExecuteAgentOutputAsync(
                    fresh.OutputGeneration, "save", savedPath, null, default )).Succeeded );
                Equal( "new", await File.ReadAllTextAsync( savedPath ) );

                releaseOld.TrySetResult( );
                await clear.WaitAsync( TimeSpan.FromSeconds( 2 ) );
                var afterOld = runtime.GetAgentConversation( );
                Equal( fresh.Generation, afterOld.Generation );
                Equal( "new", afterOld.Entries[^1].Text );
                var afterPath = Path.Combine( root, "after-old-output.txt" );
                True( (await runtime.ExecuteAgentOutputAsync(
                    afterOld.OutputGeneration, "save", afterPath, null, default )).Succeeded );
                Equal( "new", await File.ReadAllTextAsync( afterPath ) );
            }
        }
        finally
        {
            releaseOld.TrySetResult( );
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentStreamingBoundsCountUtf8BytesAndDropSlowSubscribersAsync( )
    {
        ScriptedAgentBackend backend = new( );
        backend.Script = async ( input, host, token ) =>
        {
            if ( input.Text == "oversized answer" )
            {
                await host.OnTextAsync(
                    string.Concat( Enumerable.Repeat( "😀", AgentLimits.MaximumAnswerBytes / 4 + 1 ) ), token );
                return;
            }
            if ( input.Text == "reasoning" )
            {
                await host.OnReasoningAsync(
                    string.Concat( Enumerable.Repeat( "😀", AgentLimits.MaximumReasoningBytes / 4 + 10 ) ), token );
                await host.OnReasoningAsync( "must-not-appear", token );
                await host.OnTextAsync( "bounded", token );
                return;
            }
            for ( var index = 0; index < 400; index++ )
            {
                await host.OnStatusAsync( "event-" + index, token );
            }
            await host.OnTextAsync( "finished", token );
        };
        using CancellationTokenSource serverCancellation = new( );
        (var runtime, var _, var root, var _) =
            await StartAgentRuntimeAsync( backend, serverCancellation.Token );
        try
        {
            await using ( runtime )
            {
                await runtime.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
                await runtime.StartAgentTurnAsync( "oversized answer", null, null, null, "owner", default );
                True( await WaitUntilAsync(
                    ( ) => Task.FromResult( runtime.GetAgentConversation( ).Summary?.State == AgentConversationStates.Failed ),
                    TimeSpan.FromSeconds( 2 ) ) );
                Equal( AiErrorCategories.ResponseTooLarge, runtime.GetAgentConversation( ).LastError?.Category );

                var reasoningReader =
                    runtime.SubscribeAgentEvents( completeWhenIdle: false, out var reasoningSubscription );
                await runtime.StartAgentTurnAsync( "reasoning", null, null, null, "owner", default );
                List<AgentStreamEvent> reasoningEvents = [];
                using ( CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 5 ) ) )
                {
                    await foreach ( var item in reasoningReader.ReadAllAsync( timeout.Token ) )
                    {
                        reasoningEvents.Add( item );
                    }
                }
                runtime.UnsubscribeAgentEvents( reasoningSubscription );
                var reasoning = runtime.GetAgentConversation( );
                var reasoningBytes = Encoding.UTF8.GetByteCount( reasoning.ActiveReasoning ?? string.Empty );
                True( reasoningBytes <= AgentLimits.MaximumReasoningBytes );
                True( !(reasoning.ActiveReasoning ?? string.Empty).Contains( "must-not-appear", StringComparison.Ordinal ) );
                True( reasoningEvents.Where( item => item.Kind is AgentEventKinds.Text or AgentEventKinds.Reasoning )
                    .All( item => Encoding.UTF8.GetByteCount( item.Text ?? string.Empty ) <= AgentLimits.MaximumEventTextBytes ) );

                _ = runtime.SubscribeAgentEvents( completeWhenIdle: false, out var slowSubscription );
                await runtime.StartAgentTurnAsync( "slow subscriber", null, null, null, "owner", default );
                True( await WaitUntilAsync(
                    ( ) => Task.FromResult( runtime.GetAgentConversation( ).Summary?.State == AgentConversationStates.Completed ),
                    TimeSpan.FromSeconds( 2 ) ) );
                Equal( "finished", runtime.GetAgentConversation( ).Entries[^1].Text );
                runtime.UnsubscribeAgentEvents( slowSubscription );
            }
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }
}
