using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace CommandDeck;

internal sealed record ClipboardPayloadRecovery( int Orphans, int DamagedLivePayloads );

internal sealed partial class ClipboardPayloadLedger
{
    // Startup only: no writer or read lease from an earlier process can survive here.
    internal ClipboardPayloadRecovery Recover( Action<SqliteConnection, IReadOnlyList<Guid>> removeDamagedEntries )
    {
        lock ( gate )
        {
            if ( _admitted.Count != 0 )
            {
                throw new InvalidOperationException( "Clipboard recovery requires a fresh process with no active captures." );
            }

            var rows = ReadLedgerRows( );
            var inventory = directory.Inventory( );
            var staging = inventory.Where( static file => file.Staging ).Select( static file => file.Id ).ToHashSet( );
            var files = inventory.GroupBy( static file => file.Id )
                .ToDictionary( static group => group.Key, static group => group.Sum( static file => file.Length ) );
            var orphans = files.Where( pair => !rows.ContainsKey( pair.Key ) ).ToArray( );
            var damaged = rows.Values.Where( row => row.State == 1 && (staging.Contains( row.Id ) || !LivePayloadMatches( row )) )
                .Select( static row => row.Id ).ToArray( );
            Transaction( ( ) =>
            {
                if ( damaged.Length > 0 )
                {
                    removeDamagedEntries( connection, Array.AsReadOnly( damaged ) );
                    foreach ( var id in damaged )
                    {
                        MarkGarbage( id );
                    }
                }

                Execute( "UPDATE clipboard_payloads SET state=2 WHERE state=0" );
                foreach ( var (id, actualBytes) in files )
                {
                    if ( rows.ContainsKey( id ) )
                    {
                        Execute( "UPDATE clipboard_payloads SET charge=max(charge,$bytes) WHERE id=$id AND state=2",
                            ("$bytes", actualBytes), ("$id", id.ToString( "N" )) );
                    }
                }

                foreach ( var (id, actualBytes) in orphans )
                {
                    Execute( "INSERT INTO clipboard_payloads(id,selection_id,state,charge,actual) VALUES($id,$selection,2,$bytes,$bytes)",
                        ("$id", id.ToString( "N" )), ("$selection", Guid.NewGuid( ).ToString( "N" )), ("$bytes", actualBytes) );
                }
            } );
            return new( orphans.Length, damaged.Length );
        }
    }

    private bool LivePayloadMatches( LedgerRow row )
    {
        try
        {
            using var lease = directory.OpenRead( row.Id );
            return lease.Stream.Length == row.Actual &&
                Convert.ToHexStringLower( SHA256.HashData( lease.Stream ) ) == row.Digest;
        }
        catch ( IOException )
        {
            return false;
        }
    }

    private Dictionary<Guid, LedgerRow> ReadLedgerRows( )
    {
        if ( Scalar( """
            SELECT count(*) FROM clipboard_payloads WHERE typeof(id)!='text' OR length(id)!=32 OR
            typeof(selection_id)!='text' OR length(selection_id)!=32 OR typeof(state)!='integer' OR state NOT IN (0,1,2) OR
            typeof(charge)!='integer' OR charge<0 OR (actual IS NOT NULL AND (typeof(actual)!='integer' OR actual<0)) OR
            (digest IS NOT NULL AND (typeof(digest)!='text' OR length(digest)!=64))
            """ ) != 0 )
        {
            throw new IOException( "Clipboard payload ledger contains invalid stored values." );
        }

        using var command = connection.CreateCommand( );
        command.CommandText = "SELECT id,selection_id,state,charge,actual,digest FROM clipboard_payloads LIMIT 32769";
        using var reader = command.ExecuteReader( );
        Dictionary<Guid, LedgerRow> rows = [];
        while ( reader.Read( ) )
        {
            var id = ParseLedgerId( reader.GetString( 0 ) );
            _ = ParseLedgerId( reader.GetString( 1 ) );
            var state = reader.GetInt32( 2 );
            var charge = reader.GetInt64( 3 );
            long? actual = reader.IsDBNull( 4 ) ? null : reader.GetInt64( 4 );
            var digest = reader.IsDBNull( 5 ) ? null : reader.GetString( 5 );
            if ( digest is not null && digest.Any( static character => !char.IsAsciiHexDigitLower( character ) ) ||
                state == 0 && (charge > ClipboardOfferPlan.MaximumSelectionBytes || actual > charge || actual.HasValue != (digest is not null)) ||
                state == 1 && (actual is null || actual > ClipboardOfferPlan.MaximumSelectionBytes || charge != actual || digest is null) ||
                !rows.TryAdd( id, new( id, state, actual, digest ) ) || rows.Count > 32768 )
            {
                throw new IOException( "Clipboard payload ledger identity, digest or accounting is invalid." );
            }
        }

        return rows;
    }

    private static Guid ParseLedgerId( string value )
    {
        if ( !Guid.TryParseExact( value, "N", out var id ) || id == Guid.Empty || id.ToString( "N" ) != value )
        {
            throw new IOException( "Clipboard payload ledger contains an invalid identity." );
        }

        return id;
    }

    private sealed record LedgerRow( Guid Id, int State, long? Actual, string? Digest );
}
