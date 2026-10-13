using System.Text.Json.Nodes;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ToolRoutingUsesLiveAdmissionAsync( )
    {
        var active = true;
        var level = AgentTrustLevels.Packages;
        var calls = 0;
        ValueTask<AgentToolResult> Effect( JsonObject arguments, CancellationToken token )
        {
            calls++;
            return ValueTask.FromResult( AgentToolResult.Ok( "fixture" ) );
        }

        AdmittedAgentTools tools = new( )
        {
            TryAdmit = minimum => active && level >= minimum,
            Search = Effect,
            Invoke = Effect,
            Context = ( _, arguments, token ) => Effect( arguments, token ),
            Command = Effect,
            PackageSearch = Effect,
            PackageChange = Effect
        };
        foreach ( var (name, minimum) in new[]
        {
            (AgentToolNames.Search, AgentTrustLevels.Launcher),
            (AgentToolNames.Invoke, AgentTrustLevels.Launcher),
            (AgentToolNames.ReadFile, AgentTrustLevels.ApprovedContext),
            (AgentToolNames.RunCommand, AgentTrustLevels.ConfirmedCommands),
            (AgentToolNames.SearchPackages, AgentTrustLevels.Packages),
            (AgentToolNames.ProposePackageChange, AgentTrustLevels.Packages)
        } )
        {
            level = minimum;
            True( (await AgentToolRouter.DispatchAsync( name, [], tools, default )).Success );
            var admitted = calls;
            level = minimum - 1;
            True( !(await AgentToolRouter.DispatchAsync( name, [], tools, default )).Success );
            level = AgentTrustLevels.Packages;
            active = false;
            True( !(await AgentToolRouter.DispatchAsync( name, [], tools, default )).Success );
            Equal( admitted, calls );
            active = true;
        }

        True( !(await AgentToolRouter.DispatchAsync( "invented", [], tools, default )).Success );
    }

    private static async Task EditorPayloadRejectsInvalidArgumentsAsync( )
    {
        var original = new EditorActionPayload( "/tmp/literal ; $(touch nope)", 12, 3, "key" );
        var decoded = EditorActionPayload.Read( original.ToArguments( ) );
        Equal( original.Path, decoded.Path );
        Equal( 12, decoded.Line );
        Equal( 3, decoded.Column );
        Equal( "key", decoded.Key );
        Throws<InvalidOperationException>( ( ) => _ = new EditorActionPayload( "", 1, 1 ) );
        Throws<InvalidOperationException>( ( ) => _ = new EditorActionPayload( "/tmp/test", 0, 1 ) );

        LinuxActionExecutor executor = new( );
        foreach ( var invalid in new[] { "-1", "0", "1;touch nope", "999999999999" } )
        {
            var arguments = original.ToArguments( );
            arguments["line"] = invalid;
            var result = await executor.ExecuteAsync( CacheResult( "editor" ), new CommandAction
            {
                Id = "edit",
                Title = "Edit",
                Kind = ActionKinds.EditAtLine,
                Arguments = arguments
            }, "", default );
            True( !result.Succeeded );
            True( result.Message!.Contains( "positive integer", StringComparison.Ordinal ) );
        }
    }
}
