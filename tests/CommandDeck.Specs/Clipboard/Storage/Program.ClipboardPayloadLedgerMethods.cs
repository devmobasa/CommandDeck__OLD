using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardPayloadLedgerPublishesAtomicallyAsync( )
    {
        using PayloadLedgerFixture fixture = new( );
        var plan = ClipboardOfferPlan.Create( ["text/plain", "x-special/gnome-copied-files"], ClipboardConsent.Text );
        using var reservation = fixture.Ledger.Reserve( plan, 64L * 1024 * 1024 );
        Equal( 32776L, fixture.Ledger.ChargedBytes );
        Equal( 0, fixture.Directory.Inventory( ).Count );
        ClipboardCompletedPayload text;
        using ( var header = fixture.Ledger.OpenWriter( reservation, 1 ) )
        {
            await header.WriteAsync( "copy\n"u8.ToArray( ), default );
            header.Complete( );
        }

        using ( var writer = fixture.Ledger.OpenWriter( reservation, 0 ) )
        {
            await writer.WriteAsync( "exact café\r\n"u8.ToArray( ), default );
            text = writer.Complete( );
        }

        Equal( Convert.ToHexStringLower( SHA256.HashData( "exact café\r\n"u8 ) ), text.Digest );
        Equal( 32776L, fixture.Ledger.ChargedBytes );
        Throws<InvalidOperationException>( ( ) => fixture.Ledger.Publish( reservation,
            reservation.Completed.Select( static payload => payload.Id ).ToHashSet( ), ( ) => true, ( _, _ ) => { } ) );
        Throws<InvalidOperationException>( ( ) => fixture.Ledger.Publish( reservation, new HashSet<Guid> { text.Id }, ( ) => false, ( _, _ ) => { } ) );
        Throws<IOException>( ( ) => fixture.Ledger.Publish( reservation, new HashSet<Guid> { text.Id }, ( ) => true, ( connection, _ ) =>
        {
            using var command = connection.CreateCommand( );
            command.CommandText = "INSERT INTO publication_probe VALUES(1)";
            command.ExecuteNonQuery( );
            throw new IOException( "synthetic publication failure" );
        } ) );
        Equal( 0L, fixture.Scalar( "SELECT count(*) FROM publication_probe" ) );
        Equal( 2L, fixture.Scalar( "SELECT count(*) FROM clipboard_payloads WHERE state=0" ) );

        fixture.Ledger.Publish( reservation, new HashSet<Guid> { text.Id }, ( ) => true, ( connection, retained ) =>
        {
            Equal( 1, retained.Count );
            Equal( text, retained[0] );
            using var command = connection.CreateCommand( );
            command.CommandText = "INSERT INTO publication_probe VALUES(1)";
            command.ExecuteNonQuery( );
        } );
        Equal( 1L, fixture.Scalar( "SELECT count(*) FROM publication_probe" ) );
        Equal( text.Length + 8, fixture.Ledger.ChargedBytes );
        Equal( 1, fixture.Ledger.Cleanup( ) );
        Equal( text.Length, fixture.Ledger.ChargedBytes );
        Throws<InvalidOperationException>( ( ) => fixture.Ledger.OpenWriter( reservation, 0 ) );

        fixture.Ledger.MarkGarbage( text.Id );
        using ( var reader = fixture.Directory.OpenRead( text.Id ) )
        {
            Equal( 0, fixture.Ledger.Cleanup( ) );
            Equal( text.Length, fixture.Ledger.ChargedBytes );
        }

        Equal( 1, fixture.Ledger.Cleanup( ) );
        Equal( 0L, fixture.Ledger.ChargedBytes );
    }

    private static async Task ClipboardPayloadLedgerBoundsAndRetainsCleanupChargesAsync( )
    {
        using PayloadLedgerFixture fixture = new( );
        var plain = ClipboardOfferPlan.Create( ["text/plain"], ClipboardConsent.Text );
        var reservations = Enumerable.Range( 0, 4 ).Select( _ => fixture.Ledger.Reserve( plain, 64L * 1024 * 1024 ) ).ToArray( );
        Throws<IOException>( ( ) => fixture.Ledger.Reserve( plain, 64L * 1024 * 1024 ) );
        foreach ( var reservation in reservations )
        {
            reservation.Dispose( );
        }

        Equal( 4 * 32768L, fixture.Ledger.ChargedBytes );
        Throws<IOException>( ( ) => fixture.Ledger.Reserve( plain, 64L * 1024 * 1024 ) );
        Equal( 4, fixture.Ledger.Cleanup( ) );
        var png = ClipboardOfferPlan.Create( ["image/png"], ClipboardConsent.Rich );
        var large = Enumerable.Range( 0, 3 ).Select( _ => fixture.Ledger.Reserve( png, 64L * 1024 * 1024 ) ).ToArray( );
        Equal( 48L * 1024 * 1024, fixture.Ledger.ChargedBytes );
        Throws<IOException>( ( ) => fixture.Ledger.Reserve( plain, 64L * 1024 * 1024 ) );
        foreach ( var reservation in large )
        {
            reservation.Dispose( );
        }

        fixture.Ledger.Cleanup( );
        using var active = fixture.Ledger.Reserve( plain, 64L * 1024 * 1024 );
        using ( var writer = fixture.Ledger.OpenWriter( active, 0 ) )
        {
            await writer.WriteAsync( new byte[32768], default );
            active.Dispose( );
            Equal( 0, fixture.Ledger.Cleanup( ) );
            Equal( 32768L, fixture.Ledger.ChargedBytes );
            Throws<InvalidOperationException>( ( ) => writer.Complete( ) );
        }

        Equal( 1, fixture.Ledger.Cleanup( ) );
        using var oversized = fixture.Ledger.Reserve( plain, 64L * 1024 * 1024 );
        using ( var writer = fixture.Ledger.OpenWriter( oversized, 0 ) )
        {
            await ThrowsAsync<InvalidDataException>( async ( ) => await writer.WriteAsync( new byte[32769], default ) );
        }

        Equal( 32768L, fixture.Ledger.ChargedBytes );
        Equal( 1, fixture.Ledger.Cleanup( ) );
        Equal( 0L, fixture.Ledger.ChargedBytes );
    }

    private sealed class PayloadLedgerFixture : IDisposable
    {
        internal string Root { get; } = SpecTemporaryFiles.CreateDirectory( "commanddeck-payload-ledger-" ).FullName;
        internal SqliteConnection Connection { get; }
        internal ClipboardPayloadDirectory Directory { get; }
        internal ClipboardPayloadLedger Ledger { get; }

        internal PayloadLedgerFixture( )
        {
            var database = Path.Combine( Root, "history.db" );
            using ( var created = SafeRegularFile.CreatePrivateWriteStream( database ) )
            {
                created.Flush( true );
            }

            Connection = new( $"Data Source={database};Pooling=False" );
            Connection.Open( );
            using var command = Connection.CreateCommand( );
            command.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; CREATE TABLE publication_probe(value INTEGER)";
            command.ExecuteNonQuery( );
            Directory = new( Path.Combine( Root, "history.data" ) );
            Ledger = new( Connection, Directory, new object( ) );
            Ledger.CreateSchema( );
        }

        internal long Scalar( string sql )
        {
            using var command = Connection.CreateCommand( );
            command.CommandText = sql;
            return Convert.ToInt64( command.ExecuteScalar( ), System.Globalization.CultureInfo.InvariantCulture );
        }

        public void Dispose( )
        {
            Connection.Dispose( );
            Directory.Dispose( );
            System.IO.Directory.Delete( Root, recursive: true );
        }
    }
}
