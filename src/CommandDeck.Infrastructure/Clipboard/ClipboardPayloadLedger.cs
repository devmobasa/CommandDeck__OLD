using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed record ClipboardCompletedPayload( Guid Id, ClipboardPlannedOffer Offer, long Length, string Digest );

/// <summary>Shares the store connection and gate so entry publication and ledger state commit together.</summary>
internal sealed partial class ClipboardPayloadLedger( SqliteConnection connection, ClipboardPayloadDirectory directory, object gate )
{
    private readonly HashSet<Guid> _admitted = [];

    internal void CreateSchema( )
    {
        Execute( """
            CREATE TABLE clipboard_payloads(
                id TEXT PRIMARY KEY, selection_id TEXT NOT NULL, state INTEGER NOT NULL CHECK(state IN (0,1,2)),
                charge INTEGER NOT NULL CHECK(charge>=0), actual INTEGER, digest TEXT,
                CHECK(actual IS NULL OR actual>=0));
            CREATE INDEX clipboard_payload_selection ON clipboard_payloads(selection_id);
            """ );
    }

    internal long ChargedBytes
    {
        get
        {
            lock ( gate )
            {
                return Scalar( "SELECT coalesce(sum(charge),0) FROM clipboard_payloads" );
            }
        }
    }

    internal ClipboardPayloadReservation Reserve( ClipboardOfferPlan plan, long quotaBytes )
    {
        if ( !plan.Accepted || plan.ReservedBytes <= 0 || quotaBytes is < 64L * 1024 * 1024 or > 1024L * 1024 * 1024 )
        {
            throw new InvalidOperationException( "Clipboard payload reservation has invalid admission or quota." );
        }

        lock ( gate )
        {
            var selection = Guid.NewGuid( );
            var payloads = plan.Offers.Select( offer => (Id: Guid.NewGuid( ), Offer: offer) ).ToArray( );
            Transaction( ( ) =>
            {
                if ( Scalar( "SELECT count(DISTINCT selection_id) FROM clipboard_payloads WHERE state IN (0,2)" ) >= 4 ||
                    Scalar( "SELECT coalesce(sum(charge),0) FROM clipboard_payloads WHERE state IN (0,2)" ) + plan.ReservedBytes > 48L * 1024 * 1024 ||
                    ChargedBytes + plan.ReservedBytes > quotaBytes )
                {
                    throw new IOException( "Clipboard payload capacity is occupied; cleanup or pending capture must finish." );
                }

                foreach ( var payload in payloads )
                {
                    Execute( "INSERT INTO clipboard_payloads(id,selection_id,state,charge) VALUES($id,$selection,0,$charge)",
                        ("$id", payload.Id.ToString( "N" )), ("$selection", selection.ToString( "N" )), ("$charge", payload.Offer.MaximumBytes) );
                }
            } );
            _admitted.Add( selection );
            return new( this, selection, payloads );
        }
    }

    internal ClipboardPayloadWriter OpenWriter( ClipboardPayloadReservation reservation, int inventoryIndex )
    {
        lock ( gate )
        {
            RequireAdmitted( reservation );
            var payload = reservation.Payloads.SingleOrDefault( value => value.Offer.InventoryIndex == inventoryIndex );
            if ( payload.Id == Guid.Empty || !reservation.Started.Add( payload.Id ) )
            {
                throw new InvalidOperationException( "Clipboard offer was not admitted or was already received." );
            }

            try
            {
                return new( this, reservation, payload.Id, payload.Offer, directory.CreateStaging( payload.Id ) );
            }
            catch
            {
                Abandon( reservation );
                throw;
            }
        }
    }

    internal void CompletePayload( ClipboardPayloadReservation reservation, ClipboardCompletedPayload payload )
    {
        lock ( gate )
        {
            RequireAdmitted( reservation );
            directory.Publish( payload.Id );
            Execute( "UPDATE clipboard_payloads SET actual=$actual,digest=$digest WHERE id=$id AND state=0",
                ("$actual", payload.Length), ("$digest", payload.Digest), ("$id", payload.Id.ToString( "N" )) );
            reservation.Completed.Add( payload );
        }
    }

    internal void Publish( ClipboardPayloadReservation reservation, IReadOnlySet<Guid> retainedIds, Func<bool> canPublish,
        Action<SqliteConnection, IReadOnlyList<ClipboardCompletedPayload>> publishEntry )
    {
        lock ( gate )
        {
            RequireAdmitted( reservation );
            if ( reservation.Completed.Count != reservation.Payloads.Count )
            {
                throw new InvalidOperationException( "Clipboard selection still has incomplete offers." );
            }

            var retained = reservation.Completed.Where( payload => retainedIds.Contains( payload.Id ) ).ToArray( );
            if ( retained.Length == 0 || retained.Length != retainedIds.Count || retained.Any( static payload => payload.Offer.HeaderOnly ) )
            {
                throw new InvalidOperationException( "Clipboard retained offers are missing or contain validation-only control bytes." );
            }

            Transaction( ( ) =>
            {
                if ( !canPublish( ) )
                {
                    throw new InvalidOperationException( "Clipboard capture authority changed before publication." );
                }

                Execute( "UPDATE clipboard_payloads SET state=2 WHERE selection_id=$selection AND state=0",
                    ("$selection", reservation.Id.ToString( "N" )) );
                foreach ( var payload in retained )
                {
                    Execute( "UPDATE clipboard_payloads SET state=1,charge=actual WHERE id=$id", ("$id", payload.Id.ToString( "N" )) );
                }

                publishEntry( connection, Array.AsReadOnly( retained ) );
            } );
            _admitted.Remove( reservation.Id );
        }
    }

    internal void Abandon( ClipboardPayloadReservation reservation )
    {
        lock ( gate )
        {
            if ( reservation.Owner != this || !_admitted.Remove( reservation.Id ) )
            {
                return;
            }

            Execute( "UPDATE clipboard_payloads SET state=2 WHERE selection_id=$selection AND state=0",
                ("$selection", reservation.Id.ToString( "N" )) );
        }
    }

    // Called inside the same transaction that removes entry/offers/FTS rows.
    internal void MarkGarbage( Guid id ) => Execute( "UPDATE clipboard_payloads SET state=2 WHERE id=$id",
        ("$id", id.ToString( "N" )) );

    internal int Cleanup( )
    {
        lock ( gate )
        {
            using var command = connection.CreateCommand( );
            command.CommandText = "SELECT id FROM clipboard_payloads WHERE state=2 ORDER BY id LIMIT 32768";
            using var reader = command.ExecuteReader( );
            List<Guid> garbage = [];
            while ( reader.Read( ) )
            {
                garbage.Add( Guid.ParseExact( reader.GetString( 0 ), "N" ) );
            }

            reader.Close( );
            var removed = 0;
            foreach ( var id in garbage )
            {
                try
                {
                    directory.Delete( id, true );
                    directory.Delete( id, false );
                }
                catch ( IOException )
                {
                    continue;
                }

                Execute( "DELETE FROM clipboard_payloads WHERE id=$id AND state=2", ("$id", id.ToString( "N" )) );
                removed++;
            }

            return removed;
        }
    }

    private void RequireAdmitted( ClipboardPayloadReservation reservation )
    {
        if ( reservation.Owner != this || !_admitted.Contains( reservation.Id ) )
        {
            throw new InvalidOperationException( "Clipboard payload reservation is no longer admitted." );
        }
    }

    private void Execute( string sql, params (string Name, object Value)[] parameters )
    {
        using var command = connection.CreateCommand( );
        command.CommandText = sql;
        foreach ( var (name, value) in parameters )
        {
            command.Parameters.AddWithValue( name, value );
        }

        command.ExecuteNonQuery( );
    }

    private long Scalar( string sql )
    {
        using var command = connection.CreateCommand( );
        command.CommandText = sql;
        return Convert.ToInt64( command.ExecuteScalar( ), System.Globalization.CultureInfo.InvariantCulture );
    }

    private void Transaction( Action mutation )
    {
        Execute( "BEGIN IMMEDIATE" );
        try
        {
            mutation( );
            Execute( "COMMIT" );
        }
        catch
        {
            Execute( "ROLLBACK" );
            throw;
        }
    }
}

internal sealed class ClipboardPayloadReservation(
    ClipboardPayloadLedger owner, Guid id, (Guid Id, ClipboardPlannedOffer Offer)[] payloads ) : IDisposable
{
    internal ClipboardPayloadLedger Owner { get; } = owner;
    internal Guid Id { get; } = id;
    internal IReadOnlyList<(Guid Id, ClipboardPlannedOffer Offer)> Payloads { get; } = Array.AsReadOnly( payloads );
    internal HashSet<Guid> Started { get; } = [];
    internal List<ClipboardCompletedPayload> Completed { get; } = [];
    public void Dispose( ) => Owner.Abandon( this );
}
