using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunAgentGrantsAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        IReadOnlyList<string> positionals,
        CancellationToken cancellationToken )
    {
        var action = positionals.Count > 1 ? positionals[1].ToLowerInvariant( ) : AgentCliActions.List;
        AgentGrantResult result;
        switch ( action )
        {
            case AgentCliActions.List:
                result = await runtime.GetAgentGrantsAsync( cancellationToken )
                    .ConfigureAwait( false );
                break;
            case AgentCliActions.AddFolder when positionals.Count >= 3:
                result = await runtime.AddAgentFolderGrantAsync(
                    AgentGrantKinds.Folder, positionals[2], cancellationToken )
                    .ConfigureAwait( false );
                break;
            case AgentCliActions.Remove when positionals.Count >= 3:
                result = await runtime.RemoveAgentGrantAsync( positionals[2], cancellationToken )
                    .ConfigureAwait( false );
                break;
            case AgentCliActions.Clear when arguments.HasFlag( CliOptionNames.Yes ):
                result = await runtime.ClearAgentGrantsAsync( cancellationToken )
                    .ConfigureAwait( false );
                break;
            case AgentCliActions.Clear:
                Console.Error.WriteLine( "agent grants clear requires --yes." );
                return 2;
            default:
                Console.Error.WriteLine(
                    "Use 'agent grants list', 'add-folder PATH', 'remove ID', or 'clear --yes'." );
                return 2;
        }

        return WriteAgentGrants( result, arguments.HasFlag( CliOptionNames.Json ) );
    }

    private static int WriteAgentGrants( AgentGrantResult result, bool json )
    {
        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                result, CommandDeckJsonContext.Default.AgentGrantResult ) );
            return 0;
        }

        if ( result.Grants.Count == 0 )
        {
            Console.WriteLine( "No grants." );
            return 0;
        }

        foreach ( var grant in result.Grants )
        {
            Console.WriteLine(
                $"{Safe( grant.Id )}  {Safe( grant.Kind )}  {Safe( grant.State )}  " +
                $"{Safe( grant.Scope )}" );
        }

        return 0;
    }
}
