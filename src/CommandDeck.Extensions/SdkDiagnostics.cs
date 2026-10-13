using System.Text;

namespace CommandDeck.Extensions;

internal static class SdkDiagnostics
{
    public static void Log( TextWriter writer, string command, string method, Exception exception )
    {
        if ( exception is OperationCanceledException )
        {
            return;
        }

        var text = $"[commanddeck-sdk] {command} {method}: {exception.GetType( ).Name}: {exception.Message}\n{exception.StackTrace}\n";
        StringBuilder bounded = new( );
        var bytes = 0;
        foreach ( var rune in text.EnumerateRunes( ) )
        {
            if ( bytes + rune.Utf8SequenceLength > 4096 )
            {
                break;
            }

            bounded.Append( rune.ToString( ) );
            bytes += rune.Utf8SequenceLength;
        }

        try
        {
            writer.Write( bounded.ToString( ) );
        }
        catch ( IOException ) { /* Diagnostics must not replace the author's exception. */ }
        catch ( ObjectDisposedException ) { /* The diagnostic destination may close during shutdown. */ }
    }
}
