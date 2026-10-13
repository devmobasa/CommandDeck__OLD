using System.Threading.Channels;

namespace CommandDeck.Extensions.Testing;

internal sealed class MemoryPipe : Stream
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>( new( ) { SingleReader = true, SingleWriter = false } );
    private byte[]? _chunk;
    private int _offset;
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException( );
    public override long Position { get => throw new NotSupportedException( ); set => throw new NotSupportedException( ); }
    public override async ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
    {
        if ( buffer.Length == 0 )
        {
            return 0;
        }
        while ( _chunk is null || _offset == _chunk.Length )
        {
            if ( !await _channel.Reader.WaitToReadAsync( cancellationToken ).ConfigureAwait( false ) )
            {
                return 0;
            }
            if ( !_channel.Reader.TryRead( out _chunk ) )
            {
                continue;
            }
            _offset = 0;
        }
        var count = Math.Min( buffer.Length, _chunk.Length - _offset );
        _chunk.AsMemory( _offset, count ).CopyTo( buffer );
        _offset += count;
        return count;
    }
    public override ValueTask WriteAsync( ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default ) =>
        _channel.Writer.WriteAsync( buffer.ToArray( ), cancellationToken );
    public override int Read( byte[] buffer, int offset, int count ) => ReadAsync( buffer.AsMemory( offset, count ) ).AsTask( ).GetAwaiter( ).GetResult( );
    public override void Write( byte[] buffer, int offset, int count ) => WriteAsync( buffer.AsMemory( offset, count ) ).AsTask( ).GetAwaiter( ).GetResult( );
    public override void Flush( ) { }
    public override Task FlushAsync( CancellationToken cancellationToken ) => Task.CompletedTask;
    public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( );
    public override void SetLength( long value ) => throw new NotSupportedException( );
    protected override void Dispose( bool disposing )
    {
        if ( disposing )
        {
            _channel.Writer.TryComplete( );
        }
        base.Dispose( disposing );
    }
}
