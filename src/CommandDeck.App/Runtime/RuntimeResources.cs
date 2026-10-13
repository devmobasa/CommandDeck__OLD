namespace CommandDeck;

// Construction registers ownership immediately. The same owner is transferred
// to the completed runtime, so rollback and normal shutdown cannot drift apart.
internal sealed class RuntimeResources : IAsyncDisposable
{
    private readonly List<Func<ValueTask>> _cleanup = [];
    private int _disposed;

    public T Own<T>( T resource ) where T : IAsyncDisposable
    {
        Add( resource.DisposeAsync );
        return resource;
    }

    public T OwnDisposable<T>( T resource ) where T : IDisposable
    {
        Add( ( ) => { resource.Dispose( ); return ValueTask.CompletedTask; } );
        return resource;
    }

    public void Add( Func<ValueTask> cleanup ) => _cleanup.Add( cleanup );

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }

        List<Exception> failures = [];
        for ( var index = _cleanup.Count - 1; index >= 0; index-- )
        {
            try
            {
                await _cleanup[index]( ).ConfigureAwait( false );
            }
            catch ( Exception exception )
            {
                failures.Add( exception );
            }
        }
        _cleanup.Clear( );

        if ( failures.Count > 0 )
        {
            throw new AggregateException( "Runtime resource cleanup failed.", failures );
        }
    }
}
