#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:property EnableNETAnalyzers=false

using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;


sealed class Client : IDisposable
{
    readonly Socket socket = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
    readonly StreamReader reader;
    readonly StreamWriter writer;
    int sequence;
    public Client( string address )
    {
        socket.Connect( new UnixDomainSocketEndPoint( address ) );
        var stream = new NetworkStream( socket, false );
        reader = new StreamReader( stream );
        writer = new StreamWriter( stream, new UTF8Encoding( false ) ) { AutoFlush = true };
    }
    public bool HasData => socket.Available > 0;
    public async Task<int> Send( string operation, JsonObject fields )
    {
        var request = ( JsonObject ) fields.DeepClone( );
        request["version"] = 3;
        request["requestId"] = ++sequence;
        request["operation"] = operation;
        await writer.WriteLineAsync( request.ToJsonString( ) );
        return sequence;
    }
    public async Task<List<JsonNode>> Done( int request )
    {
        var result = new List<JsonNode>( );
        using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 20 ) );
        while ( true )
        {
            var messageText = await reader.ReadLineAsync( timeout.Token );
            if ( messageText is null )
            {
                throw new IOException( "Daemon disconnected" );
            }

            var message = JsonNode.Parse( messageText )!;
            result.Add( message );
            if ( message["requestId"]!.GetValue<int>( ) == request && message["type"]!.GetValue<string>( ) == "done" )
            {
                return result;
            }
        }
    }
    public async Task<List<JsonNode>> Call( string operation, JsonObject fields, bool error = false )
    {
        var messages = await Done( await Send( operation, fields ) );
        Journey.Check( messages.Any( message => message["type"]!.GetValue<string>( ) == "error" ) == error, string.Join( '\n', messages ) );
        return messages;
    }
    public static void NoErrors( List<JsonNode> messages ) => Journey.Check( messages.All( message => message["type"]!.GetValue<string>( ) != "error" ), string.Join( '\n', messages ) );
    public void Dispose( )
    {
        writer.Dispose( );
        reader.Dispose( );
        socket.Dispose( );
    }
}

static class Wayland
{
    public static async Task Run( string host, string address, string clipboard, string root, bool failure = false )
    {
        var config = Path.Combine( root, failure ? "wayland-failure" : "wayland" );
        Directory.CreateDirectory( config );
        var fixture = Path.Combine( config, "shell.qml" );
        File.Copy( Path.Combine( Journey.Repo, "tests/CommandDeck.ExtensionJourney/shell.qml" ), fixture );
        await Journey.Exec( "cp", "-r", Path.Combine( Journey.Repo, "qml", "common" ), Path.Combine( config, "plugin" ) );
        var start = new ProcessStartInfo( "qs" ) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach ( var argument in new[] { "--no-color", "-p", fixture } )
        {
            start.ArgumentList.Add( argument );
        }
        start.Environment.Remove( "QT_QPA_PLATFORM" );
        start.Environment["COMMANDDECK_TEST_HOST"] = host;
        start.Environment["COMMANDDECK_TEST_SOCKET"] = address;
        using var shell = Process.Start( start )!;
        var output = shell.StandardOutput.ReadToEndAsync( );
        var errors = shell.StandardError.ReadToEndAsync( );
        var cursor = JsonNode.Parse( await Journey.Exec( "hyprctl", "cursorpos", "-j" ) )!;
        DesktopInput? input = null;
        try
        {
            async Task<string> Ipc( string method ) => await Journey.Exec( "qs", "ipc", "--pid", shell.Id.ToString( ), "call", "extension-journey", method );
            async Task<JsonNode> State( Func<JsonNode, bool> predicate, string label )
            {
                JsonNode? last = null;
                try
                {
                    await Journey.Wait( async ( ) =>
                    {
                        try
                        {
                            last = JsonNode.Parse( await Ipc( "state" ) );
                            return predicate( last! );
                        }
                        catch ( InvalidOperationException )
                        {
                            return false;
                        }
                    }, label );
                }
                catch
                {
                    throw new InvalidOperationException( label + ": " + last );
                }
                return last!;
            }
            await State( _ => true, "Fixture startup" );
            input = new DesktopInput( );
            await Ipc( "open" );
            await State( s => s["opened"]!.GetValue<bool>( ) && s["searchFocused"]!.GetValue<bool>( ), "Launcher initial focus" );
            await Task.Delay( 400 );
            await input.Type( failure ? "crash" : "uuid" );
            await Task.Delay( 500 );
            for ( var index = 0; index < 50; index++ )
            {
                var candidate = JsonNode.Parse( await Ipc( "state" ) )!;
                if ( candidate["result"]?["kind"]?.GetValue<string>( ) == "extension-command" )
                {
                    break;
                }
                await input.Key( 108 );
            }
            await State( s => s["result"]?["kind"]?.GetValue<string>( ) == "extension-command", "Find UUID Tools" );
            await input.Key( 28 );
            if ( failure )
            {
                var failed = await State( s => s["restartFocused"]!.GetValue<bool>( ) && s["error"]!.GetValue<string>( ).Length > 0 && !s["busy"]!.GetValue<bool>( ), "Crash recovery focus" );
                await input.Key( 57 );
                await State( s => s["sessionId"]!.GetValue<string>( ) != failed["sessionId"]!.GetValue<string>( ) && s["restartFocused"]!.GetValue<bool>( ) && !s["busy"]!.GetValue<bool>( ), "Explicit restart fails safely" );
                await input.Key( 15 );
                await State( s => s["disableFocused"]!.GetValue<bool>( ), "Disable recovery focus" );
                await input.Key( 28 );
                await State( s => !s["extensionOpen"]!.GetValue<bool>( ) && s["searchFocused"]!.GetValue<bool>( ), "Disable returns focus" );
                Console.WriteLine( "PASS repository Wayland failure recovery: crash, explicit Restart, Disable, focus restoration." );
                return;
            }
            var initial = await State( s => s["sessionId"]!.GetValue<string>( ).Length > 0 && !s["busy"]!.GetValue<bool>( ) && s["viewFocused"]!.GetValue<bool>( ) && s["count"]!.GetValue<int>( ) == 8, "Open and focus UUIDs" );
            await input.Key( 108 );
            var selected = await State( s => s["selectedId"]!.GetValue<string>( ) != initial["selectedId"]!.GetValue<string>( ), "Arrow selection" );
            await input.Key( 28 );
            await Journey.Wait( async ( ) => File.Exists( clipboard ) && await File.ReadAllTextAsync( clipboard ) == selected["selectedId"]!.GetValue<string>( ), "Keyboard copy" );
            await input.Type( selected["selectedId"]!.GetValue<string>( )[..1] );
            await State( s => s["viewQuery"]!.GetValue<string>( ).Length == 1, "Immediate filtering" );
            await input.Key( 1 );
            await State( s => s["extensionOpen"]!.GetValue<bool>( ) && s["viewQuery"]!.GetValue<string>( ) == "", "Escape clears query" );
            await input.Key( 15 );
            await input.Key( 15 );
            await State( s => s["primaryFocused"]!.GetValue<bool>( ), "Copy Tab focus" );
            await input.Key( 15 );
            await State( s => s["detailsFocused"]!.GetValue<bool>( ), "Details Tab focus" );
            await input.Key( 57 );
            await State( s => s["kind"]!.GetValue<string>( ) == "detail" && s["detailFocused"]!.GetValue<bool>( ) && !s["busy"]!.GetValue<bool>( ), "Detail initial focus" );
            await input.Key( 28 );
            await Journey.Wait( async ( ) => await File.ReadAllTextAsync( clipboard ) == selected["selectedId"]!.GetValue<string>( ), "Detail keyboard copy" );
            await State( s => !s["busy"]!.GetValue<bool>( ), "Detail copy completion" );
            await input.Key( 1 );
            await State( s => s["kind"]!.GetValue<string>( ) == "list" && s["detailsFocused"]!.GetValue<bool>( ) && !s["busy"]!.GetValue<bool>( ), "Back restores invoking action" );
            await input.Key( 15 );
            await State( s => s["callbackFocused"]!.GetValue<bool>( ), "Callback Tab focus" );
            await input.Key( 57 );
            var generated = await State( s => s["revision"]!.GetValue<int>( ) > initial["revision"]!.GetValue<int>( ) && !s["busy"]!.GetValue<bool>( ) && s["callbackFocused"]!.GetValue<bool>( ), "Regenerate callback retains focus" );
            await input.Click( generated["copy"]! );
            await Journey.Wait( async ( ) => await File.ReadAllTextAsync( clipboard ) == generated["selectedId"]!.GetValue<string>( ), "Mouse copy" );
            await input.Click( generated["regenerate"]! );
            var changed = await State( s => s["revision"]!.GetValue<int>( ) > generated["revision"]!.GetValue<int>( ) && !s["busy"]!.GetValue<bool>( ), "Mouse regenerate" );
            await input.Click( changed["details"]! );
            var detail = await State( s => s["kind"]!.GetValue<string>( ) == "detail" && !s["busy"]!.GetValue<bool>( ), "Mouse details" );
            await input.Click( detail["detailCopy"]! );
            await State( s => !s["busy"]!.GetValue<bool>( ), "Mouse detail copy" );
            await input.Click( detail["detailBack"]! );
            changed = await State( s => s["kind"]!.GetValue<string>( ) == "list" && !s["busy"]!.GetValue<bool>( ), "Mouse detail Back" );
            await input.Click( changed["nextPage"]! );
            var page = await State( s => s["revision"]!.GetValue<int>( ) > changed["revision"]!.GetValue<int>( ) && !s["busy"]!.GetValue<bool>( ), "Next page" );
            Journey.Check( page["count"]!.GetValue<int>( ) == 8 && page["selectedId"]!.GetValue<string>( ) != changed["selectedId"]!.GetValue<string>( ), "Page replacement selection" );
            await input.Click( page["previousPage"]! );
            changed = await State( s => s["revision"]!.GetValue<int>( ) > page["revision"]!.GetValue<int>( ) && !s["busy"]!.GetValue<bool>( ), "Previous page" );
            await input.Click( changed["back"]! );
            await State( s => !s["extensionOpen"]!.GetValue<bool>( ) && s["searchFocused"]!.GetValue<bool>( ), "Back focus restoration" );
            await State( s => s["result"]?["kind"]?.GetValue<string>( ) == "extension-command", "Rediscover" );
            await input.Key( 28 );
            await State( s => s["sessionId"]!.GetValue<string>( ).Length > 0 && s["sessionId"]!.GetValue<string>( ) != initial["sessionId"]!.GetValue<string>( ) && s["viewFocused"]!.GetValue<bool>( ) && !s["busy"]!.GetValue<bool>( ), "Reopen fresh session" );
            await input.Key( 1 );
            await State( s => !s["extensionOpen"]!.GetValue<bool>( ) && s["searchFocused"]!.GetValue<bool>( ), "Escape restoration" );
            Console.WriteLine( "PASS repository Wayland: find/open/filter/arrows/copy/Tab/regenerate/mouse/Back/reopen/Escape." );
        }
        finally
        {
            if ( !shell.HasExited )
            {
                shell.Kill( true );
                await shell.WaitForExitAsync( );
            }
            Directory.CreateDirectory( Path.Combine( Journey.Repo, "artifacts/extensions" ) );
            await File.WriteAllTextAsync( Path.Combine( Journey.Repo, failure ? "artifacts/extensions/wayland-failure.log" : "artifacts/extensions/wayland.log" ), await output + await errors );
            if ( input is not null )
            {
                try
                {
                    await input.Move( cursor );
                }
                finally
                {
                    input.Dispose( );
                }
            }
        }
    }
}

sealed class DesktopInput : IDisposable
{
    readonly FileStream device = new( "/dev/uinput", FileMode.Open, FileAccess.Write, FileShare.ReadWrite );
    readonly int descriptor;
    public DesktopInput( )
    {
        descriptor = device.SafeFileHandle.DangerousGetHandle( ).ToInt32( );
        Set( 0x40045564, 1 );
        Set( 0x40045564, 2 );
        Set( 0x40045566, 0 );
        Set( 0x40045566, 1 );
        foreach ( var key in Enumerable.Range( 1, 120 ).Append( 272 ) )
        {
            Set( 0x40045565, key );
        }
        var data = new byte[1116];
        Encoding.ASCII.GetBytes( "commanddeck-acceptance-input" ).CopyTo( data, 0 );
        BitConverter.GetBytes( ( ushort ) 3 ).CopyTo( data, 80 );
        device.Write( data );
        device.Flush( );
        Set( 0x5501, 0 );
        Thread.Sleep( 300 );
    }
    void Set( uint request, int value )
    {
        if ( Ioctl( descriptor, request, value ) < 0 )
        {
            throw new IOException( $"uinput ioctl {request:x}: {Marshal.GetLastPInvokeError( )}" );
        }
    }
    void Event( ushort type, ushort code, int value )
    {
        Span<byte> data = stackalloc byte[24];
        data.Clear( );
        BitConverter.TryWriteBytes( data[16..], type );
        BitConverter.TryWriteBytes( data[18..], code );
        BitConverter.TryWriteBytes( data[20..], value );
        device.Write( data );
        device.Flush( );
    }
    public async Task Key( ushort code )
    {
        Event( 1, code, 1 );
        Event( 0, 0, 0 );
        await Task.Delay( 35 );
        Event( 1, code, 0 );
        Event( 0, 0, 0 );
        await Task.Delay( 100 );
    }
    public async Task Type( string text )
    {
        const string letters = "qwertyuiopasdfghjklzxcvbnm";
        ushort[] keys = [16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 30, 31, 32, 33, 34, 35, 36, 37, 38, 44, 45, 46, 47, 48, 49, 50];
        foreach ( var letter in text )
        {
            await Key( char.IsDigit( letter ) ? ( ushort ) (letter == '0' ? 11 : letter - '1' + 2) : keys[letters.IndexOf( letter )] );
        }
    }
    public async Task Move( JsonNode point )
    {
        // Relative hardware events with feedback work across compositor command syntaxes.
        for ( var attempt = 0; attempt < 240; attempt++ )
        {
            var current = JsonNode.Parse( await Journey.Exec( "hyprctl", "cursorpos", "-j" ) )!;
            var dx = point["x"]!.GetValue<int>( ) - current["x"]!.GetValue<int>( );
            var dy = point["y"]!.GetValue<int>( ) - current["y"]!.GetValue<int>( );
            if ( Math.Abs( dx ) <= 2 && Math.Abs( dy ) <= 2 )
            {
                return;
            }
            Event( 2, 0, Math.Clamp( dx, -40, 40 ) );
            Event( 2, 1, Math.Clamp( dy, -40, 40 ) );
            Event( 0, 0, 0 );
            await Task.Delay( 20 );
        }
        throw new InvalidOperationException( "Could not position acceptance pointer" );
    }
    public async Task Click( JsonNode point )
    {
        await Move( point );
        await Task.Delay( 50 );
        await Key( 272 );
    }
    public void Dispose( )
    {
        Set( 0x5502, 0 );
        device.Dispose( );
    }
    [DllImport( "libc", EntryPoint = "ioctl", SetLastError = true )] static extern int Ioctl( int fd, uint request, int value );
}
