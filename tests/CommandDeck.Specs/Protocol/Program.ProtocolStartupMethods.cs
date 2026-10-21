using System.Net.Sockets;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SocketConnectionWaitsForDelayedDaemonAsync( )
    {
        var socketPath = SpecTemporaryFiles.PathFor( $"commanddeck-delayed-socket-{Guid.NewGuid( ):N}.sock" );
        using Socket listener = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        var startup = Task.Run( async ( ) =>
        {
            await Task.Delay( TimeSpan.FromMilliseconds( 1_200 ) );
            listener.Bind( new UnixDomainSocketEndPoint( socketPath ) );
            listener.Listen( 1 );
        } );

        try
        {
            await using var client = await ConnectWithRetryAsync( socketPath );
            await startup;
        }
        finally
        {
            await startup;
            listener.Dispose( );
            if ( File.Exists( socketPath ) )
            {
                File.Delete( socketPath );
            }
        }
    }
}
