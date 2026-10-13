using System.Net.Sockets;

namespace CommandDeck;

public static partial class Program
{
    private static bool AgentActionUsesDaemon( CliArguments arguments )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count == 0 ? "status" : positionals[0].ToLowerInvariant( );
        return action is "ask" or "conversation" or "cancel" or "clear" or "packages";
    }

    private static async Task<int> RunAgentDaemonAsync(
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count == 0 ? "status" : positionals[0].ToLowerInvariant( );
        var request = BuildDaemonAgentRequest( action, arguments, positionals );
        if ( request is null )
        {
            return WriteAgentUsage( 2 );
        }

        try
        {
            await using var client = await UnixProtocolClient.ConnectAsync(
                arguments.GetOption( "--socket" ), cancellationToken ).ConfigureAwait( false );
            await client.SendAsync( request, cancellationToken ).ConfigureAwait( false );
            return await ReadDaemonAgentResponseAsync(
                client, request.RequestId, action, arguments, cancellationToken ).ConfigureAwait( false );
        }
        catch ( SocketException )
        {
            Console.Error.WriteLine( "The CommandDeck daemon is not running or its socket is unavailable." );
            return 1;
        }
    }

    private static ProtocolRequest? BuildDaemonAgentRequest(
        string action,
        CliArguments arguments,
        IReadOnlyList<string> positionals )
    {
        const long requestId = 1;
        return action switch
        {
            "ask" when positionals.Count >= 2 || arguments.GetOption( "--prompt" ) is not null => new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.AgentTurnStart,
                Query = arguments.GetOption( "--prompt" ) ?? string.Join( ' ', positionals.Skip( 1 ) ),
                BackendId = arguments.GetOption( "--backend" ),
                ModelId = arguments.GetOption( "--model" ),
                ReasoningEffort = arguments.GetOption( "--effort" )
            },
            "conversation" => new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.AgentConversationGet
            },
            "cancel" => new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.AgentTurnCancel
            },
            "clear" => new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.AgentClear
            },
            "packages" when positionals.Count >= 3 && positionals[1] == "search" => new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.PackageSearch,
                Query = string.Join( ' ', positionals.Skip( 2 ) ),
                Limit = 16
            },
            "packages" when positionals.Count >= 4 && positionals[1] == "preview" => new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.PackagePreview,
                PackageOperation = positionals[2].ToLowerInvariant( ),
                PackageNames = positionals.Skip( 3 ).ToList( )
            },
            "packages" when positionals.Count >= 3 && positionals[1] == "commit" && arguments.HasFlag( CliOptionNames.Yes ) => new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.PackageCommit,
                PreviewId = positionals[2]
            },
            _ => null
        };
    }

    private static async Task<int> ReadDaemonAgentResponseAsync(
        UnixProtocolClient client,
        long requestId,
        string action,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var exitCode = 0;
        await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            var message = ProtocolJson.DeserializeMessage( line );
            if ( message is null || message.RequestId != requestId )
            {
                continue;
            }
            if ( message.Type == ProtocolMessageTypes.Error )
            {
                Console.Error.WriteLine( Safe( message.Error ?? "The daemon rejected the request." ) );
                exitCode = 1;
                continue;
            }
            if ( message.AgentEvent is { } streamEvent )
            {
                if ( arguments.HasFlag( CliOptionNames.Json ) )
                {
                    Console.WriteLine( System.Text.Json.JsonSerializer.Serialize(
                        streamEvent, CommandDeckJsonContext.Default.AgentStreamEvent ) );
                }
                else
                {
                    WriteAgentStreamEvent( streamEvent, arguments.HasFlag( "--reasoning" ) );
                }
                if ( streamEvent.Done is { } done )
                {
                    exitCode = done.Outcome == AgentTurnOutcomes.Completed ? 0 : 1;
                }
                if ( streamEvent.Request is { } pending )
                {
                    await client.SendAsync( new ProtocolRequest
                    {
                        RequestId = 2,
                        Operation = ProtocolOperations.AgentContextResolve,
                        PendingRequestId = pending.Id,
                        Decision = AgentRequestDecisions.Deny
                    }, cancellationToken ).ConfigureAwait( false );
                }
            }
            else if ( message.AgentConversation is { } conversation )
            {
                exitCode = WriteAgentConversationSnapshot( conversation, arguments.HasFlag( CliOptionNames.Json ) );
            }
            else if ( message.PackageSearch is { } search )
            {
                exitCode = WriteDaemonPackageSearch( search, arguments.HasFlag( CliOptionNames.Json ) );
            }
            else if ( message.PackagePreview is { } preview )
            {
                exitCode = WriteDaemonPackagePreview( preview, arguments.HasFlag( CliOptionNames.Json ) );
            }
            else if ( message.PackageCommit is { } commit )
            {
                exitCode = WriteDaemonPackageCommit( commit, arguments.HasFlag( CliOptionNames.Json ) );
            }
            if ( message.Type == ProtocolMessageTypes.Done )
            {
                if ( action == "cancel" )
                {
                    Console.WriteLine( "Assistant turn cancelled." );
                }
                if ( action == "clear" )
                {
                    Console.WriteLine( "Assistant conversation cleared." );
                }
                return exitCode;
            }
        }
        return exitCode == 0 ? 1 : exitCode;
    }

    private static int WriteDaemonPackageSearch( PackageSearchResult search, bool json )
    {
        if ( json )
        {
            Console.WriteLine( System.Text.Json.JsonSerializer.Serialize(
                search, CommandDeckJsonContext.Default.PackageSearchResult ) );
            return search.Error is null ? 0 : 1;
        }
        if ( search.Error is not null )
        {
            Console.Error.WriteLine( Safe( search.Error ) );
            return 1;
        }
        foreach ( var package in search.Packages )
        {
            Console.WriteLine( $"{Safe( package.Repository ?? "?" )}/{Safe( package.Name )} {Safe( package.Version ?? string.Empty )}" );
        }
        return 0;
    }

    private static int WriteDaemonPackagePreview( PackageTransactionPreview preview, bool json )
    {
        if ( json )
        {
            Console.WriteLine( System.Text.Json.JsonSerializer.Serialize(
                preview, CommandDeckJsonContext.Default.PackageTransactionPreview ) );
        }
        else
        {
            Console.WriteLine( $"Preview {Safe( preview.PreviewId )} ({Safe( preview.Operation )})" );
            foreach ( var entry in preview.Entries )
            {
                Console.WriteLine( $"  {Safe( entry.Name )} {Safe( entry.TargetVersion ?? entry.CurrentVersion ?? string.Empty )}" );
            }
            Console.WriteLine( preview.Commitable
                ? "  commit: available with 'commanddeck agent packages commit PREVIEW_ID --yes'"
                : $"  commit: unavailable ({Safe( preview.Reason ?? "see warnings" )})" );
        }
        return preview.Commitable ? 0 : 1;
    }

    private static int WriteDaemonPackageCommit( PackageCommitResult commit, bool json )
    {
        if ( json )
        {
            Console.WriteLine( System.Text.Json.JsonSerializer.Serialize(
                commit, CommandDeckJsonContext.Default.PackageCommitResult ) );
        }
        else
        {
            Console.WriteLine( $"{Safe( commit.State )}: {Safe( commit.Message ?? string.Empty )}" );
        }
        return commit.State == PackageOperationStates.Completed ? 0 : 1;
    }
}
