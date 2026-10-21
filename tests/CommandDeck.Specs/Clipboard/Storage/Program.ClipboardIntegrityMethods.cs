using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardCurrentSchemaRejectsCorruptionBeforeStartupDeletion( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-integrity-" ).FullName;
        try
        {
            string[] corruptions =
            [
                "UPDATE clipboard_policy SET paused=2",
                "UPDATE clipboard_policy SET clear_on_startup=2",
                "UPDATE clipboard_policy SET consent='invalid'",
                "DELETE FROM clipboard_policy",
                "INSERT INTO clipboard_policy SELECT * FROM clipboard_policy",
                "DELETE FROM clipboard_state",
                "INSERT INTO clipboard_state SELECT * FROM clipboard_state",
                "UPDATE clipboard_state SET revision=0",
                "UPDATE clipboard_state SET migration_review=2",
                "UPDATE clipboard_state SET original_capacity=1",
                "UPDATE entries SET id='wrong'",
                "UPDATE entries SET text=cast(x'ff' AS text)",
                "UPDATE entries SET copied='yesterday'",
                "UPDATE entries SET copied=9223372036854775807",
                "UPDATE entries SET pinned=2",
                "UPDATE entries SET revision=0",
                "UPDATE entries SET incarnation='00000000000000000000000000000000'",
                "UPDATE entries SET keywords='not json'",
                "UPDATE entries SET keywords='[\"a\",\"A\"]'",
                "UPDATE entries SET keywords='[\" leading\"]'",
                "UPDATE entries SET keywords='[null]'",
                "UPDATE entries SET keywords=quote(zeroblob(9000))",
                "CREATE TABLE unexpected(value TEXT)",
                "CREATE TRIGGER unexpected AFTER DELETE ON entries BEGIN UPDATE clipboard_state SET revision=99; END"
            ];
            for ( var index = 0; index < corruptions.Length; index++ )
            {
                var path = Path.Combine( root, index + ".db" );
                using ( ClipboardHistoryStore seed = new( path, new ClipboardHistoryOptions( ) ) )
                {
                    seed.Enabled = true;
                    seed.Capture( "preserve this synthetic entry" );
                }

                using ( SqliteConnection database = new( $"Data Source={path};Pooling=False" ) )
                {
                    database.Open( );
                    using var command = database.CreateCommand( );
                    command.CommandText = "PRAGMA foreign_keys=OFF; UPDATE clipboard_policy SET clear_on_startup=1; " + corruptions[index];
                    command.ExecuteNonQuery( );
                }

                var before = File.ReadAllBytes( path );
                Throws<IOException>( ( ) => { using var rejected = new ClipboardHistoryStore( path, new ClipboardHistoryOptions( ) ); } );
                True( before.AsSpan( ).SequenceEqual( File.ReadAllBytes( path ) ), corruptions[index] );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardCaptureRejectsInvalidUnicodeWithoutReplacement( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-unicode-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new ClipboardHistoryOptions( ) );
            store.Enabled = true;
            var revision = store.StoreRevision;
            foreach ( var invalid in new[] { "\ud800", "prefix\udfff", "nul\0text", new string( 'é', 16385 ) } )
            {
                store.Capture( invalid );
                Equal( revision, store.StoreRevision );
            }

            Equal( 0, store.Search( "", 10 ).Count );
            store.Capture( "exact 😀 café\r\n" );
            Equal( "exact 😀 café\r\n", store.Search( "", 10 ).Single( ).Text );
            var identity = store.SnapshotEntries( new( ), false ).Single( );
            var document = store.ReadDocument( identity.Id, identity.Incarnation )!;
            Throws<InvalidOperationException>( ( ) => store.MutateEntry( identity.Id, identity.Incarnation,
                document.Summary.Revision, "keywords", ["\ud800"] ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
