using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class DesktopPortalFixture : IPathMethodHandler, IDisposable
    {
        private readonly Process _bus;
        private readonly DBusConnection _connection;
        public string Address { get; }
        public string Path => "/org/freedesktop/portal/desktop";
        public bool HandlesChildPaths => true;
        public SafeFileHandle? ReceivedFile { get; private set; }
        public int OpenCount { get; private set; }
        public uint Response { get; set; }

        private DesktopPortalFixture( Process bus, string address )
        {
            _bus = bus;
            Address = address;
            _connection = new DBusConnection( address );
        }

        public static async Task<DesktopPortalFixture> StartAsync( )
        {
            ProcessStartInfo start = new( "dbus-daemon" )
            {
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add( "--session" );
            start.ArgumentList.Add( "--nofork" );
            start.ArgumentList.Add( "--print-address=1" );
            var bus = Process.Start( start );
            if ( bus is null )
            {
                throw new InvalidOperationException( "Could not start the private test bus." );
            }

            try
            {
                var address = await bus.StandardOutput.ReadLineAsync( ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
                if ( string.IsNullOrEmpty( address ) )
                {
                    throw new InvalidOperationException( "The private test bus returned no address." );
                }

                var fixture = new DesktopPortalFixture( bus, address );
                try
                {
                    await fixture._connection.ConnectAsync( );
                    fixture._connection.AddMethodHandler( fixture );
                    await fixture._connection.RequestNameAsync( "org.freedesktop.portal.Desktop", RequestNameOptions.None );
                    return fixture;
                }
                catch
                {
                    fixture._connection.Dispose( );
                    throw;
                }
            }
            catch
            {
                bus.Kill( );
                bus.Dispose( );
                throw;
            }
        }

        public ValueTask HandleMethodAsync( MethodContext context )
        {
            if ( context.Request.MemberAsString != "OpenFile" )
            {
                context.ReplyUnknownMethodError( );
                return ValueTask.CompletedTask;
            }

            var reader = context.Request.GetBodyReader( );
            Equal( string.Empty, reader.ReadString( ) );
            ReceivedFile?.Dispose( );
            ReceivedFile = reader.ReadHandle<SafeFileHandle>( );
            var options = reader.ReadDictionaryOfStringToVariantValue( );
            var token = options["handle_token"].GetString( );
            var sender = context.Request.SenderAsString!;
            var requestPath = Path + "/request/" + sender[1..].Replace( '.', '_' ) + "/" + token;
            OpenCount++;

            using var reply = context.CreateReplyWriter( "o" );
            reply.WriteObjectPath( requestPath );
            context.Reply( reply.CreateMessage( ) );

            using var signal = _connection.GetMessageWriter( );
            signal.WriteSignalHeader( sender, requestPath, "org.freedesktop.portal.Request", "Response", "ua{sv}" );
            signal.WriteUInt32( Response );
            signal.WriteDictionary( new Dictionary<string, VariantValue>( ) );
            _connection.TrySendMessage( signal.CreateMessage( ) );
            return ValueTask.CompletedTask;
        }

        public string ReadAfterLaunch( )
        {
            // Deliberately read only after OpenAsync returned and its local duplicate was disposed.
            return File.ReadAllText( $"/proc/{Environment.ProcessId}/fd/{ReceivedFile!.DangerousGetHandle( )}" );
        }

        public void Dispose( )
        {
            ReceivedFile?.Dispose( );
            _connection.Dispose( );
            _bus.Kill( );
            _bus.WaitForExit( );
            _bus.Dispose( );
        }
    }
}
