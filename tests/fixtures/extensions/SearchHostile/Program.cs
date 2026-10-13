using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

if ( args is ["descendant"] )
{
    Thread.Sleep( Timeout.Infinite );
    return;
}
await ExtensionApp.RunAsync( args, new Hostile( ) );
sealed class Hostile : IExtensionCommand, IExtensionSearchProvider
{
    public string Id => "idle";
    public string SearchId => "probe";
    public ValueTask OpenAsync( CommandContext context, CancellationToken token ) => Show( context, "Sibling ready", token );
    public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken token ) => Show( context, "Sibling alive", token );
    private static ValueTask Show( CommandContext context, string title, CancellationToken token ) => context.Views.ReplaceAsync( new DetailView { Id = "idle", Title = title, Actions = [ViewAction.Callback( "ping", "Ping" )] }, token );
    public async ValueTask<ExtensionSearchResponse> SearchAsync( SearchContext context, CancellationToken token )
    {
        if ( context.Query == "hang" )
        {
            Process.Start( new ProcessStartInfo( Environment.ProcessPath! ) { ArgumentList = { "descendant" }, UseShellExecute = false } );
            Thread.Sleep( Timeout.Infinite ); // Deliberately ignores cancellation; host must kill the cgroup.
        }
        if ( context.Query.StartsWith( "probe ", StringComparison.Ordinal ) )
        {
            Console.Error.WriteLine( "QUERY-MUST-NOT-ESCAPE " + context.Query );
            var bytes = ExtensionFraming.Serialize( new ExtensionFrame { Id = "e:probe", Method = context.Query[6..], Params = JsonElement.Parse( "{}" ) } );
            await Console.OpenStandardOutput( ).WriteAsync( bytes, CancellationToken.None );
            await Task.Delay( 3000, CancellationToken.None );
        }
        if ( context.Query == "isolation" )
        {
            var mutable = false;
            try
            {
                File.WriteAllText( "/extension/escaped.txt", "query" );
                mutable = true;
            }
            catch ( IOException ) { }
            catch ( UnauthorizedAccessException ) { }
            var network = false;
            try
            {
                using var socket = new Socket( AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp );
                network = true;
            }
            catch ( SocketException ) { }
            var hostHome = Directory.EnumerateFileSystemEntries( "/home" ).Any( path => path != "/home/extension" );
            return new( [new( "isolation", !mutable && !network && !hostHome ? "Isolation passed" : "Isolation FAILED" )] );
        }
        return new( [] );
    }
}
