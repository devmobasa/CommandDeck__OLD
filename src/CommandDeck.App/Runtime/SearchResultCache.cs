using System.Security.Cryptography;

namespace CommandDeck;

// Stable IDs select only the latest result for non-effect workflows. Invocation
// references additionally bind the exact action snapshot and its search owner.
internal sealed class SearchResultCache
{
    private readonly object _gate = new( );
    private readonly Dictionary<string, LinkedListNode<CachedSearchResult>> _items = new( StringComparer.Ordinal );
    private readonly Dictionary<string, LinkedListNode<CachedSearchResult>> _references = new( StringComparer.Ordinal );
    private readonly LinkedList<CachedSearchResult> _order = new( );
    private readonly TimeProvider _clock;
    private readonly int _capacity;
    private readonly TimeSpan _ttl;

    public SearchResultCache( TimeProvider? clock = null, int capacity = 4096, TimeSpan? ttl = null )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( capacity );
        _clock = clock ?? TimeProvider.System;
        _capacity = capacity;
        _ttl = ttl ?? TimeSpan.FromMinutes( 2 );
        if ( _ttl <= TimeSpan.Zero )
        {
            throw new ArgumentOutOfRangeException( nameof( ttl ) );
        }
    }

    internal int Count
    {
        get
        {
            lock ( _gate )
            {
                return _order.Count;
            }
        }
    }

    public IReadOnlyList<SearchResult> Store(
        IReadOnlyList<SearchResult> results,
        string query,
        bool privateQuery = false,
        string? owner = null )
    {
        List<SearchResult> presented = new( results.Count );
        lock ( _gate )
        {
            var now = _clock.GetTimestamp( );
            while ( _order.First is { } first && Expired( first.Value, now ) )
            {
                Remove( first );
            }

            foreach ( var result in results )
            {
                var key = CacheKey( owner, result.Id );
                if ( _items.TryGetValue( key, out var previous ) )
                {
                    Remove( previous );
                }
                while ( _order.Count >= _capacity )
                {
                    Remove( _order.First! );
                }

                var reference = Convert.ToHexString( RandomNumberGenerator.GetBytes( 24 ) ).ToLowerInvariant( );
                var snapshot = Snapshot( result, reference );
                var entry = new CachedSearchResult( snapshot, query, now, privateQuery, owner, reference );
                var node = _order.AddLast( entry );
                _items.Add( key, node );
                _references.Add( reference, node );
                presented.Add( new SearchResult( result ) { InvocationReference = reference } );
            }
        }

        return presented;
    }

    // Undo keeps the action that actually ran, without replacing the latest
    // searchable row or making the detached snapshot available to attachments.
    public string StoreDetached( SearchResult result, string query, bool privateQuery, string? owner )
    {
        lock ( _gate )
        {
            var now = _clock.GetTimestamp( );
            while ( _order.First is { } first && Expired( first.Value, now ) )
            {
                Remove( first );
            }
            while ( _order.Count >= _capacity )
            {
                Remove( _order.First! );
            }

            var reference = Convert.ToHexString( RandomNumberGenerator.GetBytes( 24 ) ).ToLowerInvariant( );
            var entry = new CachedSearchResult( Snapshot( result, reference ), query, now, privateQuery, owner, reference, Detached: true );
            _references.Add( reference, _order.AddLast( entry ) );
            return reference;
        }
    }

    public bool TryGetInvocation( string reference, string? owner, string resultId, out CachedSearchResult? result )
    {
        lock ( _gate )
        {
            if ( _references.TryGetValue( reference, out var node ) )
            {
                if ( Expired( node.Value, _clock.GetTimestamp( ) ) )
                {
                    Remove( node );
                }
                else if ( string.Equals( node.Value.Owner, owner, StringComparison.Ordinal ) &&
                          string.Equals( node.Value.Result.Id, resultId, StringComparison.Ordinal ) )
                {
                    result = node.Value;
                    return true;
                }
            }

            result = null;
            return false;
        }
    }

    public bool TryGetOwned( string id, string owner, out CachedSearchResult? result )
    {
        lock ( _gate )
        {
            if ( _items.TryGetValue( CacheKey( owner, id ), out var node ) )
            {
                if ( !Expired( node.Value, _clock.GetTimestamp( ) ) )
                {
                    result = node.Value;
                    return true;
                }

                Remove( node );
            }

            result = null;
            return false;
        }
    }

    public bool TryGet( string id, out CachedSearchResult? result )
    {
        lock ( _gate )
        {
            var node = _order.Last;
            while ( node is not null )
            {
                var previous = node.Previous;
                if ( Expired( node.Value, _clock.GetTimestamp( ) ) )
                {
                    Remove( node );
                }
                else if ( !node.Value.Detached && string.Equals( node.Value.Result.Id, id, StringComparison.Ordinal ) )
                {
                    result = node.Value;
                    return true;
                }

                node = previous;
            }

            result = null;
            return false;
        }
    }

    public void RemoveOwner( string owner )
    {
        lock ( _gate )
        {
            var node = _order.First;
            while ( node is not null )
            {
                var next = node.Next;
                if ( string.Equals( node.Value.Owner, owner, StringComparison.Ordinal ) )
                {
                    Remove( node );
                }

                node = next;
            }
        }
    }

    private bool Expired( CachedSearchResult entry, long now ) => _clock.GetElapsedTime( entry.StoredAt, now ) >= _ttl;

    private static string CacheKey( string? owner, string id ) => (owner ?? string.Empty) + "\0" + id;

    private void Remove( LinkedListNode<CachedSearchResult> node )
    {
        var key = CacheKey( node.Value.Owner, node.Value.Result.Id );
        if ( _items.TryGetValue( key, out var current ) && ReferenceEquals( current, node ) )
        {
            _items.Remove( key );
        }
        _references.Remove( node.Value.Reference );
        _order.Remove( node );
    }

    private static SearchResult Snapshot( SearchResult result, string reference ) => new( result )
    {
        InvocationReference = reference,
        Aliases = [.. result.Aliases],
        Details = [.. result.Details],
        MatchedRanges = [.. result.MatchedRanges],
        Metadata = new Dictionary<string, string>( result.Metadata, StringComparer.Ordinal ),
        Actions = [.. result.Actions.Select( static action => new CommandAction
        {
            Id = action.Id,
            Title = action.Title,
            Kind = action.Kind,
            RequiresConfirmation = action.RequiresConfirmation,
            IsDestructive = action.IsDestructive,
            Hidden = action.Hidden,
            Arguments = new Dictionary<string, string>( action.Arguments, StringComparer.Ordinal ),
            Inputs = [.. action.Inputs.Select( static input => new ActionInput
            {
                Name = input.Name,
                DefaultValue = input.DefaultValue
            } )]
        } )]
    };
}

internal sealed record CachedSearchResult(
    SearchResult Result,
    string Query,
    long StoredAt,
    bool PrivateQuery,
    string? Owner,
    string Reference,
    bool Detached = false );
