using System.Text.Json;

namespace CommandDeck;

public static class LaunchArgumentCodec
{
    public static string Encode( IReadOnlyList<string> arguments )
    {
        return JsonSerializer.Serialize( arguments.ToArray( ), CommandDeckJsonContext.Default.StringArray );
    }

    public static IReadOnlyList<string> Decode( string value )
    {
        try
        {
            string[]? arguments = JsonSerializer.Deserialize(
                value,
                CommandDeckJsonContext.Default.StringArray );
            if ( arguments is null )
            {
                throw new InvalidDataException( "Launch arguments are invalid." );
            }

            return arguments;
        }
        catch ( JsonException exception )
        {
            throw new InvalidDataException( "Launch arguments are invalid.", exception );
        }
    }
}
