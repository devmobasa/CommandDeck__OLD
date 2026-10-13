namespace CommandDeck.ExtensionTools;

internal static class Program
{
    public static async Task<int> Main( string[] args )
    {
        using var lifetime = new CancellationTokenSource( TimeSpan.FromMinutes( 10 ) );
        Console.CancelKeyPress += ( _, e ) =>
        {
            e.Cancel = true;
            lifetime.Cancel( );
        };
        try
        {
            return await AuthorCommands.RunAsync( args, lifetime.Token ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is ArgumentException or InvalidOperationException or IOException or System.Text.Json.JsonException or OperationCanceledException )
        {
            Console.Error.WriteLine( exception.Message );
            return 1;
        }
    }
}
