using System.Buffers;
using System.Text;
using System.Text.Json;

namespace CommandDeck;

public static class PayloadWriter
{
    public static string Create( params (string Key, string? Value)[] values )
    {
        ArrayBufferWriter<byte> buffer = new( );
        using ( Utf8JsonWriter writer = new( buffer ) )
        {
            writer.WriteStartObject( );
            foreach ( (var key, var value) in values )
            {
                if ( value is null )
                {
                    writer.WriteNull( key );
                }
                else
                {
                    writer.WriteString( key, value );
                }
            }

            writer.WriteEndObject( );
        }

        return Encoding.UTF8.GetString( buffer.WrittenSpan );
    }
}
