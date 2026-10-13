using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static int RunClipboardHtmlProjection( )
    {
        try
        {
            using var input = File.OpenRead( "/input" );
            var bytes = new byte[ClipboardHtmlProjector.MaximumInputBytes + 1];
            var length = 0;
            while ( length < bytes.Length )
            {
                var read = input.Read( bytes.AsSpan( length ) );
                if ( read == 0 )
                {
                    break;
                }

                length += read;
            }

            var projection = ClipboardHtmlProjector.Project( bytes.AsSpan( 0, length ) );
            Console.WriteLine( JsonSerializer.Serialize( projection, CommandDeckJsonContext.Default.ClipboardHtmlProjection ) );
            return 0;
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or ArgumentException or OutOfMemoryException )
        {
            // Never put parser diagnostics, markup or attributes on a logging path.
            Console.Error.WriteLine( "Clipboard HTML projection failed." );
            return 1;
        }
    }
}
