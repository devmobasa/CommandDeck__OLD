using System.Security.Cryptography;

namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    public string ReadCopyText( string id, string incarnation, long expectedRevision )
    {
        lock ( _gate )
        {
            var document = ReadDocument( id, incarnation, expectedRevision );
            if ( document is null )
            {
                throw new InvalidOperationException( "Clipboard entry changed or expired; refresh before copying." );
            }

            if ( id.Length != 64 )
            {
                throw new InvalidOperationException( "This entry requires multi-format clipboard serving." );
            }

            using var command = _connection.CreateCommand( );
            command.CommandText = "SELECT o.payload_id FROM clipboard_offers o JOIN clipboard_selections s ON o.entry_id=s.entry_id AND o.mime=s.preferred_mime WHERE o.entry_id=$id";
            command.Parameters.AddWithValue( "$id", id );
            var payloadId = command.ExecuteScalar( ) as string;
            if ( payloadId is null )
            {
                throw new IOException( "Clipboard payload is unavailable." );
            }

            try
            {
                using var lease = _payloadDirectory!.OpenRead( Guid.ParseExact( payloadId, "N" ) );
                var length = lease.Stream.Length;
                if ( length is < 1 or > 32768 )
                {
                    throw new IOException( "Clipboard payload length is invalid." );
                }

                var bytes = new byte[checked(( int ) length)];
                lease.Stream.ReadExactly( bytes );
                if ( lease.Stream.ReadByte( ) != -1 || Convert.ToHexString( SHA256.HashData( bytes ) ) != id )
                {
                    throw new IOException( "Clipboard payload changed." );
                }

                return StrictUtf8.GetString( bytes );
            }
            catch ( IOException )
            {
                Delete( id );
                throw new IOException( "Clipboard payload is missing or damaged; the entry was removed." );
            }
        }
    }
}
