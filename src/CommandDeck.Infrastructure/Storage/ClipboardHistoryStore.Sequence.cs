namespace CommandDeck;

public sealed record ClipboardSequenceEntry( string Id, string Incarnation, long Revision );

public sealed record ClipboardSequenceProgress( int Copied, int Queued );

// A clipboard sequence copies entries one at a time in last-copy order, oldest
// first. It starts at an entry (that entry and every newer one) or, after a
// restart, with the next entry copied; entries copied later join the end. The
// position lives only in daemon memory: Clear, turning consent off, and a daemon
// restart end it, and it is never persisted or exported.
public sealed partial class ClipboardHistoryStore
{
    private ClipboardSequence? _sequence;

    public void RestartSequence( )
    {
        lock ( _gate )
        {
            RequireHistoryConsent( );
            _sequence = new( );
        }
    }

    public int StartSequenceAt( string id, string? incarnation = null )
    {
        lock ( _gate )
        {
            RequireHistoryConsent( );
            var order = SequenceOrder( );
            var start = order.FindIndex( row => row.Identity.Id == id && (incarnation is null || row.Identity.Incarnation == incarnation) );
            if ( start < 0 )
            {
                throw new InvalidOperationException( "Clipboard entry changed or expired; refresh before acting." );
            }

            ClipboardSequence sequence = new( );
            foreach ( var row in order.Skip( start ) )
            {
                sequence.Enqueue( row.Identity );
            }

            _sequence = sequence;
            return sequence.Pending.Count;
        }
    }

    /// <summary>The next entry to copy, or null when nothing newer is queued. The position moves only on <see cref="AdvanceSequence"/>.</summary>
    public ClipboardSequenceEntry? PeekSequence( )
    {
        lock ( _gate )
        {
            RequireHistoryConsent( );
            var sequence = _sequence;
            if ( sequence is null )
            {
                throw new InvalidOperationException(
                    "Start a clipboard sequence first: run Restart clipboard sequence, or use Start sequence in Clipboard History." );
            }

            var order = SequenceOrder( );
            sequence.RemoveExpired( order );
            if ( sequence.Pending.Count == 0 )
            {
                return null;
            }

            var next = order.First( row => row.Identity == sequence.Pending[0] );
            return new( next.Identity.Id, next.Identity.Incarnation, next.Revision );
        }
    }

    public ClipboardSequenceProgress AdvanceSequence( ClipboardSequenceEntry entry )
    {
        ArgumentNullException.ThrowIfNull( entry );
        lock ( _gate )
        {
            var sequence = _sequence;
            if ( sequence is null )
            {
                throw new InvalidOperationException( "The clipboard sequence ended while copying." );
            }

            if ( sequence.Pending.Count > 0 && sequence.Pending[0] == new ClipboardEntryIdentity( entry.Id, entry.Incarnation ) )
            {
                sequence.Pending.RemoveAt( 0 );
                sequence.Copied++;
            }

            return new( sequence.Copied, sequence.Pending.Count );
        }
    }

    // Callers hold _gate. Pausing keeps the sequence; withdrawing consent ends it.
    private void EndSequenceWithoutConsent( )
    {
        if ( ReadPolicy( ).Consent == ClipboardConsent.None )
        {
            _sequence = null;
        }
    }

    private List<SequenceRow> SequenceOrder( )
    {
        Prune( );
        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT id,incarnation,revision FROM entries ORDER BY copied,rowid";
        using var reader = command.ExecuteReader( );
        List<SequenceRow> rows = [];
        while ( reader.Read( ) )
        {
            rows.Add( new( new( reader.GetString( 0 ), reader.GetString( 1 ) ), reader.GetInt64( 2 ) ) );
        }

        return rows;
    }

    private sealed record SequenceRow( ClipboardEntryIdentity Identity, long Revision );

    // Called under _gate after a capture or explicit recopy commits. The entry
    // may have been pruned during publication; only its current incarnation joins.
    private void QueueSequenceCopy( string id )
    {
        if ( _sequence is null )
        {
            return;
        }

        var order = SequenceOrder( );
        _sequence.RemoveExpired( order );
        var entry = order.FirstOrDefault( row => row.Identity.Id == id );
        if ( entry is not null )
        {
            _sequence.Enqueue( entry.Identity );
        }
    }

    private sealed class ClipboardSequence
    {
        // Queued or already copied; copying an entry moves it to the newest
        // position, and this keeps it from joining the queue again.
        private readonly HashSet<ClipboardEntryIdentity> _seen = [];

        public List<ClipboardEntryIdentity> Pending { get; } = [];
        public int Copied { get; set; }

        public void Enqueue( ClipboardEntryIdentity identity )
        {
            if ( _seen.Add( identity ) )
            {
                Pending.Add( identity );
            }
        }

        public void RemoveExpired( IReadOnlyList<SequenceRow> order )
        {
            var live = order.Select( static row => row.Identity ).ToHashSet( );
            _seen.IntersectWith( live );
            Pending.RemoveAll( identity => !live.Contains( identity ) );
        }
    }
}
