using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class InMemoryTransport( ChannelReader<string> inbound, ChannelWriter<string> outbound ) : IJsonRpcTransport
    {
        public async ValueTask<string?> ReceiveAsync( CancellationToken cancellationToken )
        {
            try
            {
                return await inbound.ReadAsync( cancellationToken );
            }
            catch ( ChannelClosedException )
            {
                return null;
            }
        }

        public ValueTask SendAsync( string message, CancellationToken cancellationToken )
        {
            outbound.TryWrite( message );
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync( )
        {
            outbound.TryComplete( );
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAppServer : IAgentProcessHandle
    {
        private readonly Channel<string> _toServer = Channel.CreateUnbounded<string>( );
        private readonly Channel<string> _toClient = Channel.CreateUnbounded<string>( );
        private readonly TaskCompletionSource _exited = new( TaskCreationOptions.RunContinuationsAsynchronously );
        private readonly Dictionary<long, TaskCompletionSource<JsonObject>> _pendingServerRequests = [];
        private readonly Task _loop;
        private long _serverRequestId = 1000;

        public FakeAppServer( )
        {
            Transport = new InMemoryTransport( _toClient.Reader, _toServer.Writer );
            _loop = Task.Run( RunAsync );
        }

        public IJsonRpcTransport Transport { get; }

        public Task Exited => _exited.Task;

        public List<JsonObject> Received { get; } = [];

        public bool Authenticated { get; set; }

        public bool RejectLogout { get; set; }

        public bool HoldLogoutReply { get; set; }

        public bool CompleteLoginDuringLogout { get; set; }

        public TaskCompletionSource LogoutReceived { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public bool HoldAccountReadReply { get; set; }

        public TaskCompletionSource AccountReadReceived { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public string? RuntimeHome { get; set; }

        public JsonObject? LastThreadStart { get; private set; }

        public JsonObject? LastTurnStart { get; private set; }

        public JsonObject? LastToolReply { get; private set; }

        public JsonObject? LastUserInputReply { get; private set; }

        public Func<FakeAppServer, string, string, Task>? TurnScript { get; set; }

        public bool StopRequested { get; private set; }

        public void Notify( string method, JsonObject parameters ) =>
            _toClient.Writer.TryWrite( new JsonObject { ["method"] = method, ["params"] = parameters }.ToJsonString( ) );

        public async Task<JsonObject> RequestAsync( string method, JsonObject parameters )
        {
            var id = Interlocked.Increment( ref _serverRequestId );
            TaskCompletionSource<JsonObject> completion = new( TaskCreationOptions.RunContinuationsAsynchronously );
            lock ( _pendingServerRequests )
            {
                _pendingServerRequests[id] = completion;
            }
            _toClient.Writer.TryWrite( new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString( ) );
            return await completion.Task.WaitAsync( TimeSpan.FromSeconds( 20 ) );
        }

        public ValueTask StopAsync( CancellationToken cancellationToken )
        {
            StopRequested = true;
            _toClient.Writer.TryComplete( );
            _exited.TrySetResult( );
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync( )
        {
            await StopAsync( CancellationToken.None );
            _toServer.Writer.TryComplete( );
            try
            {
                await _loop.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            }
            catch ( TimeoutException )
            {
            }
        }

        private async Task RunAsync( )
        {
            await foreach ( var line in _toServer.Reader.ReadAllAsync( ) )
            {
                var frame = ( JsonObject ) JsonNode.Parse( line )!;
                Received.Add( frame );
                if ( frame["method"] is null )
                {
                    var id = frame["id"]!.GetValue<long>( );
                    TaskCompletionSource<JsonObject>? completion;
                    lock ( _pendingServerRequests )
                    {
                        _pendingServerRequests.Remove( id, out completion );
                    }
                    completion?.TrySetResult( frame );
                    continue;
                }
                var method = frame["method"]!.GetValue<string>( );
                if ( frame["id"] is null )
                {
                    continue;
                }
                await HandleRequestAsync( frame["id"]!.GetValue<long>( ), method, frame["params"] as JsonObject );
            }
        }

        private async Task HandleRequestAsync( long id, string method, JsonObject? parameters )
        {
            if ( !HandleAccountRequest( id, method, parameters ) && !HandleThreadRequest( id, method, parameters ) )
            {
                ReplyError( id, -32601, "unknown method" );
            }
            await Task.CompletedTask;
        }

        private bool HandleAccountRequest( long id, string method, JsonObject? parameters )
        {
            switch ( method )
            {
                case "initialize":
                    Reply( id, new JsonObject { ["codexHome"] = RuntimeHome ?? "/tmp/fake", ["platformOs"] = "linux" } );
                    return true;
                case "model/list":
                    Reply( id, ModelList( parameters?["includeHidden"]?.GetValue<bool>( ) ?? false ) );
                    return true;
                case "account/read":
                    if ( HoldAccountReadReply )
                    {
                        AccountReadReceived.TrySetResult( );
                        return true;
                    }

                    Reply( id, new JsonObject
                    {
                        ["account"] = Authenticated ? new JsonObject { ["type"] = "chatgpt" } : null,
                        ["requiresOpenaiAuth"] = !Authenticated
                    } );
                    return true;
                case "account/login/start":
                    HandleLoginStart( id, parameters );
                    return true;
                case "account/login/cancel":
                    Reply( id, new JsonObject { ["status"] = "canceled" } );
                    return true;
                case "account/logout":
                    if ( CompleteLoginDuringLogout )
                    {
                        CompleteLogin( success: true );
                    }

                    LogoutReceived.TrySetResult( );
                    if ( HoldLogoutReply )
                    {
                        return true;
                    }

                    if ( RejectLogout )
                    {
                        ReplyError( id, -32603, "logout failed" );
                        return true;
                    }

                    Authenticated = false;
                    if ( RuntimeHome is not null )
                    {
                        File.Delete( Path.Combine( RuntimeHome, "auth.json" ) );
                    }

                    Reply( id, new JsonObject( ) );
                    Notify( "account/updated", new JsonObject { ["authMode"] = null } );
                    return true;
                case "spec/hang":
                    return true;
                default:
                    return false;
            }
        }

        private void HandleLoginStart( long id, JsonObject? parameters )
        {
            if ( parameters?["type"]?.GetValue<string>( ) == "apiKey" )
            {
                Authenticated = true;
                WriteRuntimeCredential( );
                Reply( id, new JsonObject { ["type"] = "apiKey" } );
                Notify( "account/updated", new JsonObject { ["authMode"] = "apikey" } );
                return;
            }
            Reply( id, new JsonObject
            {
                ["type"] = "chatgptDeviceCode",
                ["loginId"] = "login-1",
                ["userCode"] = "ABCD-EFGH",
                ["verificationUrl"] = "https://auth.example.invalid/device"
            } );
        }

        private bool HandleThreadRequest( long id, string method, JsonObject? parameters )
        {
            switch ( method )
            {
                case "thread/start":
                    LastThreadStart = parameters;
                    Reply( id, new JsonObject
                    {
                        ["thread"] = new JsonObject { ["id"] = "thread-1" },
                        ["model"] = parameters?["model"]?.GetValue<string>( ) ?? "fake-model",
                        ["approvalPolicy"] = "never",
                        ["approvalsReviewer"] = "user",
                        ["cwd"] = "/tmp",
                        ["modelProvider"] = "fake",
                        ["sandbox"] = new JsonObject { ["type"] = "readOnly" }
                    } );
                    return true;
                case "turn/start":
                    LastTurnStart = parameters;
                    var turnId = "turn-" + Received.Count;
                    Reply( id, new JsonObject { ["turn"] = new JsonObject { ["id"] = turnId, ["status"] = "inProgress", ["items"] = new JsonArray( ) } } );
                    var threadId = parameters?["threadId"]?.GetValue<string>( ) ?? "thread-1";
                    if ( TurnScript is { } script )
                    {
                        _ = Task.Run( ( ) => script( this, threadId, turnId ) );
                    }
                    return true;
                case "turn/interrupt":
                    Reply( id, new JsonObject( ) );
                    Notify( "turn/completed", new JsonObject
                    {
                        ["threadId"] = parameters?["threadId"]?.GetValue<string>( ),
                        ["turn"] = new JsonObject { ["id"] = parameters?["turnId"]?.GetValue<string>( ), ["status"] = "interrupted", ["items"] = new JsonArray( ) }
                    } );
                    return true;
                default:
                    return false;
            }
        }

        public void CompleteLogin( bool success )
        {
            if ( success )
            {
                Authenticated = true;
                WriteRuntimeCredential( );
            }
            Notify( "account/login/completed", new JsonObject { ["success"] = success, ["loginId"] = "login-1", ["error"] = success ? null : "denied" } );
            if ( success )
            {
                Notify( "account/updated", new JsonObject { ["authMode"] = "chatgpt" } );
            }
        }

        private void WriteRuntimeCredential( )
        {
            if ( RuntimeHome is null )
            {
                return;
            }
            File.WriteAllText( Path.Combine( RuntimeHome, "auth.json" ), """{"tokens":{"access_token":"fake","refresh_token":"fake-refresh","account_id":"acct"}}""" );
        }

        private static JsonObject ModelList( bool includeHidden )
        {
            JsonArray data =
            [
                new JsonObject
                {
                    ["id"] = "fake-sol",
                    ["displayName"] = "Fake Sol",
                    ["isDefault"] = true,
                    ["hidden"] = false,
                    ["defaultReasoningEffort"] = "low",
                    ["supportedReasoningEfforts"] = new JsonArray(
                        new JsonObject { ["reasoningEffort"] = "low", ["description"] = "fast" },
                        new JsonObject { ["reasoningEffort"] = "ultra", ["description"] = "slow" })
                }
            ];
            if ( includeHidden )
            {
                data.Add( new JsonObject { ["id"] = "fake-hidden", ["displayName"] = "Hidden", ["hidden"] = true, ["supportedReasoningEfforts"] = new JsonArray( ) } );
            }
            return new JsonObject { ["data"] = data, ["nextCursor"] = null };
        }

        private void Reply( long id, JsonObject result ) =>
            _toClient.Writer.TryWrite( new JsonObject { ["id"] = id, ["result"] = result }.ToJsonString( ) );

        private void ReplyError( long id, int code, string message ) =>
            _toClient.Writer.TryWrite( new JsonObject { ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString( ) );
    }

    private sealed class FakeCodexLauncher : IAgentProcessLauncher
    {
        public List<CodexLaunchRequest> Requests { get; } = [];

        public bool HoldAccountReadReply { get; set; }

        public FakeAppServer? Current { get; private set; }

        public Func<FakeAppServer, string, string, Task>? TurnScript { get; set; }

        public bool Authenticated { get; set; }

        // Holds the next launch after the backend admitted it, as a slow spawn would.
        public TaskCompletionSource<bool>? HoldNextLaunch { get; set; }

        public TaskCompletionSource LaunchEntered { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public List<FakeAppServer> Servers { get; } = [];

        public async ValueTask<IAgentProcessHandle> LaunchAsync( CodexLaunchRequest request, CancellationToken cancellationToken )
        {
            Requests.Add( request );
            var hold = HoldNextLaunch;
            if ( hold is not null )
            {
                HoldNextLaunch = null;
                LaunchEntered.TrySetResult( );
                await hold.Task.WaitAsync( cancellationToken );
            }
            Current = new FakeAppServer
            {
                RuntimeHome = request.RuntimeHome,
                HoldAccountReadReply = HoldAccountReadReply,
                TurnScript = TurnScript,
                Authenticated = Authenticated || File.Exists( Path.Combine( request.RuntimeHome, "auth.json" ) )
            };
            Servers.Add( Current );
            return Current;
        }
    }

    private static string CreateFakeCodexBinary( string root )
    {
        var path = Path.Combine( root, "codex" );
        File.WriteAllText( path, "#!/bin/sh\necho 'codex-cli 0.152.1'\n" );
        if ( OperatingSystem.IsLinux( ) )
        {
            File.SetUnixFileMode( path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        }
        return path;
    }

    private static AgentOptions CreateAgentOptions( string root, string binaryPath ) => new( )
    {
        CodexBinaryPath = binaryPath,
        RuntimeDirectory = Path.Combine( root, "runtime" ),
        StateDirectory = Path.Combine( root, "state" ),
        BubblewrapPath = "/usr/bin/bwrap",
        SystemdRunPath = "/usr/bin/systemd-run"
    };
}
