namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunAgentAsync(
        CliArguments arguments,
        CommandDeckRuntime runtime,
        CancellationToken cancellationToken )
    {
        try
        {
            return await DispatchAgentAsync( arguments, runtime, cancellationToken )
                .ConfigureAwait( false );
        }
        catch ( AiOperationException exception )
        {
            Console.Error.WriteLine( Safe( exception.SafeSummary ) );
            return 1;
        }
        catch ( InvalidOperationException exception )
        {
            Console.Error.WriteLine( Safe( exception.Message ) );
            return 1;
        }
    }

    private static async Task<int> DispatchAgentAsync(
        CliArguments arguments,
        CommandDeckRuntime runtime,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count == 0 ? AgentCliActions.Status : positionals[0].ToLowerInvariant( );
        return action switch
        {
            AgentCliActions.Status => await WriteAgentStatusAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            AgentCliActions.Backends => await WriteAgentBackendsAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            AgentCliActions.Models => await WriteAgentModelsAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            AgentCliActions.Preferences => await RunAgentPreferencesAsync(
                runtime, arguments, positionals, cancellationToken ).ConfigureAwait( false ),
            AgentCliActions.Grants => await RunAgentGrantsAsync(
                runtime, arguments, positionals, cancellationToken ).ConfigureAwait( false ),
            AgentCliActions.Probe => await RunAgentProbeAsync( runtime, positionals, cancellationToken )
                .ConfigureAwait( false ),
            _ => await DispatchAgentSessionAsync(
                action, arguments, positionals, runtime, cancellationToken ).ConfigureAwait( false )
        };
    }

    private static async Task<int> DispatchAgentSessionAsync(
        string action,
        CliArguments arguments,
        IReadOnlyList<string> positionals,
        CommandDeckRuntime runtime,
        CancellationToken cancellationToken ) => action switch
        {
            AgentCliActions.Login => await RunAgentLoginAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            AgentCliActions.Logout => await RunAgentLogoutAsync( runtime, arguments, cancellationToken )
                .ConfigureAwait( false ),
            AgentCliActions.Ask => await RunAgentAskAsync( runtime, arguments, positionals, cancellationToken )
                .ConfigureAwait( false ),
            AgentCliActions.Conversation => WriteAgentConversationSnapshot(
                runtime.GetAgentConversation( ), arguments.HasFlag( CliOptionNames.Json ) ),
            AgentCliActions.Cancel => await RunAgentCancelAsync( runtime, cancellationToken )
                .ConfigureAwait( false ),
            AgentCliActions.Clear => await RunAgentClearAsync( runtime, cancellationToken )
                .ConfigureAwait( false ),
            AgentCliActions.Trust => await RunAgentTrustAsync( runtime, arguments, positionals, cancellationToken )
                .ConfigureAwait( false ),
            AgentCliActions.Grants => await RunAgentGrantsAsync( runtime, arguments, positionals, cancellationToken )
                .ConfigureAwait( false ),
            AgentCliActions.Packages => await RunAgentPackagesAsync(
                runtime, arguments, positionals, cancellationToken ).ConfigureAwait( false ),
            AgentCliActions.Help => WriteAgentUsage( 0 ),
            _ => WriteAgentUsage( 2 )
        };
}
