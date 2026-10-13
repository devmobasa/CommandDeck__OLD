using System.Text;
using System.Text.Json;

namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    private const int MaximumProjectionMetadataBytes = 512 * 1024;
    private const string ProjectionSchema = """
        CREATE TABLE clipboard_projections(
            entry_id TEXT PRIMARY KEY REFERENCES entries(id) ON DELETE CASCADE,
            metadata TEXT NOT NULL)
        """;
    private static readonly ClipboardProjectionMetadata EmptyProjection = new( false, false, false, [] );

    private void InitializeProjectionStorage( )
    {
        if ( HasRichSchema )
        {
            return;
        }

        RequireMigrationSpace( );
        Transaction( ( ) =>
        {
            Execute( ProjectionSchema );
            _migrationCheckpoint?.Invoke( "projection-schema-created" );
            Execute( "INSERT INTO clipboard_projections SELECT id,$metadata FROM entries", ("$metadata", SerializeProjection( EmptyProjection )) );
            Execute( """
                INSERT INTO clipboard_migrations SELECT 3,(SELECT count(*) FROM entries),original_capacity,
                    maximum_entries,quota_mib FROM clipboard_state CROSS JOIN clipboard_policy;
                PRAGMA user_version=3;
                """ );
            _migrationCheckpoint?.Invoke( "projection-rows-written" );
            return 0;
        } );
        _migrationCheckpoint?.Invoke( "projection-schema-committed" );
    }

    private void ValidateProjectionSchema( )
    {
        RequireColumns( "clipboard_projections", ["entry_id", "metadata"] );
        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='clipboard_projections'";
        if ( command.ExecuteScalar( ) as string != ProjectionSchema )
        {
            throw new IOException( "Clipboard projection schema is invalid; existing data was preserved." );
        }
    }

    private void ValidateProjectionRows( )
    {
        if ( Scalar( "SELECT count(*) FROM clipboard_projections" ) != Scalar( "SELECT count(*) FROM entries" ) ||
            Scalar( $"""
                SELECT count(*) FROM clipboard_projections p LEFT JOIN entries e ON p.entry_id=e.id
                WHERE e.id IS NULL OR typeof(p.metadata)!='text' OR length(cast(p.metadata AS blob))>{MaximumProjectionMetadataBytes}
                """ ) != 0 )
        {
            throw new IOException( "Clipboard projection relationships are invalid; existing data was preserved." );
        }

        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT metadata FROM clipboard_projections LIMIT 1001";
        using var reader = command.ExecuteReader( );
        while ( reader.Read( ) )
        {
            _ = ParseProjection( reader.GetString( 0 ) );
        }
    }

    public ClipboardProjectionMetadata? ReadProjectionMetadata( string id, string incarnation, long expectedRevision )
    {
        lock ( _gate )
        {
            if ( ReadDocument( id, incarnation, expectedRevision ) is null )
            {
                return null;
            }

            using var command = _connection.CreateCommand( );
            command.CommandText = "SELECT metadata FROM clipboard_projections WHERE entry_id=$id";
            command.Parameters.AddWithValue( "$id", id );
            var json = command.ExecuteScalar( ) as string;
            if ( json is null )
            {
                throw new IOException( "Clipboard projection is missing." );
            }

            return ParseProjection( json );
        }
    }

    private void WriteProjectionMetadata( string id, ClipboardProjectionMetadata metadata )
    {
        var json = SerializeProjection( metadata );
        _ = ParseProjection( json );

        Execute( "INSERT INTO clipboard_projections VALUES($id,$metadata) ON CONFLICT(entry_id) DO UPDATE SET metadata=excluded.metadata",
            ("$id", id), ("$metadata", json) );
    }

    private static string SerializeProjection( ClipboardProjectionMetadata metadata ) =>
        JsonSerializer.Serialize( metadata, CommandDeckJsonContext.Default.ClipboardProjectionMetadata );

    private static ClipboardProjectionMetadata ParseProjection( string json )
    {
        if ( json.Length > MaximumProjectionMetadataBytes )
        {
            throw new IOException( "Clipboard projection metadata exceeds its bound." );
        }

        try
        {
            var metadata = JsonSerializer.Deserialize( json, CommandDeckJsonContext.Default.ClipboardProjectionMetadata );
            if ( metadata is null || metadata.Destinations is null || metadata.Destinations.Length > 256 ||
                SerializeProjection( metadata ) != json || !ValidProjectionDestinations( metadata ) ||
                metadata.ImageWidth.HasValue != metadata.ImageHeight.HasValue ||
                metadata.ImageWidth is < 1 or > 32768 || metadata.ImageHeight is < 1 or > 32768 )
            {
                throw new IOException( "Clipboard projection metadata is invalid; existing data was preserved." );
            }

            return metadata;
        }
        catch ( JsonException )
        {
            throw new IOException( "Clipboard projection metadata is invalid; existing data was preserved." );
        }
    }

    private static bool ValidProjectionDestinations( ClipboardProjectionMetadata metadata )
    {
        if ( metadata.OpenDestination is not null &&
            (metadata.Destinations.Length != 1 || metadata.Destinations[0] != metadata.OpenDestination) )
        {
            return false;
        }

        if ( metadata.ReportedSource is not null && !ClipboardHttpUrl.TryParseSingleHttpUrl( metadata.ReportedSource, out _, out _ ) )
        {
            return false;
        }

        if ( metadata.Destinations.Length == 0 )
        {
            return true;
        }

        var bytes = Encoding.UTF8.GetBytes( string.Join( '\n', metadata.Destinations ) );
        return ClipboardUriList.TryParse( bytes, false, out var parsed, out _ ) &&
            parsed!.Destinations.Count == metadata.Destinations.Length &&
            parsed.Destinations.Select( static destination => destination.Literal ).SequenceEqual( metadata.Destinations, StringComparer.Ordinal );
    }
}
