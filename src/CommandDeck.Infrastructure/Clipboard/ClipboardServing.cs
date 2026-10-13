using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

namespace CommandDeck;

public sealed record ClipboardServeOffer( string Mime, ClipboardPayloadLease Lease, long Length, string Digest );

/// <summary>Serves verified retained offers through a dedicated native helper that never arms capture.</summary>
public sealed class ClipboardServing( Func<ProcessStartInfo> start ) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new( 1, 1 );
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ClipboardCaptureStatus>> _waits = new( );
    private ClipboardHostProcess? _helper;
    private Task? _pump;
    private ulong _epoch;
    private bool _disposed;

    public async ValueTask<ActionExecutionResult> ServeAsync( IReadOnlyList<ClipboardServeOffer> offers, CancellationToken token )
    {
        ArgumentNullException.ThrowIfNull( offers );
        var manifest = offers.Select( static offer => new ClipboardServeManifestEntry( offer.Mime, offer.Length ) ).ToArray( );
        await _gate.WaitAsync( token ).ConfigureAwait( false );
        try
        {
            ObjectDisposedException.ThrowIf( _disposed, this );
            var helper = await EnsureHelperAsync( token ).ConfigureAwait( false );
            var writeId = Guid.NewGuid( );
            TaskCompletionSource<ClipboardCaptureStatus> served = new( TaskCreationOptions.RunContinuationsAsynchronously );
            _waits[writeId] = served;
            try
            {
                await helper.SendAsync( ClipboardServeProtocol.WriteStart( _epoch, writeId, manifest ), token ).ConfigureAwait( false );
                uint sequence = 1;
                try
                {
                    for ( var index = 0; index < offers.Count; index++ )
                    {
                        sequence = await StreamOfferAsync( helper, writeId, index, offers[index], sequence, token ).ConfigureAwait( false );
                    }
                }
                catch ( InvalidDataException )
                {
                    // Payload verification failed before ownership changed: discard only this write and keep the current selection.
                    if ( !await CancelWriteAsync( helper, writeId ).ConfigureAwait( false ) )
                    {
                        throw new IOException( "Clipboard serving helper did not acknowledge the discarded write." );
                    }

                    return ActionExecutionResult.Failure( "Clipboard payload verification failed; the clipboard was not changed." );
                }

                await helper.SendAsync( ClipboardServeProtocol.WriteEnd( _epoch, writeId, sequence, offers.Count ), token ).ConfigureAwait( false );
                var status = await served.Task.WaitAsync( TimeSpan.FromSeconds( 2 ), token ).ConfigureAwait( false );
                return status == ClipboardCaptureStatus.Serving ? ActionExecutionResult.Success( "Copied." ) :
                    ActionExecutionResult.Failure( "The clipboard helper rejected the copied selection." );
            }
            catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
            {
                await CleanupCancelledWriteAsync( helper, writeId ).ConfigureAwait( false );
                throw;
            }
            finally
            {
                _waits.TryRemove( writeId, out _ );
            }
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or InvalidDataException or
            TimeoutException or System.ComponentModel.Win32Exception or System.Threading.Channels.ChannelClosedException ||
            exception is OperationCanceledException && !token.IsCancellationRequested )
        {
            await ResetHelperAsync( ).ConfigureAwait( false );
            return ActionExecutionResult.Failure( "Clipboard serving is unavailable; the clipboard may already have changed." );
        }
        finally
        {
            _gate.Release( );
        }
    }

    private static async ValueTask<bool> CancelWriteAsync( ClipboardHostProcess helper, Guid writeId )
    {
        using CancellationTokenSource cleanup = new( TimeSpan.FromSeconds( 1 ) );
        return await helper.CancelAsync( writeId, cleanup.Token ).ConfigureAwait( false );
    }

    private async ValueTask CleanupCancelledWriteAsync( ClipboardHostProcess helper, Guid writeId )
    {
        try
        {
            if ( await CancelWriteAsync( helper, writeId ).ConfigureAwait( false ) )
            {
                return;
            }
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or OperationCanceledException or
            System.Threading.Channels.ChannelClosedException )
        {
        }

        await ResetHelperAsync( ).ConfigureAwait( false );
    }

    private async ValueTask<uint> StreamOfferAsync( ClipboardHostProcess helper, Guid writeId, int index, ClipboardServeOffer offer,
        uint sequence, CancellationToken token )
    {
        if ( offer.Length is < 1 || offer.Lease.Stream.Length != offer.Length )
        {
            throw new InvalidDataException( "Clipboard retained payload length changed." );
        }

        using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
        var buffer = new byte[ClipboardHelperHeader.MaximumPayload];
        long offset = 0;
        while ( offset < offer.Length )
        {
            var count = ( int ) Math.Min( buffer.Length, offer.Length - offset );
            var read = await RandomAccess.ReadAsync( offer.Lease.Stream.SafeFileHandle, buffer.AsMemory( 0, count ), offset, token ).ConfigureAwait( false );
            if ( read == 0 )
            {
                throw new InvalidDataException( "Clipboard retained payload ended early." );
            }

            hash.AppendData( buffer, 0, read );
            await helper.SendAsync( ClipboardServeProtocol.WriteChunk( _epoch, writeId, index, sequence++, buffer.AsMemory( 0, read ).ToArray( ) ), token ).ConfigureAwait( false );
            offset += read;
        }

        if ( !string.Equals( Convert.ToHexStringLower( hash.GetHashAndReset( ) ), offer.Digest, StringComparison.OrdinalIgnoreCase ) )
        {
            throw new InvalidDataException( "Clipboard retained payload digest changed." );
        }

        return sequence;
    }

    private async ValueTask<ClipboardHostProcess> EnsureHelperAsync( CancellationToken token )
    {
        if ( _helper is not null )
        {
            return _helper;
        }

        var epoch = checked(++_epoch);
        var helper = new ClipboardHostProcess( start( ), epoch );
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource( token );
            deadline.CancelAfter( TimeSpan.FromSeconds( 3 ) );
            var ready = await helper.Events.ReadAsync( deadline.Token ).ConfigureAwait( false );
            if ( ClipboardCaptureProtocol.ReadStatus( ready ).Status != ClipboardCaptureStatus.Ready )
            {
                throw new InvalidDataException( "Clipboard serving helper did not become ready." );
            }
        }
        catch
        {
            await helper.DisposeAsync( ).ConfigureAwait( false );
            throw;
        }

        _helper = helper;
        _pump = PumpAsync( helper );
        return helper;
    }

    private async Task PumpAsync( ClipboardHostProcess helper )
    {
        try
        {
            await foreach ( var frame in helper.Events.ReadAllAsync( ).ConfigureAwait( false ) )
            {
                if ( frame.Header.Type != ClipboardHelperFrameType.Status )
                {
                    continue;
                }

                var (status, _) = ClipboardCaptureProtocol.ReadStatus( frame );
                if ( status is ClipboardCaptureStatus.Serving or ClipboardCaptureStatus.WriteRejected &&
                    _waits.TryGetValue( frame.Header.SelectionId, out var waiting ) )
                {
                    waiting.TrySetResult( status );
                }
            }
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidDataException or System.Threading.Channels.ChannelClosedException ) { }
        finally
        {
            foreach ( var waiting in _waits.Values )
            {
                waiting.TrySetException( new IOException( "Clipboard serving helper stopped." ) );
            }
        }
    }

    private async ValueTask ResetHelperAsync( )
    {
        var helper = _helper;
        var pump = _pump;
        _helper = null;
        _pump = null;
        if ( helper is not null )
        {
            await helper.DisposeAsync( ).ConfigureAwait( false );
        }

        if ( pump is not null )
        {
            await pump.ConfigureAwait( false );
        }
    }

    public async ValueTask DisposeAsync( )
    {
        await _gate.WaitAsync( ).ConfigureAwait( false );
        try
        {
            _disposed = true;
            await ResetHelperAsync( ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
            _gate.Dispose( );
        }
    }
}
