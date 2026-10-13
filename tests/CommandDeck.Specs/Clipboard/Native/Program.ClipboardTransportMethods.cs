using System.Diagnostics;
using System.IO.Pipes;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static Task ClipboardTransportCancelsUnderDataBackpressureAsync( ) =>
        ExerciseClipboardTransportAsync( ClipboardTransportFixtureStart( acknowledge: true ) );

    private static async Task ExerciseClipboardTransportAsync( ProcessStartInfo start )
    {
        using AnonymousPipeServerStream unrelated = new( PipeDirection.Out, HandleInheritability.Inheritable );
        var sentinel = new FileInfo( "/proc/self/fd/" + unrelated.GetClientHandleAsString( ) ).LinkTarget;
        await using var helper = new ClipboardHostProcess( start, 7 );
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 10 ) );
        var first = await helper.Events.ReadAsync( timeout.Token );
        Equal( ClipboardHelperFrameType.Status, first.Header.Type );
        await ThrowsAsync<InvalidDataException>( async ( ) => await helper.SendAsync(
            new( new( ClipboardHelperFrameType.Configure, 99, Guid.Empty, -1, 0, 0 ), ReadOnlyMemory<byte>.Empty ), timeout.Token ) );
        True( !Directory.EnumerateFiles( $"/proc/{helper.ProcessId}/fd" ).Any( path => new FileInfo( path ).LinkTarget == sentinel ),
            "an unrelated inheritable pipe was excluded from the helper" );
        // Leave inbound frames unread. Also fill the outbound pipe: the fixture never reads stdin.
        var sending = FloodClipboardHelperAsync( helper, timeout.Token );
        await Task.Delay( 100, timeout.Token );
        True( !sending.IsCompleted, "outbound writes encounter backpressure" );
        True( await helper.CancelAsync( Guid.NewGuid( ), timeout.Token ), "independent control acknowledgement survives both full data paths" );
        var pid = helper.ProcessId;
        await helper.DisposeAsync( );
        try
        {
            await sending;
        }
        catch ( Exception exception ) when ( exception is OperationCanceledException or System.Threading.Channels.ChannelClosedException ) { }
        True( !Directory.Exists( $"/proc/{pid}" ), "helper was reaped before disposal completed" );
    }

    private static async Task ClipboardTransportKillsAnUnresponsiveHelperAsync( )
    {
        await using var helper = new ClipboardHostProcess( ClipboardTransportFixtureStart( acknowledge: false ), 8 );
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 10 ) );
        _ = await helper.Events.ReadAsync( timeout.Token );
        var pid = helper.ProcessId;
        True( !await helper.CancelAsync( Guid.NewGuid( ), timeout.Token ), "missing acknowledgement revokes the helper" );
        True( !Directory.Exists( $"/proc/{pid}" ), "unresponsive helper is killed and reaped" );
    }

    private static async Task FloodClipboardHelperAsync( ClipboardHostProcess helper, CancellationToken cancellationToken )
    {
        var bytes = new byte[ClipboardHelperHeader.MaximumPayload];
        var selection = Guid.NewGuid( );
        for ( uint index = 0; index < 100; index++ )
        {
            await helper.SendAsync( new( new( ClipboardHelperFrameType.WriteChunk, 7, selection, 0, index, bytes.Length ), bytes ), cancellationToken );
        }
    }

    private static ProcessStartInfo ClipboardTransportFixtureStart( bool acknowledge )
    {
        var executable = Environment.ProcessPath;
        if ( executable is null )
        {
            throw new InvalidOperationException( "Spec executable is unavailable." );
        }

        ProcessStartInfo start = new( executable );
        if ( Path.GetFileNameWithoutExtension( executable ) == "dotnet" )
        {
            start.ArgumentList.Add( typeof( Program ).Assembly.Location );
        }

        start.ArgumentList.Add( "--clipboard-transport-fixture" );
        start.ArgumentList.Add( acknowledge ? "ack" : "ignore" );
        return start;
    }

    private static Task<int> RunClipboardTransportFixtureAsync( string[] args ) => ClipboardHostTransportProbe.RunAsync(
        ReadArgumentValue( args, "--control-read" ) ?? "", ReadArgumentValue( args, "--control-write" ) ?? "",
        ulong.Parse( ReadArgumentValue( args, "--host-epoch" ) ?? "0", System.Globalization.CultureInfo.InvariantCulture ),
        args[1] == "ack", CancellationToken.None );

    private static async Task<int> RunClipboardTransportAotProbeAsync( string executable )
    {
        ProcessStartInfo start = new( executable );
        start.ArgumentList.Add( "clipboard-host" );
        start.ArgumentList.Add( "--transport-probe" );
        await ExerciseClipboardTransportAsync( start );
        Console.WriteLine( "Native AOT clipboard transport: inherited handle isolation and cancellation under bidirectional backpressure passed." );
        return 0;
    }
}
