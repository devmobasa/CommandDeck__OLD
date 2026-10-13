using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    // One lowercase token, so a full-text index would store it whole.
    private const string ClipboardRemnantCanary = "qzxremnantcanarytoken";

    private static void ClipboardDeletedTextLeavesNoDatabaseRemnant( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-remnant-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            using ( ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions( ) ) )
            {
                store.Enabled = true;
                store.Capture( ClipboardRemnantCanary );
                store.Capture( "retained ordinary text" );
                var canary = store.Search( "", 10 ).Single( static entry => entry.Text == ClipboardRemnantCanary );

                store.Delete( canary.Id );
            }

            // The store's owner lease blocks other readers, so the file is scanned once it closes.
            AssertNoClipboardRemnant( path, ClipboardRemnantCanary );
            Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM sqlite_master WHERE name LIKE 'clipboard_fts%'" ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardSearchIndexMigrationDropsTheLegacyIndex( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-index-migration-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            WriteLegacyClipboardTexts( path, [ClipboardRemnantCanary, "retained ordinary text"] );
            Throws<InvalidOperationException>( ( ) =>
            {
                using ClipboardHistoryStore stopped = new( path, new ClipboardHistoryOptions( ), null, null, stage =>
                {
                    if ( stage == "projection-schema-committed" )
                    {
                        throw new InvalidOperationException( "Stop at schema version 3." );
                    }
                } );
            } );
            Equal( 3L, ClipboardDatabaseScalar( path, "PRAGMA user_version" ) );
            Equal( 1L, ClipboardDatabaseScalar(
                path, $"SELECT count(*) FROM clipboard_fts WHERE clipboard_fts MATCH '{ClipboardRemnantCanary}'" ) );

            using ( ClipboardHistoryStore migrated = new( path, new ClipboardHistoryOptions( ) ) )
            {
                Equal( 4L, ClipboardDatabaseScalar( path, "PRAGMA user_version" ) );
                Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM sqlite_master WHERE name LIKE 'clipboard_fts%'" ) );
                Equal( 1L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM sqlite_master WHERE type='trigger'" ) );
                Equal( 2L, ClipboardDatabaseScalar( path, "SELECT entry_count FROM clipboard_migrations WHERE version=4" ) );
                Equal( 2, migrated.Search( "", 10 ).Count );

                migrated.Delete( Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( ClipboardRemnantCanary ) ) ) );
                Equal( "retained ordinary text", migrated.Search( "", 10 ).Single( ).Text );
            }

            AssertNoClipboardRemnant( path, ClipboardRemnantCanary );
            using ClipboardHistoryStore reopened = new( path, new ClipboardHistoryOptions( ) );
            Equal( 4L, ClipboardDatabaseScalar( path, "PRAGMA user_version" ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void AssertNoClipboardRemnant( string path, string text )
    {
        var needle = Encoding.UTF8.GetBytes( text );
        foreach ( var file in new[] { path, path + "-wal", path + "-journal" } )
        {
            if ( !File.Exists( file ) )
            {
                continue;
            }

            byte[] bytes;
            using ( FileStream stream = new( file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete ) )
            {
                bytes = new byte[stream.Length];
                stream.ReadExactly( bytes );
            }

            True( bytes.AsSpan( ).IndexOf( needle ) < 0,
                $"deleted clipboard text is still readable in {Path.GetFileName( file )}" );
        }
    }

    private static void WriteLegacyClipboardTexts( string path, IReadOnlyList<string> texts )
    {
        using SqliteConnection database = new( $"Data Source={path};Pooling=False" );
        database.Open( );
        using var command = database.CreateCommand( );
        command.CommandText = "CREATE TABLE settings(enabled INTEGER NOT NULL); INSERT INTO settings VALUES(1); " +
            "CREATE TABLE entries(id TEXT PRIMARY KEY,text TEXT NOT NULL,copied INTEGER NOT NULL,pinned INTEGER NOT NULL DEFAULT 0)";
        command.ExecuteNonQuery( );

        command.CommandText = "INSERT INTO entries VALUES($id,$text,$copied,0)";
        foreach ( var text in texts )
        {
            command.Parameters.Clear( );
            command.Parameters.AddWithValue( "$id", Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( text ) ) ) );
            command.Parameters.AddWithValue( "$text", text );
            command.Parameters.AddWithValue( "$copied", DateTimeOffset.UtcNow.ToUnixTimeSeconds( ) );
            command.ExecuteNonQuery( );
        }
    }
}
