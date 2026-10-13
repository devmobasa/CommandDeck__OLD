using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CommandDeck;

public static class JsonNodeReader
{
    public static bool TryReadInt64( JsonNode? node, out long value )
    {
        value = 0;
        if ( node is not JsonValue jsonValue )
        {
            return false;
        }
        try
        {
            switch ( jsonValue.GetValueKind( ) )
            {
                case JsonValueKind.Number:
                    value = ( long ) jsonValue;
                    return true;
                case JsonValueKind.String:
                    return long.TryParse( ( string? ) jsonValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value );
                default:
                    return false;
            }
        }
        catch ( Exception exception ) when ( exception is FormatException or InvalidOperationException or OverflowException )
        {
            return false;
        }
    }

    public static bool TryReadInt32( JsonNode? node, out int value )
    {
        value = 0;
        if ( !TryReadInt64( node, out var wide ) || wide is < int.MinValue or > int.MaxValue )
        {
            return false;
        }
        value = ( int ) wide;
        return true;
    }
}
