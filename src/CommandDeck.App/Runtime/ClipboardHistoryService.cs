namespace CommandDeck;

/// <summary>Connection-owned, bounded interactive history. It is never an assistant tool.</summary>
public sealed partial class ClipboardHistoryService( ClipboardHistoryStore store, TimeProvider? timeProvider = null,
    Func<string, CancellationToken, ValueTask<ActionExecutionResult>>? copyText = null, Func<string>? captureStatus = null,
    ClipboardPayloadProjection? projections = null,
    Func<IReadOnlyList<ClipboardServeOffer>, CancellationToken, ValueTask<ActionExecutionResult>>? serve = null,
    Func<string, string, CancellationToken, ValueTask<ClipboardHandlerResolution>>? resolveHandlers = null )
{
    private readonly object _gate = new( );
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, Owner> _owners = new( StringComparer.Ordinal );
    private readonly Dictionary<string, Session> _sessions = new( StringComparer.Ordinal );

    public void RegisterOwner( string ownerId )
    {
        lock ( _gate )
        {
            if ( !_owners.TryAdd( ownerId, new( ) ) )
            {
                throw new InvalidOperationException( "Clipboard connection is already registered." );
            }
        }
    }

    public void RevokeOwner( string ownerId )
    {
        lock ( _gate )
        {
            if ( _owners.TryGetValue( ownerId, out var owner ) )
            {
                owner.Revoked = true;
                owner.ActiveCopy?.Cancel( );
                owner.PolicyChange = null;
                RemoveSessions( ownerId );
            }
        }
    }

    public void RemoveOwner( string ownerId )
    {
        lock ( _gate )
        {
            RevokeOwner( ownerId );
            _owners.Remove( ownerId );
        }
    }

    public string Open( string ownerId, string frontendInstance, long generation )
    {
        lock ( _gate )
        {
            var owner = BindGeneration( ownerId, frontendInstance, generation, closing: false );
            ExpireSessions( );
            if ( _sessions.Count >= 8 || _sessions.Values.Count( session => session.OwnerId == ownerId ) >= 2 )
            {
                throw new InvalidOperationException( "Clipboard browser capacity reached; close an existing browser." );
            }

            var id = Guid.NewGuid( ).ToString( "N" );
            _sessions.Add( id, new( ownerId, _time.GetTimestamp( ), store.ClearEpoch ) );
            return id;
        }
    }

    // Closing a generation before its first open is intentionally remembered.
    public void CloseGeneration( string ownerId, string frontendInstance, long generation )
    {
        lock ( _gate )
        {
            var owner = BindGeneration( ownerId, frontendInstance, generation, closing: true );
            owner.Closed = true;
            owner.ActiveCopy?.Cancel( );
            RemoveSessions( ownerId );
        }
    }

    public void Close( string ownerId, string sessionId )
    {
        lock ( _gate )
        {
            GetSession( ownerId, sessionId );
            var owner = GetOwner( ownerId );
            if ( owner.CopySessionId == sessionId )
            {
                owner.ActiveCopy?.Cancel( );
            }

            _sessions.Remove( sessionId );
        }
    }

    public ClipboardHistoryPage Query( string ownerId, string sessionId, QueryExpression expression,
        bool pinnedOnly, long queryGeneration, string? cursor = null, int limit = 50, string? kind = null )
    {
        lock ( _gate )
        {
            var session = GetSession( ownerId, sessionId );
            if ( cursor is null )
            {
                if ( queryGeneration <= session.QueryGeneration || queryGeneration > 9007199254740991 )
                {
                    throw new InvalidOperationException( "Clipboard query generation is stale." );
                }

                session.Expression = CloneExpression( expression );
                session.PinnedOnly = pinnedOnly;
                session.Kind = kind;
                var snapshot = store.CreateBrowserSnapshot( session.Expression, pinnedOnly, kind );
                session.Entries = snapshot.Entries;
                session.QueryGeneration = queryGeneration;
                session.StoreRevision = snapshot.StoreRevision;
                session.SnapshotAt = _time.GetTimestamp( );
                session.Offset = 0;
            }
            else if ( cursor != session.Cursor || queryGeneration != session.QueryGeneration ||
                pinnedOnly != session.PinnedOnly || kind != session.Kind || !SameExpression( expression, session.Expression ) )
            {
                throw new InvalidOperationException( "Clipboard cursor does not belong to this query." );
            }

            List<ClipboardEntrySummary> page = [];
            while ( session.Offset < session.Entries.Count && page.Count < Math.Clamp( limit, 1, 100 ) )
            {
                var identity = session.Entries[session.Offset++];
                var document = store.ReadDocument( identity.Id, identity.Incarnation );
                if ( document is not null )
                {
                    page.Add( document.Summary );
                }
            }

            session.Cursor = session.Offset < session.Entries.Count ? Guid.NewGuid( ).ToString( "N" ) : null;
            var revision = store.StoreRevision;
            return new( sessionId, page, session.Cursor, revision, revision != session.StoreRevision );
        }
    }

    public ClipboardEntryPreview Preview( string ownerId, string sessionId, string id, string incarnation,
        long revision, long queryGeneration, int matchIndex )
    {
        lock ( _gate )
        {
            var session = GetSession( ownerId, sessionId );
            if ( session.QueryGeneration != queryGeneration )
            {
                throw new InvalidOperationException( "Clipboard preview belongs to a previous query." );
            }

            var document = Resolve( session, id, incarnation, revision );
            return new( id, incarnation, revision, queryGeneration,
                ClipboardTextPreview.Create( document.Text, session.Expression, matchIndex ), document.Summary.Presentation,
                document.Metadata.OpenDestination, document.Metadata.ExtractedText, document.Metadata.Truncated );
        }
    }

    public void Mutate( string ownerId, string sessionId, string id, string incarnation,
        long revision, string action, IReadOnlyList<string>? keywords = null )
    {
        lock ( _gate )
        {
            Resolve( GetSession( ownerId, sessionId ), id, incarnation, revision );
            if ( action == ClipboardActionIds.Delete )
            {
                CancelCopies( id );
            }

            store.MutateEntry( id, incarnation, revision, action, keywords );
        }
    }

    public void Clear( string ownerId, long expectedStoreRevision )
    {
        lock ( _gate )
        {
            GetOwner( ownerId );
            CancelCopies( null );
            store.Clear( expectedStoreRevision );
            _sessions.Clear( );
        }
    }

    private ClipboardHistoryDocument Resolve( Session session, string id, string incarnation, long revision )
    {
        if ( !session.Entries.Contains( new ClipboardEntryIdentity( id, incarnation ) ) )
        {
            throw new InvalidOperationException( "Clipboard entry does not belong to this browser snapshot." );
        }

        var document = store.ReadDocument( id, incarnation, revision );
        if ( document is null )
        {
            throw new InvalidOperationException( "Clipboard entry changed or expired; refresh before acting." );
        }

        return document;
    }

    private Owner BindGeneration( string ownerId, string instance, long generation, bool closing )
    {
        if ( !Guid.TryParseExact( instance, "N", out _ ) || generation is < 1 or > 9007199254740991 )
        {
            throw new InvalidOperationException( "Clipboard frontend identity or generation is invalid." );
        }

        var owner = GetOwner( ownerId );
        if ( owner.Instance is not null && owner.Instance != instance || generation < owner.Generation ||
            !closing && generation == owner.Generation && owner.Closed )
        {
            throw new InvalidOperationException( "Clipboard launcher generation is closed or stale." );
        }

        owner.Instance ??= instance;
        if ( generation > owner.Generation )
        {
            RemoveSessions( ownerId );
            owner.ActiveCopy?.Cancel( );
            owner.Generation = generation;
            owner.Closed = false;
            owner.EffectsRevoked = false;
        }

        return owner;
    }

    private Owner GetOwner( string ownerId )
    {
        if ( !_owners.TryGetValue( ownerId, out var owner ) || owner.Revoked )
        {
            throw new InvalidOperationException( "Clipboard connection is closed." );
        }

        return owner;
    }

    private Session GetSession( string ownerId, string id )
    {
        GetOwner( ownerId );
        ExpireSessions( );
        if ( !_sessions.TryGetValue( id, out var session ) || session.OwnerId != ownerId || session.ClearEpoch != store.ClearEpoch )
        {
            throw new InvalidOperationException( "Clipboard browser expired; reopen or refresh it." );
        }

        return session;
    }

    private void ExpireSessions( )
    {
        foreach ( var pair in _sessions.Where( pair => pair.Value.ClearEpoch != store.ClearEpoch ||
            _time.GetElapsedTime( pair.Value.SnapshotAt ) >= TimeSpan.FromMinutes( 2 ) ).ToArray( ) )
        {
            _sessions.Remove( pair.Key );
        }
    }

    private void RemoveSessions( string ownerId )
    {
        foreach ( var pair in _sessions.Where( pair => pair.Value.OwnerId == ownerId ).ToArray( ) )
        {
            _sessions.Remove( pair.Key );
        }
    }

    private static QueryExpression CloneExpression( QueryExpression expression ) => new( )
    {
        IncludeFilterIds = [.. expression.IncludeFilterIds],
        ExcludeFilterIds = [.. expression.ExcludeFilterIds],
        Clauses = expression.Clauses.Select( static clause => new QueryClause
        {
            Field = clause.Field,
            Text = clause.Text,
            Excluded = clause.Excluded
        } ).ToList( )
    };

    private static bool SameExpression( QueryExpression left, QueryExpression right ) =>
        left.IncludeFilterIds.SequenceEqual( right.IncludeFilterIds ) && left.ExcludeFilterIds.SequenceEqual( right.ExcludeFilterIds ) &&
        left.Clauses.Select( static clause => (clause.Field, clause.Text, clause.Excluded) )
            .SequenceEqual( right.Clauses.Select( static clause => (clause.Field, clause.Text, clause.Excluded) ) );

    private sealed class Owner
    {
        public string? Instance;
        public long Generation;
        public bool Closed;
        public bool Revoked;
        public bool EffectsRevoked;
        public PreparedPolicy? PolicyChange;
        public CancellationTokenSource? ActiveCopy;
        public string? CopySessionId;
        public string? CopyEntryId;
    }

    private sealed class Session( string ownerId, long timestamp, long clearEpoch )
    {
        public string OwnerId { get; } = ownerId;
        public long ClearEpoch { get; } = clearEpoch;
        public long SnapshotAt = timestamp;
        public long QueryGeneration;
        public long StoreRevision;
        public bool PinnedOnly;
        public string? Kind;
        public QueryExpression Expression = new( );
        public IReadOnlyList<ClipboardEntryIdentity> Entries = [];
        public int Offset;
        public string? Cursor;
        public Capability? Capability;
    }
}
