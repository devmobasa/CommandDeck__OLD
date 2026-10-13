using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace CommandDeck;

// Borrows a helper transport descriptor. Its owner must join all operations before disposing it.
// Nonblocking I/O plus a cancellation eventfd also covers partial frames and full output pipes.
internal sealed unsafe class ClipboardPipeStream : Stream
{
    private readonly int _descriptor;
    private readonly FileAccess _access;
    private readonly WaylandWakeSignal _cancel = new( );
    private readonly BlockingCollection<Action> _operations = new( boundedCapacity: 1 );
    private readonly Thread _ioThread;
    private int _disposed;

    internal ClipboardPipeStream( int descriptor, FileAccess access )
    {
        _descriptor = descriptor;
        _access = access;
        var flags = WaylandNative.SetFlags( descriptor, 3, 0 );
        if ( flags < 0 || WaylandNative.SetFlags( descriptor, 4, flags | 0x800 ) < 0 )
        {
            _operations.Dispose( );
            _cancel.Dispose( );
            throw new IOException( "Clipboard transport could not be made nonblocking." );
        }

        // Indefinite native poll waits must not occupy the shared workers that
        // deliver control acknowledgements. Each owned pipe has one I/O worker.
        _ioThread = new Thread( RunOperations ) { IsBackground = true, Name = "CommandDeck clipboard pipe" };
        try
        {
            _ioThread.Start( );
        }
        catch
        {
            _operations.Dispose( );
            _cancel.Dispose( );
            throw;
        }
    }

    public override bool CanRead => _access == FileAccess.Read;
    public override bool CanWrite => _access == FileAccess.Write;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException( );
    public override long Position
    {
        get => throw new NotSupportedException( );
        set => throw new NotSupportedException( );
    }

    public override ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default ) =>
        new( QueueOperation( ( ) => Read( buffer.Span, cancellationToken ) ) );

    public override ValueTask WriteAsync( ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default ) =>
        new( QueueOperation( ( ) =>
        {
            Write( buffer.Span, cancellationToken );
            return 0;
        } ) );

    private Task<T> QueueOperation<T>( Func<T> operation )
    {
        TaskCompletionSource<T> completion = new( TaskCreationOptions.RunContinuationsAsynchronously );
        if ( !_operations.TryAdd( ( ) =>
        {
            try
            {
                completion.SetResult( operation( ) );
            }
            catch ( OperationCanceledException exception )
            {
                completion.SetCanceled( exception.CancellationToken );
            }
            catch ( Exception exception )
            {
                completion.SetException( exception );
            }
        } ) )
        {
            throw new InvalidOperationException( "Clipboard pipe operations must be awaited before sending another." );
        }

        return completion.Task;
    }

    private void RunOperations( )
    {
        foreach ( var operation in _operations.GetConsumingEnumerable( ) )
        {
            operation( );
        }
    }

    public override int Read( byte[] buffer, int offset, int count ) => Read( buffer.AsSpan( offset, count ), default );
    public override void Write( byte[] buffer, int offset, int count ) => Write( buffer.AsSpan( offset, count ), default );

    private int Read( Span<byte> buffer, CancellationToken token )
    {
        if ( !CanRead )
        {
            throw new NotSupportedException( );
        }

        _cancel.Drain( );
        using var registration = token.Register( _cancel.Signal );
        while ( true )
        {
            token.ThrowIfCancellationRequested( );
            nint read;
            fixed ( byte* bytes = buffer )
            {
                read = WaylandNative.Read( _descriptor, bytes, ( nuint ) buffer.Length );
            }

            if ( read >= 0 )
            {
                return checked(( int ) read);
            }

            RetryOrWait( Marshal.GetLastPInvokeError( ), write: false, token );
        }
    }

    private void Write( ReadOnlySpan<byte> buffer, CancellationToken token )
    {
        if ( !CanWrite )
        {
            throw new NotSupportedException( );
        }

        _cancel.Drain( );
        using var registration = token.Register( _cancel.Signal );
        while ( !buffer.IsEmpty )
        {
            token.ThrowIfCancellationRequested( );
            nint written;
            fixed ( byte* bytes = buffer )
            {
                written = WaylandNative.Write( _descriptor, bytes, ( nuint ) buffer.Length );
            }

            if ( written > 0 )
            {
                buffer = buffer[checked(( int ) written)..];
                continue;
            }

            RetryOrWait( written == 0 ? 5 : Marshal.GetLastPInvokeError( ), write: true, token );
        }
    }

    private void RetryOrWait( int error, bool write, CancellationToken token )
    {
        if ( error == 4 )
        {
            return;
        }

        if ( error != 11 )
        {
            throw new IOException( "Clipboard transport I/O failed." );
        }

        PollDescriptor* descriptors = stackalloc PollDescriptor[2];
        descriptors[0] = new( ) { Fd = _descriptor, Events = ( short ) (write ? 4 : 1) };
        descriptors[1] = new( ) { Fd = _cancel.Descriptor, Events = 1 };
        while ( WaylandNative.Poll( descriptors, 2, -1 ) < 0 )
        {
            token.ThrowIfCancellationRequested( );
            if ( Marshal.GetLastPInvokeError( ) != 4 )
            {
                throw new IOException( "Clipboard transport wait failed." );
            }
        }

        token.ThrowIfCancellationRequested( );
    }

    public override void Flush( ) { }
    public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( );
    public override void SetLength( long value ) => throw new NotSupportedException( );

    protected override void Dispose( bool disposing )
    {
        if ( disposing && Interlocked.Exchange( ref _disposed, 1 ) == 0 )
        {
            _operations.CompleteAdding( );
            _ioThread.Join( );
            _operations.Dispose( );
            _cancel.Dispose( );
        }

        base.Dispose( disposing );
    }
}
