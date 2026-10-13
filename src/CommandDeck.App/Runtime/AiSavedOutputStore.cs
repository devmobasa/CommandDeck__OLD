namespace CommandDeck;

internal sealed class AiSavedOutputStore : IDisposable
{
    private const int MaximumPerClient = 16;
    private const int MaximumGlobally = 256;
    private readonly Dictionary<AiOutputKey, SavedAiOutput> _entries = [];
    private readonly object _gate = new( );
    private long _sequence;

    public void Store(
        string clientOwnerId,
        long generation,
        string originalPath,
        SafeRegularFile identity )
    {
        lock ( _gate )
        {
            AiOutputKey key = new( clientOwnerId, generation );
            if ( _entries.Remove( key, out var previous ) )
            {
                previous.Dispose( );
            }
            while ( _entries.Count( pair => string.Equals(
                       pair.Key.ClientOwnerId, clientOwnerId, StringComparison.Ordinal ) ) >=
                   MaximumPerClient )
            {
                RemoveOldest( pair => string.Equals(
                    pair.Key.ClientOwnerId, clientOwnerId, StringComparison.Ordinal ) );
            }
            while ( _entries.Count >= MaximumGlobally )
            {
                RemoveOldest( static _ => true );
            }
            _entries[key] = new SavedAiOutput(
                originalPath, identity, Interlocked.Increment( ref _sequence ) );
        }
    }

    public bool TryDuplicate(
        string clientOwnerId,
        long generation,
        out SafeRegularFile? file,
        out string? originalPath )
    {
        lock ( _gate )
        {
            if ( _entries.TryGetValue(
                    new AiOutputKey( clientOwnerId, generation ),
                    out var saved ) )
            {
                file = saved.File.Duplicate( );
                originalPath = saved.OriginalPath;
                return true;
            }
        }
        file = null;
        originalPath = null;
        return false;
    }

    public void ClearClient( string clientOwnerId )
    {
        lock ( _gate )
        {
            var keys = _entries.Keys
                .Where( key => string.Equals(
                    key.ClientOwnerId, clientOwnerId, StringComparison.Ordinal ) )
                .ToArray( );
            foreach ( var key in keys )
            {
                _entries.Remove( key, out var saved );
                saved?.Dispose( );
            }
        }
    }

    private void RemoveOldest( Func<KeyValuePair<AiOutputKey, SavedAiOutput>, bool> predicate )
    {
        var oldest = _entries
            .Where( predicate )
            .OrderBy( static pair => pair.Value.Sequence )
            .Cast<KeyValuePair<AiOutputKey, SavedAiOutput>?>( )
            .FirstOrDefault( );
        if ( oldest is { } value && _entries.Remove( value.Key, out var removed ) )
        {
            removed.Dispose( );
        }
    }

    public void Dispose( )
    {
        lock ( _gate )
        {
            foreach ( var saved in _entries.Values )
            {
                saved.Dispose( );
            }
            _entries.Clear( );
        }
    }

    private readonly record struct AiOutputKey( string ClientOwnerId, long Generation );
    private sealed record SavedAiOutput(
        string OriginalPath,
        SafeRegularFile File,
        long Sequence ) : IDisposable
    {
        public void Dispose( ) => File.Dispose( );
    }
}
