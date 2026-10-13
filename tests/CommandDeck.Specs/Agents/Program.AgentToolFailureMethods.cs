using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private const string ToolFailureDetail = "/sensitive/tool-detail";

    private static async Task AgentToolUnexpectedFailuresFinishTheActivityAsync( )
    {
        await AgentToolFailureFinishesTheActivityAsync( new SqliteException( "disk I/O error at " + ToolFailureDetail, 10 ) );
        await AgentToolFailureFinishesTheActivityAsync( new ArgumentException( "Bad path " + ToolFailureDetail ) );
    }

    private static async Task AgentToolFailureFinishesTheActivityAsync( Exception failure )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-agent-tool-failure-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            using AgentAuthority authority = new( catalog.AgentPreferences );
            var authoritySnapshot = (await authority.LoadAsync( default )).Snapshot;
            await authority.UpdateSettingsAsync( new AgentPreferences { Enabled = true }, authoritySnapshot.SessionEpoch, default );
            AgentToolResult? toolResult = null;
            Exception? escaped = null;
            ScriptedAgentBackend backend = new( );
            backend.Script = async ( _, host, token ) =>
            {
                try
                {
                    toolResult = await host.OnToolCallAsync( new AgentToolCall
                    {
                        CallId = "search-1",
                        Name = AgentToolNames.Search,
                        ArgumentsJson = "{\"query\":\"notes\"}"
                    }, token );
                }
                catch ( Exception exception ) when ( exception is not OperationCanceledException )
                {
                    // The JSON-RPC boundary answers the App Server generically, so its turn goes on.
                    escaped = exception;
                }

                await host.OnTextAsync( "continued after the tool", token );
            };
            await using AgentConversationCoordinator coordinator = new(
                authority,
                backend,
                new AgentContextBroker( authority, root ),
                new AgentCommandTool( root ),
                new FailingAgentLauncherHost( failure ) );

            await coordinator.StartTurnAsync( "search my notes", null, null, null, default );
            True( await WaitUntilAsync(
                ( ) => Task.FromResult( coordinator.Snapshot( ).Summary?.State == AgentConversationStates.Completed ),
                TimeSpan.FromSeconds( 5 ) ), "the turn did not complete after the tool failed" );

            var snapshot = coordinator.Snapshot( );
            Equal( AgentToolActivityStates.Failed, snapshot.Tools.Single( ).State );
            True( escaped is null, $"{failure.GetType( ).Name} escaped the tool router" );
            True( toolResult is { Success: false }, "the failed tool was not reported as denied" );
            True( !toolResult!.ContentText.Contains( ToolFailureDetail, StringComparison.Ordinal ),
                "the tool result carried the exception text" );
            True( snapshot.Entries.Any( static entry => entry.Text.Contains( "continued after the tool", StringComparison.Ordinal ) ),
                "the turn did not continue after the tool failed" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private sealed class FailingAgentLauncherHost( Exception failure ) : IAgentLauncherHost
    {
        public ValueTask<IReadOnlyList<SearchResult>> SearchAsync( SearchRequest request, CancellationToken cancellationToken ) =>
            ValueTask.FromException<IReadOnlyList<SearchResult>>( failure );

        public SearchResult? ResolveCachedResult( string resultId, string invocationReference ) => null;

        public ValueTask<ActionExecutionResult> InvokeAsync(
            string resultId,
            string actionId,
            string invocationReference,
            string query,
            bool confirmed,
            CancellationToken cancellationToken ) =>
            ValueTask.FromException<ActionExecutionResult>( failure );
    }
}
