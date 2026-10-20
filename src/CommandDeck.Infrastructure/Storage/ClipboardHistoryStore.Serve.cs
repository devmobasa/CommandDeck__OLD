namespace CommandDeck;

/// <summary>Held leases for every retained offer of one entry; dispose after serving completes.</summary>
public sealed class ClipboardCopySelection( IReadOnlyList<ClipboardServeOffer> offers ) : IDisposable
{
    public IReadOnlyList<ClipboardServeOffer> Offers { get; } = offers;

    public void Dispose( )
    {
        foreach ( var offer in Offers )
        {
            offer.Lease.Dispose( );
        }
    }
}

public sealed partial class ClipboardHistoryStore
{
    public ClipboardCopySelection ReadCopySelection( string id, string incarnation, long expectedRevision )
    {
        lock ( _gate )
        {
            if ( ReadDocument( id, incarnation, expectedRevision ) is null )
            {
                throw new InvalidOperationException( "Clipboard entry changed or expired; refresh before copying." );
            }

            using var command = _connection.CreateCommand( );
            command.CommandText = """
                SELECT o.mime,p.id,p.actual,p.digest FROM clipboard_offers o JOIN clipboard_payloads p ON p.id=o.payload_id
                WHERE o.entry_id=$id AND p.state=1 ORDER BY o.offer_index
                """;
            command.Parameters.AddWithValue( "$id", id );
            using var reader = command.ExecuteReader( );
            List<ClipboardServeOffer> offers = [];
            try
            {
                while ( reader.Read( ) )
                {
                    if ( reader.IsDBNull( 2 ) || reader.IsDBNull( 3 ) )
                    {
                        throw new IOException( "Clipboard payload is unavailable." );
                    }

                    var length = reader.GetInt64( 2 );
                    var digest = reader.GetString( 3 );
                    var lease = _payloadDirectory!.OpenRead( Guid.ParseExact( reader.GetString( 1 ), "N" ) );
                    if ( lease.Stream.Length != length || length < 1 )
                    {
                        lease.Dispose( );
                        throw new IOException( "Clipboard payload length changed." );
                    }

                    offers.Add( new( reader.GetString( 0 ), lease, length, digest ) );
                }

                if ( offers.Count == 0 )
                {
                    throw new IOException( "Clipboard payload is unavailable." );
                }
            }
            catch ( IOException )
            {
                foreach ( var offer in offers )
                {
                    offer.Lease.Dispose( );
                }

                reader.Close( );
                Delete( id );
                throw new IOException( "Clipboard payload is missing or damaged; the entry was removed." );
            }

            return new( offers );
        }
    }

    /// <summary>Records an explicit recopy without changing the entry revision or snapshot membership.</summary>
    public void TouchCopied( string id, string incarnation )
    {
        lock ( _gate )
        {
            var copied = Transaction( ( ) =>
            {
                var changed = Execute( "UPDATE entries SET copied=$time WHERE id=$id AND incarnation=$incarnation",
                    ("$time", _time.GetUtcNow( ).ToUnixTimeSeconds( )), ("$id", id), ("$incarnation", incarnation) );
                if ( changed > 0 )
                {
                    AdvanceRevision( );
                }

                return changed;
            } );
            if ( copied > 0 )
            {
                QueueSequenceCopy( id );
            }
        }
    }
}
