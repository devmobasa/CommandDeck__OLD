using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardProjectionUpgradePreservesV2History( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-projection-storage-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            ClipboardEntryIdentity identity;
            long revision;
            using ( ClipboardHistoryStore original = new( path, new( ) ) )
            {
                original.Enabled = true;
                original.Capture( "unchanged 😀\r\n" );
                identity = original.SnapshotEntries( new( ), false ).Single( );
                original.Pin( identity.Id, true );
                revision = original.ReadDocument( identity.Id, identity.Incarnation )!.Summary.Revision;
            }

            var payload = Directory.GetFiles( path + ".payloads" ).Single( );
            var bytes = File.ReadAllBytes( payload );
            RestoreLegacyClipboardSearchIndex( path );
            ExecuteProjectionFixtureSql( path, "DROP TABLE clipboard_projections; DELETE FROM clipboard_migrations WHERE version=3; PRAGMA user_version=2" );
            using ( ClipboardHistoryStore upgraded = new( path, new( ) ) )
            {
                Equal( 4L, ClipboardDatabaseScalar( path, "PRAGMA user_version" ) );
                Equal( "unchanged 😀\r\n", upgraded.ReadCopyText( identity.Id, identity.Incarnation, revision ) );
                True( upgraded.ReadDocument( identity.Id, identity.Incarnation, revision )!.Summary.Pinned );
                True( File.ReadAllBytes( payload ).SequenceEqual( bytes ) );
                var metadata = upgraded.ReadProjectionMetadata( identity.Id, identity.Incarnation, revision )!;
                True( !metadata.ExtractedText && !metadata.Truncated && !metadata.ReducedFidelity );
                Equal( 0, metadata.Destinations.Length );
                True( upgraded.ReadProjectionMetadata( identity.Id, identity.Incarnation, revision + 1 ) is null );
                upgraded.Delete( identity.Id );
                Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_projections" ) );
                upgraded.Capture( "new after upgrade" );
                Equal( 1L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_projections" ) );
            }

            using ClipboardHistoryStore reopened = new( path, new( ) );
            Equal( "new after upgrade", reopened.Search( "", 10 ).Single( ).Text );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardProjectionCorruptionPrecedesStartupClear( )
    {
        foreach ( var mutation in new[]
        {
            "UPDATE clipboard_projections SET metadata='{}'",
            "DELETE FROM clipboard_projections",
            "UPDATE clipboard_projections SET metadata='null'",
            "UPDATE clipboard_projections SET metadata=replace(metadata,'[]','[\"https://safe.invalid/\\nfile:///bad\"]')"
        } )
        {
            var root = Directory.CreateTempSubdirectory( "commanddeck-projection-corruption-" ).FullName;
            var path = Path.Combine( root, "history.db" );
            try
            {
                using ( ClipboardHistoryStore original = new( path, new( ) ) )
                {
                    original.Enabled = true;
                    original.Capture( "must remain" );
                }

                ExecuteProjectionFixtureSql( path, mutation + "; UPDATE clipboard_policy SET clear_on_startup=1" );
                Throws<IOException>( ( ) => { using ClipboardHistoryStore rejected = new( path, new( ) ); } );
                Equal( 1L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM entries" ) );
                Equal( 1, Directory.GetFiles( path + ".payloads" ).Length );
            }
            finally
            {
                Directory.Delete( root, recursive: true );
            }
        }
    }

    /// <summary>Turns a current store back into schema version 3, which kept a full-text index.</summary>
    private static void RestoreLegacyClipboardSearchIndex( string path )
    {
        ExecuteProjectionFixtureSql( path, $"""
            DROP TRIGGER clipboard_entry_delete;
            {ClipboardHistoryStore.SearchIndexSchema};
            INSERT INTO clipboard_fts(rowid,text,keywords) SELECT rowid,text,keywords FROM entries;
            DELETE FROM clipboard_migrations WHERE version=4;
            PRAGMA user_version=3;
            """ );
        foreach ( var trigger in ClipboardHistoryStore.SearchIndexPayloadTriggers )
        {
            ExecuteProjectionFixtureSql( path, trigger );
        }
    }

    private static void ExecuteProjectionFixtureSql( string path, string sql )
    {
        using SqliteConnection connection = new( new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString( ) );
        connection.Open( );
        using var command = connection.CreateCommand( );
        command.CommandText = sql;
        command.ExecuteNonQuery( );
    }
}
