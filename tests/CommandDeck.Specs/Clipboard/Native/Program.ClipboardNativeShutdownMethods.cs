using System.Diagnostics;
using System.IO.Pipes;

namespace CommandDeck.Specs;

public static partial class Program
{
    // Keep both inputs open: Shutdown itself, rather than EOF or forced disposal, must stop the host.
    private static async Task ClipboardNativeShutdownAsync( string root, string socket, string? executable,
        Process server, string condition, CancellationToken token )
    {
        var start = ClipboardPrivateFixtureStart( "--clipboard-native-fixture", root );
        if ( executable is not null )
        {
            start.FileName = Path.GetFullPath( executable );
            start.ArgumentList.Clear( );
            start.ArgumentList.Add( "clipboard-host" );
            start.ArgumentList.Add( "--capture" );
        }

        start.Environment["WAYLAND_DISPLAY"] = socket;
        using AnonymousPipeServerStream control = new( PipeDirection.Out );
        using AnonymousPipeServerStream acknowledgements = new( PipeDirection.In );
        start.InheritedHandles = [control.ClientSafePipeHandle, acknowledgements.ClientSafePipeHandle];
        start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
        start.ArgumentList.Add( "--control-read" );
        start.ArgumentList.Add( control.GetClientHandleAsString( ) );
        start.ArgumentList.Add( "--control-write" );
        start.ArgumentList.Add( acknowledgements.GetClientHandleAsString( ) );
        start.ArgumentList.Add( "--host-epoch" );
        start.ArgumentList.Add( "23" );
        using var helper = Process.Start( start );
        if ( helper is null )
        {
            throw new IOException( "Native shutdown fixture did not start." );
        }

        control.DisposeLocalCopyOfClientHandle( );
        acknowledgements.DisposeLocalCopyOfClientHandle( );
        var errors = helper.StandardError.ReadToEndAsync( token );
        try
        {
            var output = helper.StandardOutput.BaseStream;
            var ready = await ClipboardHelperFraming.ReadAsync( output, token );
            if ( ready is null )
            {
                throw new IOException( "Native shutdown fixture exited before readiness." );
            }

            Equal( ClipboardCaptureStatus.Ready, ClipboardCaptureProtocol.ReadStatus( ready ).Status );
            await ClipboardHelperFraming.WriteAsync( helper.StandardInput.BaseStream,
                ClipboardCaptureProtocol.Configure( 23, condition == "full-output" ? ClipboardConsent.Rich : ClipboardConsent.Text ), token );
            var configured = await ClipboardHelperFraming.ReadAsync( output, token );
            if ( configured is null )
            {
                throw new IOException( "Native shutdown fixture exited before configuration." );
            }

            Equal( ClipboardCaptureStatus.Configured, ClipboardCaptureProtocol.ReadStatus( configured ).Status );
            if ( condition == "partial-input" )
            {
                await helper.StandardInput.BaseStream.WriteAsync( new byte[] { 1 }, token );
                await helper.StandardInput.BaseStream.FlushAsync( token );
            }
            else if ( condition == "full-output" )
            {
                await server.StandardInput.WriteLineAsync( "finished" );
                var finished = await server.StandardOutput.ReadLineAsync( token );
                await server.StandardInput.WriteLineAsync( "blocked" );
                Equal( "sent", await server.StandardOutput.ReadLineAsync( token ) );
                var first = await ClipboardHelperFraming.ReadAsync( output, token );
                if ( first is null )
                {
                    throw new IOException( "Native shutdown fixture exited before its inventory." );
                }

                ClipboardHelperInventory inventory = new( 23, first.Header.SelectionId, ClipboardCaptureProtocol.ReadSelectionStart( first ) );
                while ( !inventory.Complete )
                {
                    var frame = await ClipboardHelperFraming.ReadAsync( output, token );
                    if ( frame is null )
                    {
                        throw new IOException( "Native shutdown fixture exited during its inventory." );
                    }

                    inventory.Accept( frame );
                }

                await ClipboardHelperFraming.WriteAsync( helper.StandardInput.BaseStream,
                    new( new( ClipboardHelperFrameType.ReceiveOffer, 23, first.Header.SelectionId, 0, 0, 0 ), ReadOnlyMemory<byte>.Empty ), token );
                var chunk = await ClipboardHelperFraming.ReadAsync( output, token );
                if ( chunk is null )
                {
                    throw new IOException( "Native shutdown fixture exited before receiving content." );
                }

                Equal( ClipboardHelperFrameType.OfferChunk, chunk.Header.Type );
                await Task.Delay( 100, token );
                await server.StandardInput.WriteLineAsync( "finished" );
                Equal( finished, await server.StandardOutput.ReadLineAsync( token ) );
                // The 16 MiB source cannot finish into the unread bounded output and event queue.
            }

            await Task.Delay( 100, token );
            await ClipboardHelperFraming.WriteAsync( control,
                new( new( ClipboardHelperFrameType.Shutdown, 23, Guid.Empty, -1, 0, 0 ), ReadOnlyMemory<byte>.Empty ), token );
            await helper.WaitForExitAsync( token ).WaitAsync( TimeSpan.FromSeconds( 3 ), token );
            Equal( 0, helper.ExitCode );
            Equal( "", await errors );
        }
        finally
        {
            if ( !helper.HasExited )
            {
                helper.Kill( entireProcessTree: true );
                await helper.WaitForExitAsync( CancellationToken.None );
            }
        }
    }
}
