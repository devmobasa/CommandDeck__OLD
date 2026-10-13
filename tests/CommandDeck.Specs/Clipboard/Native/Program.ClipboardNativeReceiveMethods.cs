using System.Diagnostics;
using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExerciseClipboardNativeReceiveAsync( ClipboardHostProcess helper, Process server, CancellationToken token )
    {
        foreach ( var scenario in new[] { "raw", "limit", "oversize", "gnome-copy", "gnome-cut" } )
        {
            var (selection, names, nextSequence) = await Announce( scenario );
            var plan = ClipboardOfferPlan.Create( names, ClipboardConsent.Text );
            ClipboardHelperReceiveState state = new( 17, selection,
                plan.Offers.Select( static offer => new KeyValuePair<int, long>( offer.InventoryIndex, offer.MaximumBytes ) ).ToArray( ), nextSequence );
            var rejected = false;
            for ( var index = 0; index < plan.Offers.Count; index++ )
            {
                await Request( selection, plan.Offers[index].InventoryIndex, ( uint ) index );
                using MemoryStream bytes = new( );
                while ( true )
                {
                    var frame = await helper.Events.ReadAsync( token );
                    if ( frame.Header.Type == ClipboardHelperFrameType.Status )
                    {
                        Equal( ClipboardCaptureStatus.SelectionCancelled, ClipboardCaptureProtocol.ReadStatus( frame ).Status );
                        Equal( selection, frame.Header.SelectionId );
                        rejected = true;
                        break;
                    }

                    state.Accept( frame );
                    if ( frame.Header.Type == ClipboardHelperFrameType.OfferEnd )
                    {
                        break;
                    }

                    bytes.Write( frame.Payload.Span );
                }

                if ( rejected )
                {
                    break;
                }

                var expected = plan.Offers[index].HeaderOnly ? "copy\n"u8.ToArray( )
                    : scenario == "limit" ? Enumerable.Repeat( ( byte ) 'x', 32768 ).ToArray( )
                    : Encoding.UTF8.GetBytes( "synthetic\r\n😀é\n" );
                True( bytes.ToArray( ).SequenceEqual( expected ), "native receive preserves exact bytes and the header-only boundary" );
            }

            Equal( scenario is "oversize" or "gnome-cut", rejected );
            if ( !rejected )
            {
                state.Accept( await helper.Events.ReadAsync( token ) );
                True( state.Complete, "selection ends only after every admitted offer" );
            }
        }

        await server.StandardInput.WriteLineAsync( "count" );
        Equal( "6", await server.StandardOutput.ReadLineAsync( token ) );
        await helper.SendAsync( ClipboardCaptureProtocol.Configure( 17, ClipboardConsent.Rich, 3 ), token );
        Equal( ClipboardCaptureStatus.Configured, ClipboardCaptureProtocol.ReadStatus( await helper.Events.ReadAsync( token ) ).Status );
        var (large, _, largeSequence) = await Announce( "large" );
        ClipboardHelperReceiveState largeState = new( 17, large, [new( 0, 16 * 1024 * 1024 )], largeSequence );
        await Request( large, 0, 0 );
        while ( !largeState.Complete )
        {
            largeState.Accept( await helper.Events.ReadAsync( token ) );
        }

        Equal( 16L * 1024 * 1024, largeState.ReceivedBytes );
        var (stalled, _, _) = await Announce( "stall" );
        await Request( stalled, 0, 0 );
        var expired = await helper.Events.ReadAsync( token );
        Equal( ClipboardCaptureStatus.SelectionCancelled, ClipboardCaptureProtocol.ReadStatus( expired ).Status );
        Equal( stalled, expired.Header.SelectionId );

        var (blocked, _, _) = await Announce( "blocked" );
        await Request( blocked, 0, 0 );
        while ( true )
        {
            await server.StandardInput.WriteLineAsync( "count" );
            if ( await server.StandardOutput.ReadLineAsync( token ) == "9" )
            {
                break;
            }

            await Task.Delay( 10, token );
        }

        var acknowledged = await helper.CancelAsync( blocked, token );
        while ( true )
        {
            await server.StandardInput.WriteLineAsync( "finished" );
            if ( await server.StandardOutput.ReadLineAsync( token ) == "8" )
            {
                break;
            }

            await Task.Delay( 10, token );
        }

        // A delayed acknowledgement must revoke and reap the helper within the host deadline.
        if ( !acknowledged )
        {
            True( !Directory.Exists( $"/proc/{helper.ProcessId}" ), "native cancellation failure revokes the helper" );
            return;
        }

        // The 16 MiB source cannot complete into bounded unread queues. Completion proves its pipe was closed.
        while ( true )
        {
            var frame = await helper.Events.ReadAsync( token );
            if ( frame.Header.Type == ClipboardHelperFrameType.Status )
            {
                Equal( blocked, frame.Header.SelectionId );
                break;
            }
        }

        async Task<(Guid Selection, IReadOnlyList<string> Names, uint NextSequence)> Announce( string scenario )
        {
            await server.StandardInput.WriteLineAsync( scenario );
            Equal( "sent", await server.StandardOutput.ReadLineAsync( token ) );
            var first = await helper.Events.ReadAsync( token );
            ClipboardHelperInventory inventory = new( 17, first.Header.SelectionId, ClipboardCaptureProtocol.ReadSelectionStart( first ) );
            ClipboardHelperFrame last = first;
            while ( !inventory.Complete )
            {
                last = await helper.Events.ReadAsync( token );
                inventory.Accept( last );
            }

            return (first.Header.SelectionId, inventory.Names, last.Header.Sequence + 1);
        }

        ValueTask Request( Guid selection, int offer, uint sequence ) => helper.SendAsync(
            new( new( ClipboardHelperFrameType.ReceiveOffer, 17, selection, offer, sequence, 0 ), ReadOnlyMemory<byte>.Empty ), token );
    }
}
