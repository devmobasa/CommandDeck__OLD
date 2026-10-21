using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardMigrationPreservesLegacyHistory( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-migration-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        ClipboardTestClock clock = new( );
        try
        {
            WriteLegacyClipboard( path, 450, 400, clock.Now.AddDays( -10 ).ToUnixTimeSeconds( ), enabled: true );
            using ( ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions( ), clock ) )
            {
                Equal( 800, store.Policy.MaximumEntries );
                Equal( ClipboardConsent.Text, store.Policy.Consent );
                True( store.MigrationReview.Required, "capacity and expired rows require explicit review" );
                Equal( 450, store.MigrationReview.EntryCount );
                Equal( 400, store.MigrationReview.PinnedCount );
                Equal( 50, store.MigrationReview.ExpiredCount );
                True( !store.Enabled, "migration hold prevents capture" );
                store.Capture( "not admitted during review" );
                store.Prune( );
                Equal( 450, store.MigrationReview.EntryCount );
                var id = Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( "legacy 0" ) ) );
                Equal( "legacy 0", store.Read( id ) );
            }

            using ( ClipboardHistoryStore restarted = new( path, new ClipboardHistoryOptions { MaximumEntries = 10, RetentionHours = 1 }, clock ) )
            {
                Equal( 800, restarted.Policy.MaximumEntries );
                Equal( 168, restarted.Policy.RetentionHours );
                var review = restarted.MigrationReview;
                Throws<InvalidOperationException>( ( ) => restarted.AcknowledgeMigration( restarted.Policy.Revision, review.StoreRevision, false ) );
                Equal( 450, restarted.MigrationReview.EntryCount );
                restarted.Delete( Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( "legacy 449" ) ) ) );
                Throws<InvalidOperationException>( ( ) => restarted.AcknowledgeMigration( restarted.Policy.Revision, review.StoreRevision, true ) );
                restarted.AcknowledgeMigration( restarted.Policy.Revision, restarted.MigrationReview.StoreRevision, true );
                True( !restarted.MigrationReview.Required && restarted.Enabled, "review acknowledgement releases capture" );
                Equal( 400, restarted.MigrationReview.EntryCount );
                restarted.Capture( "new text" );
                Equal( 401, restarted.MigrationReview.EntryCount );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardMigrationRejectsUnknownAndExcessiveData( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-invalid-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            WriteLegacyClipboard( path, 600, 501, DateTimeOffset.UtcNow.ToUnixTimeSeconds( ), enabled: false );
            var before = File.ReadAllBytes( path );
            Throws<IOException>( ( ) => { using var rejected = new ClipboardHistoryStore( path, new ClipboardHistoryOptions( ) ); } );
            True( before.SequenceEqual( File.ReadAllBytes( path ) ), "failed capacity preflight changes no data or schema" );
            using ( SqliteConnection database = new( $"Data Source={path};Pooling=False" ) )
            {
                database.Open( );
                using var command = database.CreateCommand( );
                command.CommandText = "PRAGMA user_version=5";
                command.ExecuteNonQuery( );
            }

            before = File.ReadAllBytes( path );
            Throws<IOException>( ( ) => { using var rejected = new ClipboardHistoryStore( path, new ClipboardHistoryOptions( ) ); } );
            True( before.SequenceEqual( File.ReadAllBytes( path ) ), "newer schema is preserved" );
            var empty = Path.Combine( root, "truncated.db" );
            File.WriteAllBytes( empty, [] );
            Throws<IOException>( ( ) => { using var rejected = new ClipboardHistoryStore( empty, new ClipboardHistoryOptions( ) ); } );
            Equal( 0L, new FileInfo( empty ).Length );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardPolicyPauseAndConsentPersist( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-policy-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            using ( ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions( ) ) )
            {
                var initial = store.Policy;
                Equal( ClipboardConsent.None, initial.Consent );
                var paused = store.SetCapturePolicy( initial.Revision, ClipboardConsent.Text, true );
                True( !store.Enabled, "paused consent does not capture" );
                store.Capture( "not retained" );
                Equal( 0, store.Search( "", 10 ).Count );
                Throws<InvalidOperationException>( ( ) => store.SetCapturePolicy( initial.Revision, ClipboardConsent.Rich, false ) );
                Equal( paused, store.Policy );
                var epoch = store.CaptureEpoch;
                var resumed = store.SetCapturePolicy( paused.Revision, ClipboardConsent.Text, false );
                store.Capture( "stale paused event", epoch );
                Equal( 0, store.Search( "", 10 ).Count );
                epoch = store.CaptureEpoch;
                store.Clear( );
                store.Capture( "stale pre-clear event", epoch );
                Equal( 0, store.Search( "", 10 ).Count );
                store.SetCapturePolicy( resumed.Revision, ClipboardConsent.Text, true );
            }

            using ClipboardHistoryStore restarted = new( path, new ClipboardHistoryOptions { MaximumEntries = 10 } );
            True( restarted.Policy.Paused, "pause survives restart" );
            Equal( ClipboardConsent.Text, restarted.Policy.Consent );
            Equal( 200, restarted.Policy.MaximumEntries );
            restarted.SetCapturePolicy( restarted.Policy.Revision, ClipboardConsent.Text, false );
            restarted.Capture( "retained" );
            restarted.SetCapturePolicy( restarted.Policy.Revision, ClipboardConsent.None, false );
            Equal( "retained", restarted.Search( "", 10 ).Single( ).Text );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void WriteLegacyClipboard( string path, int count, int pins, long copied, bool enabled )
    {
        using SqliteConnection database = new( $"Data Source={path};Pooling=False" );
        database.Open( );
        using var command = database.CreateCommand( );
        command.CommandText = "CREATE TABLE settings(enabled INTEGER NOT NULL); INSERT INTO settings VALUES($enabled); " +
            "CREATE TABLE entries(id TEXT PRIMARY KEY,text TEXT NOT NULL,copied INTEGER NOT NULL,pinned INTEGER NOT NULL DEFAULT 0)";
        command.Parameters.AddWithValue( "$enabled", enabled ? 1 : 0 );
        command.ExecuteNonQuery( );
        using var transaction = database.BeginTransaction( );
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO entries VALUES($id,$text,$copied,$pinned)";
        for ( var index = 0; index < count; index++ )
        {
            var text = "legacy " + index;
            command.Parameters.Clear( );
            command.Parameters.AddWithValue( "$id", Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text ) ) ) );
            command.Parameters.AddWithValue( "$text", text );
            command.Parameters.AddWithValue( "$copied", copied );
            command.Parameters.AddWithValue( "$pinned", index < pins ? 1 : 0 );
            command.ExecuteNonQuery( );
        }

        transaction.Commit( );
    }
}
