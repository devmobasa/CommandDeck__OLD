namespace CommandDeck;

internal sealed class AiOutputCache
{
    private readonly int _maximumEntries;
    private readonly Dictionary<long, AiOutputRecord> _outputs = [];
    private readonly object _lock = new( );

    public AiOutputCache( int maximumEntries )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( maximumEntries );
        _maximumEntries = maximumEntries;
    }

    public int Count
    {
        get
        {
            lock ( _lock )
            {
                return _outputs.Count;
            }
        }
    }

    public void Store( long generation, string clientOwnerId, string text )
    {
        lock ( _lock )
        {
            if ( !_outputs.ContainsKey( generation ) )
            {
                while ( _outputs.Count >= _maximumEntries )
                {
                    _outputs.Remove( _outputs.Keys.Min( ) );
                }
            }
            _outputs[generation] = new AiOutputRecord( clientOwnerId, text );
        }
    }

    public bool TryGet( string clientOwnerId, long generation, out string? text )
    {
        lock ( _lock )
        {
            if ( _outputs.TryGetValue( generation, out var record ) &&
                string.Equals( record.ClientOwnerId, clientOwnerId, StringComparison.Ordinal ) )
            {
                text = record.Text;
                return true;
            }
        }
        text = null;
        return false;
    }

    public void ClearClient( string clientOwnerId )
    {
        lock ( _lock )
        {
            var generations = _outputs
                .Where( pair => string.Equals(
                    pair.Value.ClientOwnerId, clientOwnerId, StringComparison.Ordinal ) )
                .Select( static pair => pair.Key )
                .ToArray( );
            foreach ( var generation in generations )
            {
                _outputs.Remove( generation );
            }
        }
    }

    public void Clear( )
    {
        lock ( _lock )
        {
            _outputs.Clear( );
        }
    }

    private sealed record AiOutputRecord( string ClientOwnerId, string Text );
}
