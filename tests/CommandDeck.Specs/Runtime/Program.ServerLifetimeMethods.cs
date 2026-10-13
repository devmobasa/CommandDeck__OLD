using System.Net.Sockets;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ServerBoundsAndDrainsConnectionsAsync( )
    {
        using CancellationTokenSource stop = new( );
        var (runtime, server, directory, _) = await StartAgentRuntimeAsync( new ScriptedAgentBackend( ), stop.Token );
        List<Socket> clients = [];
        try
        {
            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 10 ) );
            for ( var index = 0; index < UnixProtocolServer.MaximumClients; index++ )
            {
                Socket socket = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
                clients.Add( socket );
                await socket.ConnectAsync( new UnixDomainSocketEndPoint( Path.Combine( directory, "commanddeck.sock" ) ), timeout.Token );
            }
            while ( server.ConnectedClients != UnixProtocolServer.MaximumClients )
            {
                await Task.Delay( 10, timeout.Token );
            }

            using Socket excess = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
            await excess.ConnectAsync( new UnixDomainSocketEndPoint( Path.Combine( directory, "commanddeck.sock" ) ), timeout.Token );
            Equal( 0, await excess.ReceiveAsync( new byte[1], SocketFlags.None, timeout.Token ) );

            // Send enough malformed requests to back up error writes without reading.
            var burst = System.Text.Encoding.UTF8.GetBytes( string.Concat( Enumerable.Repeat( "{\n", 50_000 ) ) );
            var write = clients[0].SendAsync( burst, SocketFlags.None, timeout.Token ).AsTask( );
            await stop.CancelAsync( );
            await server.Completion.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( 0, server.ConnectedClients );
            True( !File.Exists( Path.Combine( directory, "commanddeck.sock" ) ) );
            try
            {
                await write;
            }
            catch ( SocketException ) { }
        }
        finally
        {
            await stop.CancelAsync( );
            foreach ( var client in clients )
            {
                client.Dispose( );
            }
            await server.Completion;
            await runtime.DisposeAsync( );
            Directory.Delete( directory, recursive: true );
        }
    }
}
