using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static int RunClipboardHost( CliArguments arguments, CancellationToken cancellationToken )
    {
        if ( arguments.HasFlag( "--capture" ) )
        {
            return ClipboardNativeHost.RunAsync(
                arguments.GetOption( "--control-read" ) ?? "", arguments.GetOption( "--control-write" ) ?? "",
                ulong.Parse( arguments.GetOption( "--host-epoch" ) ?? "0", System.Globalization.CultureInfo.InvariantCulture ),
                cancellationToken ).GetAwaiter( ).GetResult( );
        }

        if ( arguments.HasFlag( "--transport-probe" ) )
        {
            return ClipboardHostTransportProbe.RunAsync(
                arguments.GetOption( "--control-read" ) ?? "", arguments.GetOption( "--control-write" ) ?? "",
                ulong.Parse( arguments.GetOption( "--host-epoch" ) ?? "0", System.Globalization.CultureInfo.InvariantCulture ),
                acknowledge: true, cancellationToken ).GetAwaiter( ).GetResult( );
        }

        if ( !arguments.HasFlag( "--probe" ) )
        {
            Console.Error.WriteLine( "Clipboard native capture is not yet available." );
            return 1;
        }

        using ClipboardWaylandProbe probe = new( );
        var capabilities = probe.Run( null, cancellationToken );
        // Registry-only counts contain no selection data, offer names or window titles.
        Console.WriteLine( JsonSerializer.Serialize( capabilities, ClipboardHostJsonContext.Default.ClipboardWaylandCapabilities ) );
        return 0;
    }
}
