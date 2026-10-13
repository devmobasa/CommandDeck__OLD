using System.Text.Json.Nodes;

namespace CommandDeck;

internal sealed class AdmittedAgentTools
{
    public required Func<int, bool> TryAdmit { get; init; }
    public required Func<JsonObject, CancellationToken, ValueTask<AgentToolResult>> Search { get; init; }
    public required Func<JsonObject, CancellationToken, ValueTask<AgentToolResult>> Invoke { get; init; }
    public required Func<string, JsonObject, CancellationToken, ValueTask<AgentToolResult>> Context { get; init; }
    public required Func<JsonObject, CancellationToken, ValueTask<AgentToolResult>> Command { get; init; }
    public required Func<JsonObject, CancellationToken, ValueTask<AgentToolResult>> PackageSearch { get; init; }
    public required Func<JsonObject, CancellationToken, ValueTask<AgentToolResult>> PackageChange { get; init; }
}

// Routing owns the tool vocabulary. The conversation owner supplies live admission
// and effects; pending decisions and turn/subscriber state never leave that owner.
internal static class AgentToolRouter
{
    public static ValueTask<AgentToolResult> DispatchAsync( string name, JsonObject arguments, AdmittedAgentTools tools, CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        return name switch
        {
            AgentToolNames.Search when tools.TryAdmit( AgentTrustLevels.Launcher ) => tools.Search( arguments, cancellationToken ),
            AgentToolNames.Invoke when tools.TryAdmit( AgentTrustLevels.Launcher ) => tools.Invoke( arguments, cancellationToken ),
            AgentToolNames.ListDirectory or AgentToolNames.ReadFile or AgentToolNames.SearchText
                when tools.TryAdmit( AgentTrustLevels.ApprovedContext ) => tools.Context( name, arguments, cancellationToken ),
            AgentToolNames.RunCommand when tools.TryAdmit( AgentTrustLevels.ConfirmedCommands ) => tools.Command( arguments, cancellationToken ),
            AgentToolNames.SearchPackages when tools.TryAdmit( AgentTrustLevels.Packages ) => tools.PackageSearch( arguments, cancellationToken ),
            AgentToolNames.ProposePackageChange when tools.TryAdmit( AgentTrustLevels.Packages ) => tools.PackageChange( arguments, cancellationToken ),
            _ => ValueTask.FromResult( AgentToolResult.Denied( "This tool is not available at the current trust level." ) )
        };
    }
}
