namespace CommandDeck.Specs;

public static partial class Program
{
    // The daemon parses the typed text and owns the argv; a client names only
    // the shown row, its reference and an action.
    private static async Task RunCommandRowTravelsTheProtocolAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = CreateCliSpecRoot( );
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        using CancellationTokenSource lifetime = new( TimeSpan.FromSeconds( 45 ) );
        Task? serverTask = null;
        try
        {
            var bin = Directory.CreateDirectory( Path.Combine( root, "bin" ) ).FullName;
            var capture = Path.Combine( root, "calls" );
            var ran = Path.Combine( root, "ran" );
            await WriteExecutableAsync( Path.Combine( bin, "some-fixture-binary" ), $"#!/bin/sh\n: > '{ran}'\n" );
            await WriteExecutableAsync( Path.Combine( bin, "uwsm-app" ), ArgvRecorder( capture ) );
            Environment.SetEnvironmentVariable( "PATH", bin );
            var config = await WriteManagedSpecConfigAsync( root, new AiOptions { Enabled = false } );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, lifetime.Token );
            var socket = SpecTemporaryFiles.SocketPath( root, "run.sock" );
            serverTask = new UnixProtocolServer( runtime, socket ).RunAsync( lifetime.Token );
            await using var client = await ConnectWithRetryAsync( socket );

            async Task<List<ProtocolMessage>> ExchangeAsync( ProtocolRequest request )
            {
                await client.SendAsync( request, lifetime.Token );
                List<ProtocolMessage> messages = [];
                await foreach ( var line in client.ReadRawMessagesAsync( lifetime.Token ) )
                {
                    var message = DeserializeMessage( line );
                    if ( message?.RequestId == request.RequestId )
                    {
                        messages.Add( message );
                    }

                    if ( message?.RequestId == request.RequestId && message.Type == ProtocolMessageTypes.Done )
                    {
                        break;
                    }
                }

                return messages;
            }

            var searched = await ExchangeAsync( new ProtocolRequest
            {
                RequestId = 1,
                Operation = ProtocolOperations.Search,
                Query = "> some-fixture-binary --flag \"two words\"",
                Limit = 20
            } );
            var row = searched.First( static message => message.Type == ProtocolMessageTypes.Result ).Result!;
            Equal( "Run some-fixture-binary --flag 'two words'", row.Title );
            Equal( "run|run-in-terminal|copy-command", string.Join( '|', row.Actions.Select( static action => action.Id ) ) );
            Equal( ActionKinds.LaunchArgv, row.Actions[0].Kind );
            True( !File.Exists( capture ), "a search launches nothing" );

            // A forged reference or client-supplied argv runs nothing.
            foreach ( var forged in new[]
            {
                new ProtocolRequest { RequestId = 2, Operation = ProtocolOperations.Invoke, ResultId = row.Id, ActionId = "run",
                    InvocationReference = "forged" },
                new ProtocolRequest { RequestId = 3, Operation = ProtocolOperations.Invoke, ResultId = row.Id, ActionId = "run",
                    InvocationReference = row.InvocationReference,
                    InputValues = new( StringComparer.Ordinal ) { ["argv"] = "[\"sh\"]" } }
            } )
            {
                var refused = await ExchangeAsync( forged );
                True( !refused.Any( static message => message.ActionResult?.Succeeded == true ), $"request {forged.RequestId}" );
            }

            True( !File.Exists( capture ), "refused invocations launch nothing" );
            var invoked = await ExchangeAsync( new ProtocolRequest
            {
                RequestId = 4,
                Operation = ProtocolOperations.Invoke,
                ResultId = row.Id,
                ActionId = "run",
                InvocationReference = row.InvocationReference
            } );
            True( invoked.Single( static message => message.Type == ProtocolMessageTypes.ActionResult ).ActionResult!.Succeeded );
            Equal( "uwsm-app|-t|service|-S|both|--|some-fixture-binary|--flag|two words\n", await File.ReadAllTextAsync( capture ) );
            True( !File.Exists( ran ), "the fixture launcher never starts the program" );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            await lifetime.CancelAsync( );
            if ( serverTask is not null )
            {
                await serverTask;
            }

            Directory.Delete( root, recursive: true );
        }
    }
}
