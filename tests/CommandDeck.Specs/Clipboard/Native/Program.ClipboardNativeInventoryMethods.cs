using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static Task ClipboardNativeInventoryAsync( ) => ExerciseClipboardNativeInventoryAsync( null );

    private static Task ClipboardNativeControlWithOneWorkerAsync( ) =>
        ExerciseClipboardNativeInventoryAsync( null, singleWorker: true );

    private static Task<int> RunClipboardNativeFixtureAsync( string[] args )
    {
        if ( args.Contains( "--single-worker", StringComparer.Ordinal ) )
        {
            ThreadPool.GetMinThreads( out _, out var minimumIo );
            ThreadPool.GetMaxThreads( out _, out var maximumIo );
            True( ThreadPool.SetMinThreads( 1, minimumIo ) );
            True( ThreadPool.SetMaxThreads( 1, maximumIo ) );
        }

        return ClipboardNativeHost.RunAsync(
            ReadArgumentValue( args, "--control-read" ) ?? "", ReadArgumentValue( args, "--control-write" ) ?? "",
            ulong.Parse( ReadArgumentValue( args, "--host-epoch" ) ?? "0", System.Globalization.CultureInfo.InvariantCulture ), CancellationToken.None );
    }

    private static async Task ExerciseClipboardNativeInventoryAsync( string? executable, bool singleWorker = false )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.PathFor( "commanddeck-native-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        var serverStart = ClipboardPrivateFixtureStart( "--clipboard-wayland-server", root );
        serverStart.RedirectStandardInput = true;
        serverStart.RedirectStandardOutput = true;
        serverStart.RedirectStandardError = true;
        using var server = Process.Start( serverStart );
        if ( server is null )
        {
            throw new IOException( "Synthetic compositor did not start." );
        }

        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 120 ) );
        var errors = server.StandardError.ReadToEndAsync( timeout.Token );
        try
        {
            var socket = await server.StandardOutput.ReadLineAsync( timeout.Token );
            if ( socket is null )
            {
                throw new InvalidOperationException(
                    "Private compositor exited before creating a socket: " + await errors );
            }

            True( socket.StartsWith( "wayland-", StringComparison.Ordinal ), "private compositor has a socket" );
            var start = ClipboardPrivateFixtureStart( "--clipboard-native-fixture", root );
            if ( singleWorker )
            {
                start.ArgumentList.Add( "--single-worker" );
            }

            if ( executable is not null )
            {
                start.FileName = Path.GetFullPath( executable );
                start.ArgumentList.Clear( );
                start.ArgumentList.Add( "clipboard-host" );
                start.ArgumentList.Add( "--capture" );
            }

            start.Environment["WAYLAND_DISPLAY"] = socket;
            await using var helper = new ClipboardHostProcess( start, 17 );
            var ready = helper.Events.ReadAsync( timeout.Token ).AsTask( );
            var readyFrame = singleWorker ? await ready.WaitAsync( TimeSpan.FromSeconds( 3 ) ) : await ready;
            Equal( ClipboardCaptureStatus.Ready, ClipboardCaptureProtocol.ReadStatus( readyFrame ).Status );
            await helper.SendAsync( ClipboardCaptureProtocol.Configure( 17, ClipboardConsent.Text ), timeout.Token );
            Equal( ClipboardCaptureStatus.Configured, ClipboardCaptureProtocol.ReadStatus( await helper.Events.ReadAsync( timeout.Token ) ).Status );

            await ProcessSleepsWhileIdleAsync( helper.ProcessId, "native clipboard helper", timeout.Token );

            foreach ( var scenario in new[] { "sensitive", "aliases", "owner", "primary", "ordinary" } )
            {
                await Send( scenario );
            }

            foreach ( var expected in new[] { ClipboardInventoryRejection.SensitiveHint, ClipboardInventoryRejection.AliasLimit, ClipboardInventoryRejection.SelfWrite } )
            {
                var skipped = ClipboardCaptureProtocol.ReadStatus( await helper.Events.ReadAsync( timeout.Token ) );
                Equal( ClipboardCaptureStatus.SelectionRejected, skipped.Status );
                Equal( expected, ( ClipboardInventoryRejection ) skipped.Detail );
            }

            var first = await helper.Events.ReadAsync( timeout.Token );
            var count = ClipboardCaptureProtocol.ReadSelectionStart( first );
            ClipboardHelperInventory inventory = new( 17, first.Header.SelectionId, count );
            while ( !inventory.Complete )
            {
                inventory.Accept( await helper.Events.ReadAsync( timeout.Token ) );
            }

            Equal( "application/x-fixture-ordinary", inventory.Names[1] );
            True( await helper.CancelAsync( first.Header.SelectionId, timeout.Token ), "native cancellation is acknowledged" );
            var cancelled = await helper.Events.ReadAsync( timeout.Token );
            Equal( ClipboardCaptureStatus.SelectionCancelled, ClipboardCaptureProtocol.ReadStatus( cancelled ).Status );
            Equal( first.Header.SelectionId, cancelled.Header.SelectionId );

            await helper.SendAsync( ClipboardCaptureProtocol.Configure( 17, ClipboardConsent.None, 1 ), timeout.Token );
            Equal( ClipboardCaptureStatus.Configured, ClipboardCaptureProtocol.ReadStatus( await helper.Events.ReadAsync( timeout.Token ) ).Status );
            await Send( "paused" );
            await helper.SendAsync( ClipboardCaptureProtocol.Configure( 17, ClipboardConsent.Text, 2 ), timeout.Token );
            Equal( ClipboardCaptureStatus.Configured, ClipboardCaptureProtocol.ReadStatus( await helper.Events.ReadAsync( timeout.Token ) ).Status );
            await Send( "many" );
            first = await helper.Events.ReadAsync( timeout.Token );
            Equal( 512, ClipboardCaptureProtocol.ReadSelectionStart( first ) );
            inventory = new( 17, first.Header.SelectionId, 512 );
            // Stop reading a large inventory once the bounded incoming queue
            // fills. Cancellation must still use the independent control pipe.
            True( await WaitUntilAsync( ( ) => Task.FromResult( helper.Events.Count == ClipboardHelperFraming.MaximumQueuedFrames ),
                TimeSpan.FromSeconds( 3 ) ), "inventory fills the bounded incoming queue" );
            True( await helper.CancelAsync( first.Header.SelectionId, timeout.Token ), "native cancellation bypasses inventory backpressure" );
            await server.StandardInput.WriteLineAsync( "count" );
            Equal( "0", await server.StandardOutput.ReadLineAsync( timeout.Token ) );
            while ( true )
            {
                var frame = await helper.Events.ReadAsync( timeout.Token );
                if ( frame.Header.Type == ClipboardHelperFrameType.Status )
                {
                    Equal( 17UL, frame.Header.HostEpoch );
                    Equal( first.Header.SelectionId, frame.Header.SelectionId );
                    Equal( ClipboardCaptureStatus.SelectionCancelled, ClipboardCaptureProtocol.ReadStatus( frame ).Status );
                    break;
                }

                inventory.Accept( frame );
            }
            await ExerciseClipboardNativeReceiveAsync( helper, server, timeout.Token );
            await helper.DisposeAsync( );
            await ExerciseClipboardNativeWatcherAsync( server, root, socket!, executable, timeout.Token );
            try
            {
                await ExerciseClipboardNativeServingAsync( server, root, socket!, executable, timeout.Token );
            }
            catch ( Exception exception )
            {
                if ( !server.HasExited )
                {
                    server.Kill( entireProcessTree: true );
                }

                throw new InvalidOperationException( "Synthetic compositor diagnostics: " + await errors, exception );
            }
            foreach ( var condition in new[] { "idle", "partial-input", "full-output" } )
            {
                await ClipboardNativeShutdownAsync( root, socket, executable, server, condition, timeout.Token );
            }
            await server.StandardInput.WriteLineAsync( "quit" );
            await server.WaitForExitAsync( timeout.Token );
            Equal( 0, server.ExitCode );
            Equal( "", await errors );
        }
        finally
        {
            if ( !server.HasExited )
            {
                server.Kill( entireProcessTree: true );
                await server.WaitForExitAsync( );
            }

            Directory.Delete( root, recursive: true );
        }

        async Task Send( string scenario )
        {
            await server.StandardInput.WriteLineAsync( scenario );
            Equal( "sent", await server.StandardOutput.ReadLineAsync( timeout.Token ) );
        }
    }

    // An armed helper with nothing to do must block in poll until work arrives, not wake on a timer.
    private static async Task ProcessSleepsWhileIdleAsync( int helper, string label, CancellationToken token )
    {
        await Task.Delay( 500, token );
        var before = ClipboardHelperVoluntarySwitches( helper );
        await Task.Delay( 1000, token );
        var wakeups = ClipboardHelperVoluntarySwitches( helper ) - before;
        Console.WriteLine( $"Idle {label}: {wakeups} voluntary context switches in one second." );
        True( wakeups <= 10, $"idle {label} woke {wakeups} times in one second" );
    }

    // Each blocking wait that ends, such as a timed-out poll, adds one voluntary switch to its thread.
    private static long ClipboardHelperVoluntarySwitches( int helper )
    {
        long total = 0;
        foreach ( var task in Directory.EnumerateDirectories( $"/proc/{helper}/task" ) )
        {
            try
            {
                var line = File.ReadLines( Path.Combine( task, "status" ) )
                    .Single( line => line.StartsWith( "voluntary_ctxt_switches:", StringComparison.Ordinal ) );
                total += long.Parse( line["voluntary_ctxt_switches:".Length..].Trim( ), System.Globalization.CultureInfo.InvariantCulture );
            }
            catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
            {
                // The thread exited while the counters were read.
            }
        }

        return total;
    }

    private static async Task<int> RunClipboardNativeAotAsync( string executable )
    {
        await ExerciseClipboardNativeInventoryAsync( executable );
        Console.WriteLine( "Native AOT clipboard inventory: private Wayland protocol and cancellation passed." );
        return 0;
    }

    private static ProcessStartInfo ClipboardPrivateFixtureStart( string command, string root )
    {
        var start = ClipboardTransportFixtureStart( acknowledge: true );
        start.ArgumentList.RemoveAt( start.ArgumentList.Count - 1 );
        start.ArgumentList.RemoveAt( start.ArgumentList.Count - 1 );
        start.ArgumentList.Add( command );
        start.Environment["XDG_RUNTIME_DIR"] = SpecTemporaryFiles.CreateSocketDirectory( root ).FullName;
        start.Environment.Remove( "WAYLAND_DISPLAY" );
        start.Environment.Remove( "WAYLAND_SOCKET" );
        start.Environment.Remove( "DISPLAY" );
        start.Environment.Remove( "DBUS_SESSION_BUS_ADDRESS" );
        return start;
    }
}
