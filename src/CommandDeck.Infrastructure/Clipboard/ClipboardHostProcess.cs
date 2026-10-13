using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;

namespace CommandDeck;

/// <summary>Owns one helper process and distinct bounded data/control transports.</summary>
internal sealed class ClipboardHostProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ulong _epoch;
    private readonly AnonymousPipeServerStream _control;
    private readonly AnonymousPipeServerStream _acknowledgements;
    private readonly object _cancelGate = new( );
    private readonly CancellationTokenSource _shutdown = new( );
    private readonly Channel<ClipboardHelperFrame> _outgoing = ClipboardHelperFraming.CreateQueue( );
    private readonly Channel<ClipboardHelperFrame> _controls = ClipboardHelperFraming.CreateQueue( );
    private readonly Channel<ClipboardHelperFrame> _incoming = ClipboardHelperFraming.CreateQueue( );
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _cancellations = new( );
    private readonly Task[] _tasks;
    private int _disposed;

    internal ChannelReader<ClipboardHelperFrame> Events => _incoming.Reader;
    internal int ProcessId => _process.Id;

    internal ClipboardHostProcess( ProcessStartInfo start, ulong epoch )
    {
        if ( !OperatingSystem.IsLinux( ) || epoch == 0 )
        {
            throw new InvalidOperationException( "Clipboard helper requires Linux and a nonzero host epoch." );
        }

        _epoch = epoch;
        _control = new( PipeDirection.Out );
        _acknowledgements = new( PipeDirection.In );
        start.UseShellExecute = false;
        start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
        start.KillOnParentExit = true;
        start.InheritedHandles = [_control.ClientSafePipeHandle, _acknowledgements.ClientSafePipeHandle];
        AddArgument( start, "--control-read", _control.GetClientHandleAsString( ) );
        AddArgument( start, "--control-write", _acknowledgements.GetClientHandleAsString( ) );
        AddArgument( start, "--host-epoch", epoch.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
        _process = new( )
        {
            StartInfo = start
        };
        try
        {
            if ( !_process.Start( ) )
            {
                throw new IOException( "Clipboard helper could not be started." );
            }

            _control.DisposeLocalCopyOfClientHandle( );
            _acknowledgements.DisposeLocalCopyOfClientHandle( );
            _tasks =
            [
                GuardAsync( ( ) => WriteLoopAsync( _process.StandardInput.BaseStream, _outgoing.Reader ) ),
                GuardAsync( ( ) => WriteLoopAsync( _control, _controls.Reader ) ),
                GuardAsync( ReadEventsAsync ), GuardAsync( ReadAcknowledgementsAsync ),
                GuardAsync( DrainDiagnosticsAsync ), ObserveExitAsync( )
            ];
        }
        catch
        {
            try
            {
                Stop( );
                _process.WaitForExit( );
            }
            catch ( InvalidOperationException ) { }

            _control.Dispose( );
            _acknowledgements.Dispose( );
            _process.Dispose( );
            _shutdown.Dispose( );
            throw;
        }
    }

    // The caller transfers an immutable frame and awaits backpressure before sending another.
    internal ValueTask SendAsync( ClipboardHelperFrame frame, CancellationToken cancellationToken )
    {
        ValidateEpoch( frame.Header );
        Span<byte> header = stackalloc byte[ClipboardHelperHeader.Size];
        frame.Header.Encode( header );
        if ( frame.Payload.Length != frame.Header.PayloadLength )
        {
            throw new InvalidDataException( "Clipboard helper payload length does not match its header." );
        }

        if ( frame.Header.Type is ClipboardHelperFrameType.Cancel or ClipboardHelperFrameType.Shutdown )
        {
            throw new InvalidOperationException( "Clipboard cancellation requires the independent control pipe." );
        }

        return _outgoing.Writer.WriteAsync( frame, cancellationToken );
    }

    internal async Task<bool> CancelAsync( Guid selectionId, CancellationToken cancellationToken )
    {
        TaskCompletionSource<bool> acknowledged = new( TaskCreationOptions.RunContinuationsAsynchronously );
        lock ( _cancelGate )
        {
            if ( selectionId == Guid.Empty || _cancellations.Count >= 4 || !_cancellations.TryAdd( selectionId, acknowledged ) )
            {
                throw new InvalidOperationException( "Clipboard cancellation identity or capacity is invalid." );
            }
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, _shutdown.Token );
        deadline.CancelAfter( TimeSpan.FromMilliseconds( 250 ) );
        try
        {
            ClipboardHelperFrame cancel = new( new( ClipboardHelperFrameType.Cancel, _epoch, selectionId, -1, 0, 0 ), ReadOnlyMemory<byte>.Empty );
            await _controls.Writer.WriteAsync( cancel, deadline.Token ).ConfigureAwait( false );
            return await acknowledged.Task.WaitAsync( deadline.Token ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException )
        {
            Stop( );
            await _process.WaitForExitAsync( CancellationToken.None ).ConfigureAwait( false );
            cancellationToken.ThrowIfCancellationRequested( );
            return false;
        }
        finally
        {
            _cancellations.TryRemove( selectionId, out _ );
        }
    }

    private async Task WriteLoopAsync( Stream pipe, ChannelReader<ClipboardHelperFrame> reader )
    {
        await foreach ( var frame in reader.ReadAllAsync( _shutdown.Token ).ConfigureAwait( false ) )
        {
            await ClipboardHelperFraming.WriteAsync( pipe, frame, _shutdown.Token ).ConfigureAwait( false );
        }
    }

    private async Task ReadEventsAsync( )
    {
        while ( await _incoming.Writer.WaitToWriteAsync( _shutdown.Token ).ConfigureAwait( false ) )
        {
            var frame = await ClipboardHelperFraming.ReadAsync( _process.StandardOutput.BaseStream, _shutdown.Token, ValidateEpoch ).ConfigureAwait( false );
            if ( frame is null )
            {
                break;
            }

            if ( !_incoming.Writer.TryWrite( frame ) )
            {
                throw new IOException( "Clipboard helper event queue lost its reserved capacity." );
            }
        }

        Stop( );
    }

    private async Task ReadAcknowledgementsAsync( )
    {
        while ( await ClipboardHelperFraming.ReadAsync( _acknowledgements, _shutdown.Token, ValidateEpoch ).ConfigureAwait( false ) is { } frame )
        {
            if ( frame.Header.Type != ClipboardHelperFrameType.Ack || frame.Header.OfferIndex != -1 ||
                frame.Header.Sequence != 0 || frame.Payload.Length != 0 ||
                !_cancellations.TryGetValue( frame.Header.SelectionId, out var pending ) || !pending.TrySetResult( true ) )
            {
                throw new InvalidDataException( "Clipboard helper sent an unsolicited or invalid cancellation acknowledgement." );
            }
        }

        Stop( );
    }

    private async Task DrainDiagnosticsAsync( )
    {
        var buffer = new byte[4096];
        var total = 0;
        int read;
        while ( (read = await _process.StandardError.BaseStream.ReadAsync( buffer, _shutdown.Token ).ConfigureAwait( false )) != 0 )
        {
            total += read;
            if ( total > 65536 )
            {
                throw new IOException( "Clipboard helper exceeded its diagnostic output limit." );
            }
        }
    }

    private async Task GuardAsync( Func<Task> work )
    {
        try
        {
            await work( ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException )
        {
            _incoming.Writer.TryComplete( new IOException( "Clipboard helper transport stopped." ) );
            Stop( );
        }
    }

    private async Task ObserveExitAsync( )
    {
        await _process.WaitForExitAsync( ).ConfigureAwait( false );
        Stop( );
        _incoming.Writer.TryComplete( );
    }

    private void ValidateEpoch( ClipboardHelperHeader header )
    {
        if ( header.HostEpoch != _epoch )
        {
            throw new InvalidDataException( "Clipboard helper belongs to a different host epoch." );
        }
    }

    private void Stop( )
    {
        _shutdown.Cancel( );
        _outgoing.Writer.TryComplete( );
        _controls.Writer.TryComplete( );
        try
        {
            if ( !_process.HasExited )
            {
                _process.Kill( entireProcessTree: true );
            }
        }
        catch ( InvalidOperationException ) { }
    }

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }

        Stop( );
        await _process.WaitForExitAsync( ).ConfigureAwait( false );
        _control.Dispose( );
        _acknowledgements.Dispose( );
        await Task.WhenAll( _tasks ).ConfigureAwait( false );
        _process.Dispose( );
        _shutdown.Dispose( );
    }

    private static void AddArgument( ProcessStartInfo start, string name, string value )
    {
        start.ArgumentList.Add( name );
        start.ArgumentList.Add( value );
    }
}
