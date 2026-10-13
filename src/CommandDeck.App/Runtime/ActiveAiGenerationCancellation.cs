namespace CommandDeck;

internal sealed class ActiveAiGenerationCancellation : IDisposable
{
    private readonly object _sync = new( );
    private CancellationTokenSource? _source;

    public ActiveAiGenerationCancellation( CancellationTokenSource source )
    {
        ArgumentNullException.ThrowIfNull( source );
        _source = source;
        Token = source.Token;
    }

    public CancellationToken Token { get; }

    public void Cancel( )
    {
        lock ( _sync )
        {
            _source?.Cancel( );
        }
    }

    public void Dispose( )
    {
        lock ( _sync )
        {
            var source = _source;
            _source = null;
            source?.Dispose( );
        }
    }
}
