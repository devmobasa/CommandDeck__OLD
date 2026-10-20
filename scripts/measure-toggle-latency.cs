#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

// Manual acceptance check for an installed CommandDeck on a live Omarchy
// session. It toggles the launcher the way the hotkey does, by dispatching the
// commanddeck:toggle global shortcut through Hyprland's request socket, and
// times how long the launcher's layer surface takes to map, as reported by
// `openlayer>>commanddeck` on Hyprland's event socket. It then closes the
// launcher again. It never types keys and refuses to run without a Hyprland
// session. `--self-test` checks its parsing and statistics on recorded lines.

using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

[assembly: SupportedOSPlatform( "linux" )]

return await ToggleLatency.RunAsync( args );

static class ToggleLatency
{
    public const string Namespace = "commanddeck";
    public const string ShortcutId = "commanddeck:toggle";
    public const string DispatchRequest = "dispatch hl.dsp.global(\"" + ShortcutId + "\")";
    public static readonly string[] ShellToggle = ["omarchy-shell", "shell", "toggle", "local.commanddeck", "{}"];

    private const string Usage = """
        Usage:
          dotnet scripts/measure-toggle-latency.cs --live [--samples N] [--timeout-ms MS] [--path global|shell]
          dotnet scripts/measure-toggle-latency.cs --self-test

        --live         Toggle the launcher on this Hyprland session and time it (required).
        --samples N    Open/close cycles to time, 1-200 (default 20).
        --timeout-ms   Longest wait for each layer event, 100-10000 (default 2000).
        --path         global: Hyprland's global dispatcher, as the hotkey does (default).
                       shell: start `omarchy-shell shell toggle` for each toggle, for comparison.
        """;

    public static async Task<int> RunAsync( string[] args )
    {
        if ( args is ["--self-test"] )
        {
            return SelfTest.Run( );
        }

        Options options;
        try
        {
            options = Options.Parse( args );
        }
        catch ( ArgumentException exception )
        {
            Console.Error.WriteLine( exception.Message );
            Console.Error.WriteLine( Usage );
            return 2;
        }

        var refusal = HyprlandSession.Refusal( Environment.GetEnvironmentVariable, File.Exists );
        if ( refusal is not null )
        {
            Console.Error.WriteLine( $"Refusing to run: {refusal}" );
            return 2;
        }

        await using var session = await HyprlandSession.ConnectAsync( Environment.GetEnvironmentVariable );
        return await MeasureAsync( session, options );
    }

    private static async Task<int> MeasureAsync( HyprlandSession session, Options options )
    {
        if ( !LayerEvents.HasShortcut( await session.RequestAsync( "j/globalshortcuts" ) ) )
        {
            Console.Error.WriteLine( $"Refusing to run: {ShortcutId} is not registered; enable the plugin and restart the shell." );
            return 2;
        }

        if ( LayerEvents.HasLayer( await session.RequestAsync( "j/layers" ), Namespace ) )
        {
            Console.Error.WriteLine( "Refusing to run: CommandDeck is open; close it first." );
            return 2;
        }

        List<double> opens = [];
        List<double> closes = [];
        var open = false;
        try
        {
            for ( var sample = 1; sample <= options.Samples; sample++ )
            {
                var opened = await ToggleAsync( session, options, "openlayer" );
                open = opened is not null;
                var closed = open ? await ToggleAsync( session, options, "closelayer" ) : null;
                open = open && closed is null;
                if ( opened is null || closed is null )
                {
                    Console.Error.WriteLine( $"Sample {sample}: no {(opened is null ? "openlayer" : "closelayer")}>>{Namespace} event within {options.TimeoutMilliseconds} ms." );
                    return 1;
                }

                opens.Add( opened.Value );
                closes.Add( closed.Value );
                Console.WriteLine( $"sample {sample,3}: open {opened.Value,7:F1} ms   close {closed.Value,7:F1} ms" );
                await Task.Delay( options.SettleMilliseconds );
            }
        }
        finally
        {
            // A late close event may still be on its way; toggle only a launcher that is open.
            if ( open && LayerEvents.HasLayer( await session.RequestAsync( "j/layers" ), Namespace ) )
            {
                await session.ToggleAsync( options.Via );
            }
        }

        Console.WriteLine( Percentiles.Describe( "open ", opens ) );
        Console.WriteLine( Percentiles.Describe( "close", closes ) );
        return 0;
    }

    // Milliseconds from the toggle request to the matching layer event, or null.
    private static async Task<double?> ToggleAsync( HyprlandSession session, Options options, string kind )
    {
        session.DrainEvents( );
        var started = Stopwatch.GetTimestamp( );
        await session.ToggleAsync( options.Via );
        using CancellationTokenSource timeout = new( options.TimeoutMilliseconds );
        try
        {
            while ( true )
            {
                var received = await session.Events.ReadAsync( timeout.Token );
                if ( LayerEvents.Matches( received.Line, kind, Namespace ) )
                {
                    await Task.Delay( options.SettleMilliseconds );
                    return Stopwatch.GetElapsedTime( started, received.Timestamp ).TotalMilliseconds;
                }
            }
        }
        catch ( OperationCanceledException )
        {
            return null;
        }
    }
}

sealed record Options( int Samples, int TimeoutMilliseconds, string Via, int SettleMilliseconds )
{
    public static Options Parse( IReadOnlyList<string> args )
    {
        if ( !args.Contains( "--live" ) )
        {
            throw new ArgumentException( "Pass --live to toggle the launcher on this session." );
        }

        var samples = 20;
        var timeout = 2000;
        var via = "global";
        for ( var index = 0; index < args.Count; index++ )
        {
            switch ( args[index] )
            {
                case "--live":
                    break;
                case "--samples":
                    samples = Number( args, ++index, 1, 200 );
                    break;
                case "--timeout-ms":
                    timeout = Number( args, ++index, 100, 10_000 );
                    break;
                case "--path" when index + 1 < args.Count && args[index + 1] is "global" or "shell":
                    via = args[++index];
                    break;
                default:
                    throw new ArgumentException( $"Unknown or incomplete argument: {args[index]}" );
            }
        }

        return new( samples, timeout, via, 250 );
    }

    private static int Number( IReadOnlyList<string> args, int index, int minimum, int maximum ) =>
        index < args.Count && int.TryParse( args[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value ) &&
        value >= minimum && value <= maximum
            ? value
            : throw new ArgumentException( $"{args[index - 1]} takes a whole number from {minimum} to {maximum}." );
}

static class LayerEvents
{
    // Hyprland event lines are EVENT>>DATA; openlayer and closelayer carry the namespace.
    public static bool Matches( string line, string kind, string layerNamespace )
    {
        var separator = line.IndexOf( ">>", StringComparison.Ordinal );
        return separator > 0 &&
            line.AsSpan( 0, separator ).SequenceEqual( kind ) &&
            line.AsSpan( separator + 2 ).TrimEnd( '\r' ).SequenceEqual( layerNamespace );
    }

    public static bool HasShortcut( string json )
    {
        using var document = JsonDocument.Parse( json );
        return document.RootElement.ValueKind == JsonValueKind.Array &&
            document.RootElement.EnumerateArray( ).Any( static entry =>
                entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty( "name", out var name ) &&
                name.ValueKind == JsonValueKind.String &&
                name.GetString( ) == ToggleLatency.ShortcutId );
    }

    // `hyprctl -j layers` nests surfaces under monitors and levels.
    public static bool HasLayer( string json, string layerNamespace )
    {
        using var document = JsonDocument.Parse( json );
        return Contains( document.RootElement, layerNamespace );
    }

    private static bool Contains( JsonElement element, string layerNamespace ) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject( ).Any( property =>
            property.Name == "namespace" && property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString( ) == layerNamespace
                : Contains( property.Value, layerNamespace ) ),
        JsonValueKind.Array => element.EnumerateArray( ).Any( item => Contains( item, layerNamespace ) ),
        _ => false
    };
}

static class Percentiles
{
    // Nearest rank: the smallest sample with at least q of the samples at or below it.
    public static double NearestRank( IReadOnlyList<double> samples, double quantile )
    {
        if ( samples.Count == 0 || quantile is <= 0 or > 1 )
        {
            throw new ArgumentException( "Percentiles need samples and a quantile in (0, 1]." );
        }

        // The tolerance keeps 0.95 * 100 from rounding up to rank 96.
        var sorted = samples.Order( ).ToArray( );
        var rank = ( int ) Math.Ceiling( (quantile * sorted.Length) - 1e-9 );
        return sorted[Math.Max( rank, 1 ) - 1];
    }

    public static string Describe( string label, IReadOnlyList<double> samples ) =>
        string.Create( CultureInfo.InvariantCulture,
            $"{label} p50 {NearestRank( samples, 0.50 ):F1} ms  p95 {NearestRank( samples, 0.95 ):F1} ms  " +
            $"min {samples.Min( ):F1} ms  max {samples.Max( ):F1} ms  ({samples.Count} samples)" );
}

sealed class HyprlandSession : IAsyncDisposable
{
    private readonly string _requestSocket;
    private readonly Socket _events;
    private readonly CancellationTokenSource _stop = new( );
    private readonly Task _reader;
    private readonly Channel<(long Timestamp, string Line)> _lines = Channel.CreateUnbounded<(long, string)>( );

    private HyprlandSession( string requestSocket, Socket events )
    {
        _requestSocket = requestSocket;
        _events = events;
        _reader = ReadEventsAsync( );
    }

    public ChannelReader<(long Timestamp, string Line)> Events => _lines.Reader;

    public static string? Refusal( Func<string, string?> environment, Func<string, bool> exists )
    {
        var instance = environment( "HYPRLAND_INSTANCE_SIGNATURE" );
        var runtime = environment( "XDG_RUNTIME_DIR" );
        if ( string.IsNullOrEmpty( instance ) || string.IsNullOrEmpty( runtime ) )
        {
            return "no Hyprland session (HYPRLAND_INSTANCE_SIGNATURE and XDG_RUNTIME_DIR are required).";
        }

        var directory = Path.Combine( runtime, "hypr", instance );
        return exists( Path.Combine( directory, ".socket.sock" ) ) && exists( Path.Combine( directory, ".socket2.sock" ) )
            ? null
            : $"Hyprland's sockets are missing in {directory}.";
    }

    public static async Task<HyprlandSession> ConnectAsync( Func<string, string?> environment )
    {
        var directory = Path.Combine( environment( "XDG_RUNTIME_DIR" )!, "hypr", environment( "HYPRLAND_INSTANCE_SIGNATURE" )! );
        Socket events = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        await events.ConnectAsync( new UnixDomainSocketEndPoint( Path.Combine( directory, ".socket2.sock" ) ) );
        return new( Path.Combine( directory, ".socket.sock" ), events );
    }

    public async Task<string> RequestAsync( string request )
    {
        using Socket socket = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 2 ) );
        await socket.ConnectAsync( new UnixDomainSocketEndPoint( _requestSocket ), timeout.Token );
        await socket.SendAsync( Encoding.UTF8.GetBytes( request ), SocketFlags.None, timeout.Token );
        using MemoryStream reply = new( );
        var buffer = new byte[64 * 1024];
        int read;
        while ( (read = await socket.ReceiveAsync( buffer, SocketFlags.None, timeout.Token )) > 0 && reply.Length < 8 * 1024 * 1024 )
        {
            reply.Write( buffer, 0, read );
        }

        return Encoding.UTF8.GetString( reply.ToArray( ) );
    }

    public async Task ToggleAsync( string via )
    {
        if ( via == "shell" )
        {
            ProcessStartInfo start = new( ToggleLatency.ShellToggle[0] ) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach ( var argument in ToggleLatency.ShellToggle[1..] )
            {
                start.ArgumentList.Add( argument );
            }

            using var process = Process.Start( start ) ?? throw new InvalidOperationException( "omarchy-shell did not start." );
            var output = process.StandardOutput.ReadToEndAsync( );
            var error = process.StandardError.ReadToEndAsync( );
            await process.WaitForExitAsync( );
            if ( process.ExitCode != 0 )
            {
                throw new InvalidOperationException( $"omarchy-shell exited with {process.ExitCode}: {await error}{await output}" );
            }

            return;
        }

        var reply = (await RequestAsync( ToggleLatency.DispatchRequest )).Trim( );
        if ( reply != "ok" )
        {
            throw new InvalidOperationException( $"Hyprland rejected `{ToggleLatency.DispatchRequest}`: {reply}" );
        }
    }

    public void DrainEvents( )
    {
        while ( _lines.Reader.TryRead( out _ ) )
        {
        }
    }

    private async Task ReadEventsAsync( )
    {
        using NetworkStream stream = new( _events, ownsSocket: false );
        using StreamReader reader = new( stream, Encoding.UTF8 );
        try
        {
            while ( await reader.ReadLineAsync( _stop.Token ) is { } line )
            {
                _lines.Writer.TryWrite( (Stopwatch.GetTimestamp( ), line) );
            }
        }
        catch ( Exception exception ) when ( exception is OperationCanceledException or IOException or ObjectDisposedException )
        {
        }
        finally
        {
            _lines.Writer.TryComplete( );
        }
    }

    public async ValueTask DisposeAsync( )
    {
        await _stop.CancelAsync( );
        _events.Dispose( );
        await _reader;
        _stop.Dispose( );
    }
}

static class SelfTest
{
    private const string RecordedEvents = """
        activewindow>>kitty,~
        openlayer>>commanddeck-hud
        openlayer>>commanddeck
        workspacev2>>2,2
        closelayer>>commanddeck
        openlayer>>quickshell
        openlayer>>commanddeck
        garbage
        """;

    private const string RecordedLayers = """
        {"DP-1":{"levels":{"0":[{"namespace":"omarchy-background"}],"3":[{"address":"0x1","namespace":"commanddeck"}]}}}
        """;

    public static int Run( )
    {
        List<string> failures = [];
        void Check( bool condition, string name )
        {
            Console.WriteLine( $"{(condition ? "PASS" : "FAIL")} {name}" );
            if ( !condition )
            {
                failures.Add( name );
            }
        }

        var lines = RecordedEvents.Split( '\n' );
        Check( lines.Count( line => LayerEvents.Matches( line, "openlayer", "commanddeck" ) ) == 2, "openlayer matches only the launcher namespace" );
        Check( lines.Count( line => LayerEvents.Matches( line, "closelayer", "commanddeck" ) ) == 1, "closelayer matches the launcher namespace" );
        Check( !LayerEvents.Matches( "openlayer>>commanddeck-hud", "openlayer", "commanddeck" ), "the HUD namespace is not the launcher" );
        Check( LayerEvents.Matches( "openlayer>>commanddeck\r", "openlayer", "commanddeck" ), "a trailing carriage return is ignored" );
        Check( !LayerEvents.Matches( ">>commanddeck", "openlayer", "commanddeck" ) && !LayerEvents.Matches( "", "openlayer", "commanddeck" ), "malformed lines never match" );

        Check( LayerEvents.HasLayer( RecordedLayers, "commanddeck" ), "an open launcher is found in hyprctl layers" );
        Check( !LayerEvents.HasLayer( RecordedLayers, "commanddeck-hud" ), "an absent namespace is not found" );
        Check( LayerEvents.HasShortcut( """[{"name":"commanddeck:toggle","description":"Toggle CommandDeck"}]""" ), "the registered shortcut is found" );
        Check( !LayerEvents.HasShortcut( """[{"name":"other:toggle"}]""" ), "another shortcut does not count" );
        Check( ToggleLatency.DispatchRequest == "dispatch hl.dsp.global(\"commanddeck:toggle\")", "the toggle uses the hotkey's global dispatcher" );

        double[] twenty = [.. Enumerable.Range( 1, 20 ).Select( static value => value * 10.0 ).Reverse( )];
        Check( Percentiles.NearestRank( twenty, 0.50 ) == 100 && Percentiles.NearestRank( twenty, 0.95 ) == 190, "nearest-rank p50 and p95 of 20 unsorted samples" );
        Check( Percentiles.NearestRank( [42.5], 0.95 ) == 42.5, "one sample is every percentile" );
        double[] hundred = [.. Enumerable.Range( 1, 100 ).Select( static value => ( double ) value )];
        Check( Percentiles.NearestRank( hundred, 0.95 ) == 95, "p95 of 100 samples is rank 95 despite float rounding" );
        Check( Percentiles.Describe( "open ", [1, 2, 3] ).StartsWith( "open  p50 2.0 ms  p95 3.0 ms  min 1.0 ms  max 3.0 ms", StringComparison.Ordinal ), "the summary line" );
        Check( Throws( ( ) => Percentiles.NearestRank( [], 0.5 ) ), "no samples is an error" );

        Check( Throws( ( ) => Options.Parse( [] ) ), "measurement requires --live" );
        Check( Options.Parse( ["--live", "--samples", "5", "--path", "shell"] ) == new Options( 5, 2000, "shell", 250 ), "options parse" );
        Check( Throws( ( ) => Options.Parse( ["--live", "--samples", "0"] ) ) && Throws( ( ) => Options.Parse( ["--live", "--path", "keys"] ) ), "invalid options are rejected" );

        Check( HyprlandSession.Refusal( static _ => null, static _ => true ) is not null, "no Hyprland session is refused" );
        Check( HyprlandSession.Refusal( static name => name == "HYPRLAND_INSTANCE_SIGNATURE" ? "sig" : "/run/user/1", static _ => false ) is not null, "missing sockets are refused" );
        Check( HyprlandSession.Refusal( static name => name == "HYPRLAND_INSTANCE_SIGNATURE" ? "sig" : "/run/user/1", static _ => true ) is null, "a live session is accepted" );

        var window = Path.Combine( "qml", "common", "CommandDeckWindow.qml" );
        Check( File.Exists( window ) && File.ReadAllText( window ).Contains( $"WlrLayershell.namespace: \"{ToggleLatency.Namespace}\"", StringComparison.Ordinal ),
            "the launcher window declares the measured namespace (run from the repository root)" );

        Console.WriteLine( failures.Count == 0 ? "Self-test passed." : $"{failures.Count} self-test check(s) failed." );
        return failures.Count == 0 ? 0 : 1;
    }

    private static bool Throws( Action action )
    {
        try
        {
            action( );
            return false;
        }
        catch ( ArgumentException )
        {
            return true;
        }
    }
}
