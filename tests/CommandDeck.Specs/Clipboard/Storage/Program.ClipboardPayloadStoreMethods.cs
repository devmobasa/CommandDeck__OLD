using System.Text;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardPayloadStorePreservesBytesAndRevokesDeletedEntries( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-payload-store-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            string identity;
            string incarnation;
            using ( ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions( ) ) )
            {
                store.Enabled = true;
                const string text = "exact 😀 café\r\n";
                store.Capture( text );
                Throws<IOException>( ( ) =>
                {
                    using ClipboardHistoryStore competing = new( path, new ClipboardHistoryOptions( ) );
                } );
                var entry = store.SnapshotEntries( new( ), false ).Single( );
                identity = entry.Id;
                incarnation = entry.Incarnation;
                var file = Directory.GetFiles( path + ".payloads" ).Single( );
                True( Encoding.UTF8.GetBytes( text ).SequenceEqual( File.ReadAllBytes( file ) ), "stored offer preserves exact bytes" );
                var revision = store.StoreRevision;
                store.Capture( text );
                Equal( file, Directory.GetFiles( path + ".payloads" ).Single( ) );
                Equal( incarnation, store.SnapshotEntries( new( ), false ).Single( ).Incarnation );
                True( store.StoreRevision > revision, "recopy updates revision without replacing immutable offers" );
                var document = store.ReadDocument( identity, incarnation )!;
                store.MutateEntry( identity, incarnation, document.Summary.Revision, "keywords", ["synthetic"] );
                Equal( 4L, ClipboardDatabaseScalar( path, "PRAGMA user_version" ) );
            }

            using ( ClipboardHistoryStore reopened = new( path, new ClipboardHistoryOptions( ) ) )
            {
                Equal( "synthetic", reopened.ReadDocument( identity, incarnation )!.Summary.Keywords.Single( ) );
                reopened.Delete( identity );
                Equal( 0, Directory.GetFiles( path + ".payloads" ).Length );
                Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_payloads" ) );
                Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM entries" ) );
                reopened.Capture( "exact 😀 café\r\n" );
                True( reopened.SnapshotEntries( new( ), false ).Single( ).Incarnation != incarnation, "recreated entry has a new identity" );
                reopened.Clear( );
                Equal( 0, Directory.GetFiles( path + ".payloads" ).Length );
                Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_offers" ) );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardPayloadMigrationLowSpacePreservesSource( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-space-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            WriteLegacyClipboard( path, 10, 4, DateTimeOffset.UtcNow.ToUnixTimeSeconds( ), enabled: true );
            var before = File.ReadAllBytes( path );
            Throws<IOException>( ( ) =>
            {
                using ClipboardHistoryStore rejected = new( path, new ClipboardHistoryOptions( ), null, ( ) => 0 );
            } );
            True( before.SequenceEqual( File.ReadAllBytes( path ) ), "disk preflight precedes even the legacy schema upgrade" );
            True( !Directory.Exists( path + ".payloads" ), "disk preflight creates no payload objects" );
            using ClipboardHistoryStore migrated = new( path, new ClipboardHistoryOptions( ) );
            Equal( 10, migrated.MigrationReview.EntryCount );
            Equal( 4, migrated.MigrationReview.PinnedCount );
            Equal( 10, Directory.GetFiles( path + ".payloads" ).Length );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardPayloadStoreRecoversMissingAndDamagedFiles( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-damaged-payload-" ).FullName;
        try
        {
            foreach ( var missing in new[] { true, false } )
            {
                var path = Path.Combine( root, missing + ".db" );
                long revision;
                using ( ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions( ) ) )
                {
                    store.Enabled = true;
                    store.Capture( "synthetic payload" );
                    revision = store.StoreRevision;
                }

                var file = Directory.GetFiles( path + ".payloads" ).Single( );
                if ( missing )
                {
                    File.Delete( file );
                }
                else
                {
                    File.WriteAllText( file, "damaged bytes" );
                }

                using ClipboardHistoryStore reopened = new( path, new ClipboardHistoryOptions( ) );
                Equal( 0, reopened.Search( "", 10 ).Count );
                True( reopened.StoreRevision > revision, "damaged entry is revoked before browser access" );
                Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM entries" ) );
                Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_payloads" ) );
                Equal( 0, Directory.GetFiles( path + ".payloads" ).Length );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardPayloadMigrationRetriesDamagedStagedProgress( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-migration-retry-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            WriteLegacyClipboard( path, 2, 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds( ), enabled: true );
            Throws<IOException>( ( ) =>
            {
                using ClipboardHistoryStore interrupted = new( path, new ClipboardHistoryOptions( ), null, null, stage =>
                {
                    if ( stage == "row-migrated" )
                    {
                        throw new IOException( "Injected interruption after durable migration progress." );
                    }
                } );
            } );
            Equal( 1L, ClipboardDatabaseScalar( path, "PRAGMA user_version" ) );
            var file = Directory.GetFiles( path + ".payloads" ).Single( );
            File.WriteAllText( file, "damaged progress" );
            using ClipboardHistoryStore resumed = new( path, new ClipboardHistoryOptions( ) );
            Equal( 2, resumed.MigrationReview.EntryCount );
            Equal( 1, resumed.MigrationReview.PinnedCount );
            Equal( 2, Directory.GetFiles( path + ".payloads" ).Length );
            True( !File.Exists( file ), "damaged staged progress is replaced from preserved source rows" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static long ClipboardDatabaseScalar( string path, string sql )
    {
        using SqliteConnection database = new( $"Data Source={path};Pooling=False" );
        database.Open( );
        using var command = database.CreateCommand( );
        command.CommandText = sql;
        return ( long ) command.ExecuteScalar( )!;
    }

    private static string ClipboardDatabaseScalarText( string path, string sql )
    {
        using SqliteConnection database = new( $"Data Source={path};Pooling=False" );
        database.Open( );
        using var command = database.CreateCommand( );
        command.CommandText = sql;
        return ( string ) command.ExecuteScalar( )!;
    }
}
