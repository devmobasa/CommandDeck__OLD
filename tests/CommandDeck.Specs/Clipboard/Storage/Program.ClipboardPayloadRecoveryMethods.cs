namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardPayloadRecoveryChargesOrphansAndRevokesDamagedLiveAsync( )
    {
        using PayloadLedgerFixture fixture = new( );
        using var reservation = fixture.Ledger.Reserve( ClipboardOfferPlan.Create( ["text/plain"], ClipboardConsent.Text ), 64L * 1024 * 1024 );
        ClipboardCompletedPayload live;
        using ( var writer = fixture.Ledger.OpenWriter( reservation, 0 ) )
        {
            await writer.WriteAsync( "valid"u8.ToArray( ), default );
            live = writer.Complete( );
        }

        fixture.Ledger.Publish( reservation, new HashSet<Guid> { live.Id }, ( ) => true, ( connection, _ ) =>
        {
            using var command = connection.CreateCommand( );
            command.CommandText = "INSERT INTO publication_probe VALUES(1)";
            command.ExecuteNonQuery( );
        } );
        var orphan = Guid.NewGuid( );
        using ( var writer = fixture.Directory.CreateStaging( orphan ) )
        {
            writer.Stream.Write( "orphaned"u8 );
        }

        // Same-size damage exercises digest validation rather than only file length.
        File.WriteAllText( Path.Combine( fixture.Root, "history.data", live.Id.ToString( "N" ) + ".blob" ), "wrong" );
        var recovered = fixture.Ledger.Recover( ( connection, damaged ) =>
        {
            Equal( live.Id, damaged.Single( ) );
            using var command = connection.CreateCommand( );
            command.CommandText = "DELETE FROM publication_probe";
            command.ExecuteNonQuery( );
        } );
        Equal( 1, recovered.Orphans );
        Equal( 1, recovered.DamagedLivePayloads );
        Equal( 13L, fixture.Ledger.ChargedBytes );
        Equal( 0L, fixture.Scalar( "SELECT count(*) FROM publication_probe" ) );
        Equal( 2, fixture.Ledger.Cleanup( ) );
        Equal( 0L, fixture.Ledger.ChargedBytes );
        Equal( 0, fixture.Directory.Inventory( ).Count );
        var again = fixture.Ledger.Recover( ( _, _ ) => throw new InvalidOperationException( "No damaged rows expected." ) );
        Equal( new ClipboardPayloadRecovery( 0, 0 ), again );
    }

    private static async Task ClipboardPayloadRecoveryFailsWithoutPartialRemovalAsync( )
    {
        using PayloadLedgerFixture fixture = new( );
        using var reservation = fixture.Ledger.Reserve( ClipboardOfferPlan.Create( ["text/plain"], ClipboardConsent.Text ), 64L * 1024 * 1024 );
        ClipboardCompletedPayload live;
        using ( var writer = fixture.Ledger.OpenWriter( reservation, 0 ) )
        {
            await writer.WriteAsync( "valid"u8.ToArray( ), default );
            live = writer.Complete( );
        }

        fixture.Ledger.Publish( reservation, new HashSet<Guid> { live.Id }, ( ) => true, ( _, _ ) => { } );
        File.Delete( Path.Combine( fixture.Root, "history.data", live.Id.ToString( "N" ) + ".blob" ) );
        Throws<IOException>( ( ) => fixture.Ledger.Recover( ( connection, _ ) =>
        {
            using var command = connection.CreateCommand( );
            command.CommandText = "INSERT INTO publication_probe VALUES(1)";
            command.ExecuteNonQuery( );
            throw new IOException( "synthetic entry-removal failure" );
        } ) );
        Equal( 0L, fixture.Scalar( "SELECT count(*) FROM publication_probe" ) );
        Equal( 1L, fixture.Scalar( "SELECT count(*) FROM clipboard_payloads WHERE state=1" ) );
        Equal( 5L, fixture.Ledger.ChargedBytes );
        var recovery = fixture.Ledger.Recover( ( _, damaged ) => Equal( live.Id, damaged.Single( ) ) );
        Equal( 1, recovery.DamagedLivePayloads );
        Equal( 1, fixture.Ledger.Cleanup( ) );

        var orphan = Guid.NewGuid( );
        using ( var file = fixture.Directory.CreateStaging( orphan ) )
        {
            file.Stream.Write( "private orphan"u8 );
        }

        File.WriteAllText( Path.Combine( fixture.Root, "history.data", "unexpected" ), "leave untouched" );
        Throws<IOException>( ( ) => fixture.Ledger.Recover( ( _, _ ) => { } ) );
        Equal( 0L, fixture.Ledger.ChargedBytes );
        Equal( "leave untouched", File.ReadAllText( Path.Combine( fixture.Root, "history.data", "unexpected" ) ) );
    }

    private static async Task ClipboardPayloadRecoveryChargesConflictingStagingFilesAsync( )
    {
        using PayloadLedgerFixture fixture = new( );
        using var reservation = fixture.Ledger.Reserve( ClipboardOfferPlan.Create( ["text/plain"], ClipboardConsent.Text ), 64L * 1024 * 1024 );
        ClipboardCompletedPayload live;
        using ( var writer = fixture.Ledger.OpenWriter( reservation, 0 ) )
        {
            await writer.WriteAsync( "valid"u8.ToArray( ), default );
            live = writer.Complete( );
        }

        fixture.Ledger.Publish( reservation, new HashSet<Guid> { live.Id }, ( ) => true, ( _, _ ) => { } );
        using ( var staging = fixture.Directory.CreateStaging( live.Id ) )
        {
            staging.Stream.Write( "staging"u8 );
        }

        var recovered = fixture.Ledger.Recover( ( _, damaged ) => Equal( live.Id, damaged.Single( ) ) );
        Equal( 1, recovered.DamagedLivePayloads );
        Equal( 12L, fixture.Ledger.ChargedBytes );
        Equal( 1, fixture.Ledger.Cleanup( ) );
        Equal( 0, fixture.Directory.Inventory( ).Count );

        using var command = fixture.Connection.CreateCommand( );
        command.CommandText = "INSERT INTO clipboard_payloads(id,selection_id,state,charge) VALUES($id,$selection,1,5)";
        command.Parameters.AddWithValue( "$id", Guid.NewGuid( ).ToString( "N" ) );
        command.Parameters.AddWithValue( "$selection", Guid.NewGuid( ).ToString( "N" ) );
        command.ExecuteNonQuery( );
        Throws<IOException>( ( ) => fixture.Ledger.Recover( ( _, _ ) => throw new InvalidOperationException( "Invalid ledger must fail before callbacks." ) ) );
        Equal( 5L, fixture.Ledger.ChargedBytes );
    }
}
