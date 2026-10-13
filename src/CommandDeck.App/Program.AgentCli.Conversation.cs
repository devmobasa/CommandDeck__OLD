using System.Text.Json;
using System.Threading.Channels;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunAgentAskAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        IReadOnlyList<string> positionals,
        CancellationToken cancellationToken )
    {
        var text = arguments.GetOption( CliOptionNames.Prompt ) ??
                   string.Join( ' ', positionals.Skip( 1 ) );
        if ( string.IsNullOrWhiteSpace( text ) )
        {
            Console.Error.WriteLine( "agent ask requires prompt text." );
            return 2;
        }

        var reader = runtime.SubscribeAgentEvents(
            completeWhenIdle: false,
            out var subscriptionId );
        try
        {
            await runtime.StartAgentTurnAsync(
                text,
                arguments.GetOption( CliOptionNames.Backend ),
                arguments.GetOption( CliOptionNames.Model ),
                arguments.GetOption( CliOptionNames.Effort ),
                cancellationToken ).ConfigureAwait( false );
            return await StreamAgentTurnAsync( runtime, reader, arguments, cancellationToken )
                .ConfigureAwait( false );
        }
        finally
        {
            runtime.UnsubscribeAgentEvents( subscriptionId );
        }
    }

    private static async Task<int> StreamAgentTurnAsync(
        CommandDeckRuntime runtime,
        ChannelReader<AgentStreamEvent> reader,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var json = arguments.HasFlag( CliOptionNames.Json );
        var reasoning = arguments.HasFlag( CliOptionNames.Reasoning );
        AgentTurnResult? done = null;
        await foreach ( var value in
            reader.ReadAllAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            if ( json )
            {
                Console.WriteLine( JsonSerializer.Serialize(
                    value, CommandDeckJsonContext.Default.AgentStreamEvent ) );
            }
            else
            {
                WriteAgentStreamEvent( value, reasoning );
            }

            if ( value.Request is { } request )
            {
                await DenyAgentRequestAsync( runtime, request, cancellationToken )
                    .ConfigureAwait( false );
            }

            if ( value.Kind == AgentEventKinds.Done )
            {
                done = value.Done;
                break;
            }
        }

        if ( !json )
        {
            Console.WriteLine( );
            Console.Error.WriteLine(
                $"[done] {Safe( done?.Outcome ?? "unknown" )} " +
                $"{Safe( done?.Error?.Summary ?? string.Empty )}" );
        }

        return done?.Outcome == AgentTurnOutcomes.Completed ? 0 : 1;
    }

    private static void WriteAgentStreamEvent( AgentStreamEvent value, bool reasoning )
    {
        switch ( value.Kind )
        {
            case AgentEventKinds.Text:
                Console.Write( Safe( value.Text ?? string.Empty ) );
                break;
            case AgentEventKinds.Reasoning when reasoning:
                Console.Error.Write( Safe( value.Text ?? string.Empty ) );
                break;
            case AgentEventKinds.Tool when value.Tool is { } tool:
                Console.Error.WriteLine( $"[tool] {Safe( tool.Label )} ({Safe( tool.State )})" );
                break;
            case AgentEventKinds.Request when value.Request is { } request:
                Console.Error.WriteLine( $"[needs decision] {Safe( request.Prompt )}" );
                break;
            case AgentEventKinds.Done when value.Done is { } done:
                Console.WriteLine( );
                if ( done.Outcome != AgentTurnOutcomes.Completed )
                {
                    Console.Error.WriteLine( $"[{Safe( done.Outcome )}] {Safe( done.Error?.Summary ?? string.Empty )}" );
                }
                break;
            default:
                break;
        }
    }

    private static async ValueTask DenyAgentRequestAsync(
        CommandDeckRuntime runtime,
        AgentPendingRequest request,
        CancellationToken cancellationToken )
    {
        try
        {
            await runtime.ResolveAgentPendingAsync(
                request.Id,
                AgentRequestDecisions.Deny,
                null,
                cancellationToken ).ConfigureAwait( false );
        }
        catch ( InvalidOperationException exception )
        {
            Console.Error.WriteLine( $"[needs decision] {Safe( exception.Message )}" );
        }
    }

    private static int WriteAgentConversationSnapshot(
        AgentConversationSnapshot snapshot,
        bool json )
    {
        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                snapshot, CommandDeckJsonContext.Default.AgentConversationSnapshot ) );
            return 0;
        }

        Console.WriteLine( $"Conversation: {FormatAgentConversation( snapshot.Summary )}" );
        foreach ( var entry in snapshot.Entries )
        {
            Console.WriteLine( $"[{Safe( entry.Role )}] {Safe( entry.Text )}" );
        }
        if ( !string.IsNullOrEmpty( snapshot.ActiveText ) )
        {
            Console.WriteLine( $"[active] {Safe( snapshot.ActiveText )}" );
        }
        foreach ( var tool in snapshot.Tools )
        {
            Console.WriteLine( $"[tool] {Safe( tool.Label )} ({Safe( tool.State )})" );
        }
        Console.WriteLine( snapshot.Pending is { } pending
            ? $"[needs decision] {Safe( pending.Kind )}: {Safe( pending.Prompt )}"
            : "Pending: none" );
        return 0;
    }

    private static async Task<int> RunAgentCancelAsync(
        CommandDeckRuntime runtime,
        CancellationToken cancellationToken )
    {
        await runtime.CancelAgentTurnAsync( cancellationToken ).ConfigureAwait( false );
        Console.WriteLine( "Assistant turn cancelled." );
        return 0;
    }

    private static async Task<int> RunAgentClearAsync(
        CommandDeckRuntime runtime,
        CancellationToken cancellationToken )
    {
        await runtime.ClearAgentAsync( cancellationToken ).ConfigureAwait( false );
        Console.WriteLine( "Assistant conversation cleared." );
        return 0;
    }
}
