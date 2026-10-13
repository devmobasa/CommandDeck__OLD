namespace CommandDeck.Specs;

public static partial class Program
{
    private static Task HerdrActionRaisesForegroundWindowAsync( ) =>
        HerdrActionHandlesDispatchResponseAsync( "ok\n", succeeded: true );

    private static async Task HerdrActionRejectsDispatchWarningsAsync( )
    {
        foreach ( var response in new[]
        {
            "warning: [string \"return hl.dispatch(...)\"]:1: hl.focus: window not found\n",
            "ok\nwarning: hl.focus: window not found\n",
            ""
        } )
        {
            await HerdrActionHandlesDispatchResponseAsync( response, succeeded: false );
        }
    }

    private static async Task HerdrActionHandlesDispatchResponseAsync( string response, bool succeeded )
    {
        var results = await SearchHerdrAsync( "herdr: " );
        var space = results.Single( result => result.Id == "herdr:workspace:w2" );
        List<string> events = [];
        HerdrFake fake = new( )
        {
            Respond = args =>
            {
                if ( args[2] == "workspace" )
                {
                    events.Add( "herdr-focus" );
                    return HerdrResult(
                        """{"id":"cli:workspace:focus","result":{"type":"workspace_info","workspace":{"workspace_id":"w2","focused":true}}}""" );
                }

                events.Add( "herdr-status" );
                return HerdrResult( HerdrStatusRunningJson );
            }
        };
        HerdrClient herdr = new( fake.Runner, static ( ) => true );
        HerdrWindowActivator activator = new(
            herdr,
            ( _, title, _ ) =>
            {
                events.Add( title is null ? "title-clear" : "title-set" );
                return ValueTask.FromResult( HerdrWindowTitleResult.Success( ) );
            },
            ( _, args, _, _ ) =>
            {
                if ( args.SequenceEqual( ["-j", "clients"] ) )
                {
                    events.Add( "hyprland-clients" );
                    return ValueTask.FromResult( HerdrResult(
                        """[{"address":"0x1a2b","title":"commanddeck-herdr-test"}]""" ) );
                }

                events.Add( "hyprland-focus" );
                Equal(
                    "dispatch hl.dsp.focus({ window = \"address:0x1a2b\" })",
                    string.Join( ' ', args ) );
                return ValueTask.FromResult( HerdrResult( response ) );
            },
            static ( ) => true,
            static ( ) => "commanddeck-herdr-test" );
        LinuxActionExecutor executor = new(
            herdr: herdr,
            herdrWindowActivator: activator );

        var switched = await executor.ExecuteAsync(
            space,
            space.Actions[0],
            "",
            CancellationToken.None );

        Equal( succeeded, switched.Succeeded );
        if ( !succeeded )
        {
            Equal( "Switched Herdr space, but could not focus its window.", switched.Message );
        }

        Equal(
            "herdr-focus|herdr-status|title-set|hyprland-clients|hyprland-focus|title-clear",
            string.Join( '|', events ) );
    }

    private static async Task HerdrWindowActivationUsesRawTitleContractAsync( )
    {
        await using HerdrTitleServer server = new(
            TitleResponse( "set" ),
            TitleResponse( "cleared" ) );
        HerdrFake fake = new( )
        {
            Respond = _ => HerdrResult(
                $$"""{"running":true,"version":"0.9.0","protocol":22,"compatible":true,"socket":"{{server.SocketPath}}"}""" )
        };
        HerdrClient herdr = new( fake.Runner, static ( ) => true );
        HerdrWindowActivator activator = new(
            herdr,
            HerdrWindowTitleClient.SendAsync,
            ( _, args, _, _ ) => ValueTask.FromResult(
                args.SequenceEqual( ["-j", "clients"] )
                    ? HerdrResult( """[{"address":"0xabcd","title":"commanddeck-herdr-raw"}]""" )
                    : HerdrResult( "ok" ) ),
            static ( ) => true,
            static ( ) => "commanddeck-herdr-raw" );

        var outcome = await activator.ActivateAsync(
            HerdrTargetKinds.Workspace,
            CancellationToken.None );
        await server.Completion;

        True( outcome.Succeeded, outcome.Message );
        Equal( 2, server.Requests.Count );
        True( server.Requests[0].Contains(
            "\"method\":\"client.window_title.set\",\"params\":{\"title\":\"commanddeck-herdr-raw\"}",
            StringComparison.Ordinal ) );
        True( server.Requests[1].Contains(
            "\"method\":\"client.window_title.clear\",\"params\":{}",
            StringComparison.Ordinal ) );
    }

    private static async Task HerdrWindowActivationRejectsMalformedTitleResponseAsync( )
    {
        await using HerdrTitleServer server = new(
            """{"id":7,"result":{"type":"client_window_title","changed":true,"reason":"set"}}""",
            TitleResponse( "cleared" ) );
        HerdrFake fake = new( )
        {
            Respond = _ => HerdrResult(
                $$"""{"running":true,"version":"0.9.0","protocol":22,"compatible":true,"socket":"{{server.SocketPath}}"}""" )
        };
        HerdrWindowActivator activator = new(
            new HerdrClient( fake.Runner, static ( ) => true ),
            HerdrWindowTitleClient.SendAsync,
            ( _, _, _, _ ) => throw new InvalidOperationException( "Hyprland must not run." ),
            static ( ) => true,
            static ( ) => "commanddeck-herdr-malformed" );

        var outcome = await activator.ActivateAsync(
            HerdrTargetKinds.Tab,
            CancellationToken.None );
        await server.Completion;

        True( !outcome.Succeeded );
        Equal( "Switched Herdr tab, but could not focus its window.", outcome.Message );
        Equal( 2, server.Requests.Count );
    }

    private static async Task HerdrWindowActivationRejectsOversizedTitleResponseAsync( )
    {
        await using HerdrTitleServer server = new(
            "{\"padding\":\"" + new string( 'x', 16_384 ) +
            "\",\"id\":\"commanddeck:window-title\",\"result\":{\"type\":\"client_window_title\",\"changed\":true,\"reason\":\"set\"}}",
            TitleResponse( "cleared" ) );
        HerdrFake fake = new( )
        {
            Respond = _ => HerdrResult(
                $$"""{"running":true,"version":"0.9.0","protocol":22,"compatible":true,"socket":"{{server.SocketPath}}"}""" )
        };
        var hyprlandCalled = false;
        HerdrWindowActivator activator = new(
            new HerdrClient( fake.Runner, static ( ) => true ),
            HerdrWindowTitleClient.SendAsync,
            ( _, _, _, _ ) =>
            {
                hyprlandCalled = true;
                return ValueTask.FromResult( HerdrResult( "[]" ) );
            },
            static ( ) => true,
            static ( ) => "commanddeck-herdr-oversized" );

        var outcome = await activator.ActivateAsync(
            HerdrTargetKinds.Workspace,
            CancellationToken.None );
        await server.Completion;

        True( !outcome.Succeeded );
        True( !hyprlandCalled );
        Equal( 2, server.Requests.Count );
    }

    private static async Task HerdrWindowActivationRestoresTitleAfterHyprlandFailureAsync( )
    {
        List<string> events = [];
        HerdrFake fake = new( );
        HerdrWindowActivator activator = new(
            new HerdrClient( fake.Runner, static ( ) => true ),
            ( _, title, _ ) =>
            {
                events.Add( title is null ? "title-clear" : "title-set" );
                return ValueTask.FromResult( HerdrWindowTitleResult.Success( ) );
            },
            ( _, args, _, _ ) =>
            {
                if ( args.SequenceEqual( ["-j", "clients"] ) )
                {
                    events.Add( "hyprland-clients" );
                    return ValueTask.FromResult( HerdrResult(
                        """[{"address":"0xcafe","title":"commanddeck-herdr-failure"}]""" ) );
                }

                events.Add( "hyprland-focus" );
                return ValueTask.FromResult( HerdrResult( "", exitCode: 1, error: "focus failed" ) );
            },
            static ( ) => true,
            static ( ) => "commanddeck-herdr-failure" );

        var outcome = await activator.ActivateAsync(
            HerdrTargetKinds.Workspace,
            CancellationToken.None );

        True( !outcome.Succeeded );
        Equal( "Switched Herdr space, but could not focus its window.", outcome.Message );
        Equal(
            "title-set|hyprland-clients|hyprland-focus|title-clear",
            string.Join( '|', events ) );
    }

    private static async Task HerdrWindowActivationIsInertOutsideHyprlandAsync( )
    {
        HerdrFake fake = new( );
        HerdrWindowActivator activator = new(
            new HerdrClient( fake.Runner, static ( ) => true ),
            ( _, _, _ ) => throw new InvalidOperationException( "Herdr title API must not run." ),
            ( _, _, _, _ ) => throw new InvalidOperationException( "Hyprland must not run." ),
            static ( ) => false,
            static ( ) => throw new InvalidOperationException( "A marker must not be created." ) );

        var outcome = await activator.ActivateAsync(
            HerdrTargetKinds.Tab,
            CancellationToken.None );

        True( outcome.Succeeded );
        Equal( 0, fake.Calls.Count );
    }

    private static Task HerdrWindowActivationClearsAfterMarkTimeoutAsync( ) =>
        HerdrWindowActivationClearsAfterInterruptedMarkAsync( cancel: false );

    private static Task HerdrWindowActivationClearsAfterMarkCancellationAsync( ) =>
        HerdrWindowActivationClearsAfterInterruptedMarkAsync( cancel: true );

    private static async Task HerdrWindowActivationClearsAfterInterruptedMarkAsync( bool cancel )
    {
        using CancellationTokenSource cancellation = new( );
        await using HerdrTitleServer server = new( null, TitleResponse( "cleared" ) )
        {
            ResponseWithheld = cancel ? cancellation.Cancel : null
        };
        HerdrFake fake = new( )
        {
            Respond = _ => HerdrResult(
                $$"""{"running":true,"version":"0.9.0","protocol":22,"compatible":true,"socket":"{{server.SocketPath}}"}""" )
        };
        HerdrWindowActivator activator = new(
            new HerdrClient( fake.Runner, static ( ) => true ),
            HerdrWindowTitleClient.SendAsync,
            ( _, _, _, _ ) => throw new InvalidOperationException( "Hyprland must not run." ),
            static ( ) => true,
            static ( ) => "commanddeck-herdr-interrupted" );

        if ( cancel )
        {
            await ThrowsAsync<OperationCanceledException>( async ( ) =>
                await activator.ActivateAsync( HerdrTargetKinds.Workspace, cancellation.Token ) );
        }
        else
        {
            var outcome = await activator.ActivateAsync( HerdrTargetKinds.Workspace, cancellation.Token );
            True( !outcome.Succeeded );
        }

        await server.Completion;
        Equal( 2, server.Requests.Count );
        True( server.Requests[0].Contains( "client.window_title.set", StringComparison.Ordinal ) );
        True( server.Requests[1].Contains( "client.window_title.clear", StringComparison.Ordinal ) );
    }

    private static string TitleResponse( string reason ) =>
        """{"id":"commanddeck:window-title","result":{"type":"client_window_title","changed":true,"reason":"$REASON"}}"""
            .Replace( "$REASON", reason, StringComparison.Ordinal );

    private sealed class HerdrTitleServer : IAsyncDisposable
    {
        private readonly System.Net.Sockets.Socket _listener;
        private readonly CancellationTokenSource _timeout = new( TimeSpan.FromSeconds( 2 ) );

        public HerdrTitleServer( params string?[] responses )
        {
            SocketPath = Path.Combine(
                Path.GetTempPath( ),
                $"commanddeck-herdr-title-{Guid.NewGuid( ):N}.sock" );
            _listener = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.Unix,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Unspecified );
            _listener.Bind( new System.Net.Sockets.UnixDomainSocketEndPoint( SocketPath ) );
            _listener.Listen( responses.Length );
            Completion = ServeAsync( responses );
        }

        public string SocketPath { get; }
        public List<string> Requests { get; } = [];
        public Task Completion { get; }

        public Action? ResponseWithheld { get; init; }

        private async Task ServeAsync( IReadOnlyList<string?> responses )
        {
            foreach ( var response in responses )
            {
                using var socket = await _listener.AcceptAsync( _timeout.Token );
                using System.Net.Sockets.NetworkStream stream = new( socket, ownsSocket: false );
                using StreamReader reader = new( stream, leaveOpen: true );
                Requests.Add( await reader.ReadLineAsync( _timeout.Token ) ?? "" );
                if ( response is null )
                {
                    // Apply the request but withhold acknowledgement until the
                    // client disconnects on its own timeout or cancellation.
                    ResponseWithheld?.Invoke( );
                    Equal<string?>( null, await reader.ReadLineAsync( _timeout.Token ) );
                    continue;
                }

                await stream.WriteAsync(
                    System.Text.Encoding.UTF8.GetBytes( response + "\n" ),
                    _timeout.Token );
                await stream.FlushAsync( _timeout.Token );
            }
        }

        public async ValueTask DisposeAsync( )
        {
            await _timeout.CancelAsync( );
            _listener.Dispose( );
            _timeout.Dispose( );
            File.Delete( SocketPath );
        }
    }
}
