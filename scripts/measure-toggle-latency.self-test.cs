using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

// Run the actual measurement entry point in a child whose only desktop is two
// private sockets. A fake shell records hide by creating files in its directory.
static class CleanupProcessTest
{
    public static async Task<bool> RunAsync( string signal, string via = "global" )
    {
        var directory = Directory.CreateTempSubdirectory( "cd-latency-" ).FullName;
        var socketDirectory = Path.Combine( directory, "hypr", "test" );
        Directory.CreateDirectory( socketDirectory );
        var groupSignal = signal == "group-SIGINT";
        var shell = Path.Combine( directory, "omarchy-shell" );
        await File.WriteAllTextAsync( shell, """
            #!/bin/sh
            if [ "$2" = toggle ]; then
              echo $$ > toggle-pid
              /usr/bin/touch opened
              exec /usr/bin/sleep 30
            fi
            if [ -e toggle-pid ] && /usr/bin/kill -0 "$(/usr/bin/cat toggle-pid)" 2>/dev/null; then
              /usr/bin/touch toggle-alive-at-hide
            fi
            trap '/usr/bin/touch interrupted; exit 130' INT
            if [ ! -e attempted ]; then
              /usr/bin/touch attempted
              case "$CLEANUP_CASE" in
                retry) exit 7 ;;
                retry-timeout) exec /usr/bin/sleep 30 ;;
              esac
            else
              /usr/bin/touch retried
            fi
            /usr/bin/touch hide-started
            while [ ! -e release-hide ]; do /usr/bin/sleep 0.01; done
            /usr/bin/touch "$@"
            """ );
        File.SetUnixFileMode( shell, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        if ( !groupSignal )
        {
            await File.WriteAllTextAsync( Path.Combine( directory, "release-hide" ), "" );
        }

        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 15 ) );
        using var requests = Listen( Path.Combine( socketDirectory, ".socket.sock" ) );
        using var events = Listen( Path.Combine( socketDirectory, ".socket2.sock" ) );
        TaskCompletionSource dispatched = new( TaskCreationOptions.RunContinuationsAsynchronously );
        var serve = ServeAsync( requests, dispatched, deadline.Token );
        var timesOut = signal is "timeout" or "retry" or "retry-timeout" || groupSignal;
        var childStart = ChildStart( directory, timesOut, via );
        childStart.Environment["CLEANUP_CASE"] = signal;
        using var child = Process.Start( childStart );
        if ( child is null )
        {
            throw new InvalidOperationException( "The isolated measurement process did not start." );
        }

        var output = child.StandardOutput.ReadToEndAsync( );
        var error = child.StandardError.ReadToEndAsync( );
        try
        {
            using var eventClient = await events.AcceptAsync( deadline.Token );
            await AwaitToggleAsync( eventClient, dispatched, directory, via, deadline.Token );
            if ( !timesOut || groupSignal )
            {
                if ( groupSignal )
                {
                    while ( !File.Exists( Path.Combine( directory, "hide-started" ) ) )
                    {
                        await Task.Delay( 10, deadline.Token );
                    }
                }

                await SendSignalAsync( groupSignal ? "SIGINT" : signal, groupSignal ? -child.Id : child.Id, deadline.Token );

                if ( groupSignal )
                {
                    await Task.Delay( 100, deadline.Token );
                    await File.WriteAllTextAsync( Path.Combine( directory, "release-hide" ), "", deadline.Token );
                }
            }

            await child.WaitForExitAsync( deadline.Token );
            var cleaned = File.Exists( Path.Combine( directory, "shell" ) )
                && File.Exists( Path.Combine( directory, "hide" ) )
                && File.Exists( Path.Combine( directory, "local.commanddeck" ) );
            var retried = File.Exists( Path.Combine( directory, "retried" ) );
            var interrupted = File.Exists( Path.Combine( directory, "interrupted" ) );
            var toggleAlive = File.Exists( Path.Combine( directory, "toggle-alive-at-hide" ) );
            var passed = child.ExitCode == (timesOut ? 1 : 130) && cleaned && !interrupted;
            passed &= !toggleAlive && retried == (signal is "retry" or "retry-timeout");
            if ( !passed )
            {
                Console.Error.WriteLine( $"Isolated {via} {signal}: exit {child.ExitCode}, cleanup {cleaned}, toggle alive at hide {toggleAlive}; {await output}{await error}" );
            }

            return passed;
        }
        catch ( OperationCanceledException ) when ( deadline.IsCancellationRequested )
        {
            Console.Error.WriteLine( $"Isolated {via} {signal}: measurement did not finish cleanup before the test deadline." );
            return false;
        }
        finally
        {
            if ( !child.HasExited )
            {
                child.Kill( entireProcessTree: true );
                await child.WaitForExitAsync( );
            }

            await deadline.CancelAsync( );
            await serve;
            Directory.Delete( directory, recursive: true );
        }
    }

    private static async Task AwaitToggleAsync( Socket events, TaskCompletionSource dispatched, string directory, string via,
        CancellationToken cancellationToken )
    {
        if ( via == "global" )
        {
            // No openlayer is delivered: the measurement is waiting on a delayed open.
            await dispatched.Task.WaitAsync( cancellationToken );
            return;
        }

        while ( !File.Exists( Path.Combine( directory, "opened" ) ) )
        {
            await Task.Delay( 10, cancellationToken );
        }

        await events.SendAsync( Encoding.UTF8.GetBytes( "openlayer>>commanddeck\n" ), SocketFlags.None, cancellationToken );
    }

    private static async Task SendSignalAsync( string signal, int target, CancellationToken cancellationToken )
    {
        ProcessStartInfo start = new( "/usr/bin/kill" );
        start.ArgumentList.Add( "-s" );
        start.ArgumentList.Add( signal );
        start.ArgumentList.Add( "--" );
        start.ArgumentList.Add( target.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
        using var sender = Process.Start( start );
        if ( sender is null )
        {
            throw new InvalidOperationException( "The signal sender did not start." );
        }

        await sender.WaitForExitAsync( cancellationToken );
        if ( sender.ExitCode != 0 )
        {
            throw new InvalidOperationException( "The signal sender failed." );
        }
    }

    private static ProcessStartInfo ChildStart( string directory, bool timesOut, string via )
    {
        var executable = Environment.ProcessPath;
        if ( executable is null )
        {
            throw new InvalidOperationException( "The current executable path is unavailable." );
        }

        ProcessStartInfo start = new( "/usr/bin/setsid" )
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add( "--wait" );
        start.ArgumentList.Add( executable );
        if ( Path.GetFileNameWithoutExtension( executable ) == "dotnet" )
        {
            start.ArgumentList.Add( Assembly.GetExecutingAssembly( ).Location );
        }

        var timeout = timesOut ? via == "shell" ? "500" : "100" : "10000";
        foreach ( var argument in new[] { "--live", "--samples", "1", "--timeout-ms", timeout, "--path", via } )
        {
            start.ArgumentList.Add( argument );
        }

        start.Environment["XDG_RUNTIME_DIR"] = directory;
        start.Environment["HYPRLAND_INSTANCE_SIGNATURE"] = "test";
        start.Environment["PATH"] = directory;
        start.Environment.Remove( "WAYLAND_DISPLAY" );
        start.Environment.Remove( "DISPLAY" );
        return start;
    }

    private static Socket Listen( string path )
    {
        Socket socket = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        socket.Bind( new UnixDomainSocketEndPoint( path ) );
        socket.Listen( 4 );
        return socket;
    }

    private static async Task ServeAsync( Socket listener, TaskCompletionSource dispatched, CancellationToken cancellationToken )
    {
        try
        {
            while ( true )
            {
                using var client = await listener.AcceptAsync( cancellationToken );
                var buffer = new byte[4096];
                var length = await client.ReceiveAsync( buffer, SocketFlags.None, cancellationToken );
                var request = Encoding.UTF8.GetString( buffer, 0, length );
                var reply = request switch
                {
                    "j/globalshortcuts" => """[{"name":"commanddeck:toggle"}]""",
                    "j/layers" => "{}",
                    ToggleLatency.DispatchRequest => "ok",
                    _ => throw new InvalidOperationException( $"Unexpected isolated request: {request}" )
                };
                await client.SendAsync( Encoding.UTF8.GetBytes( reply ), SocketFlags.None, cancellationToken );
                client.Shutdown( SocketShutdown.Send );
                if ( request == ToggleLatency.DispatchRequest )
                {
                    dispatched.SetResult( );
                }
            }
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
        }
    }
}
