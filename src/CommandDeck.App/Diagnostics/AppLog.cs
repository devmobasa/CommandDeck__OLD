namespace CommandDeck;

public static class AppLog
{
    public static bool Verbose { get; set; }

    public static void Debug( string message )
    {
        if ( Verbose )
        {
            Write( "debug", message );
        }
    }

    public static void Info( string message ) => Write( "info", message );
    public static void Warning( string message ) => Write( "warning", message );

    public static void Error( string message, Exception? exception = null ) =>
        Write( "error", exception is null ? message : $"{message} {exception}" );

    // Specs capture lines here instead of scraping stderr.
    internal static Action<string, string>? Sink { get; set; }

    private static void Write( string level, string message )
    {
        if ( Sink is { } sink )
        {
            sink( level, message );
            return;
        }

        Console.Error.WriteLine(
            $"{DateTimeOffset.UtcNow:O} {level.ToUpperInvariant( ),-7} {message}" );
    }
}
