namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    internal static ClipboardEntryPresentation Present( string kind, ClipboardProjectionMetadata metadata ) =>
        new( kind, metadata.ImageWidth, metadata.ImageHeight, metadata.Destinations.Length,
            metadata.OpenDestination is not null, metadata.ReducedFidelity );

    /// <summary>Decodes the retained PNG through the isolated renderer; returns null when no inert preview is available.</summary>
    public async ValueTask<string?> ReadImagePreviewAsync( string id, string incarnation, long expectedRevision,
        ClipboardPayloadProjection projections, CancellationToken token )
    {
        ArgumentNullException.ThrowIfNull( projections );
        ClipboardPayloadLease lease;
        long length;
        string digest;
        lock ( _gate )
        {
            var document = ReadDocument( id, incarnation, expectedRevision );
            if ( document is null || document.Summary.Presentation.Kind != ClipboardPresentationKinds.Images )
            {
                return null;
            }

            using var command = _connection.CreateCommand( );
            command.CommandText = """
                SELECT p.id,p.actual,p.digest FROM clipboard_offers o JOIN clipboard_payloads p ON p.id=o.payload_id
                JOIN clipboard_selections s ON s.entry_id=o.entry_id AND s.preferred_mime=o.mime WHERE o.entry_id=$id
                """;
            command.Parameters.AddWithValue( "$id", id );
            using var reader = command.ExecuteReader( );
            if ( !reader.Read( ) || reader.IsDBNull( 1 ) || reader.IsDBNull( 2 ) )
            {
                return null;
            }

            length = reader.GetInt64( 1 );
            digest = reader.GetString( 2 );
            lease = _payloadDirectory!.OpenRead( Guid.ParseExact( reader.GetString( 0 ), "N" ) );
        }

        using ( lease )
        {
            try
            {
                var image = await projections.ReadPngAsync( lease, length, digest, token ).ConfigureAwait( false );
                return image.Succeeded ? image.Preview!.ImageSource : null;
            }
            catch ( InvalidDataException )
            {
                return null;
            }
        }
    }
}
