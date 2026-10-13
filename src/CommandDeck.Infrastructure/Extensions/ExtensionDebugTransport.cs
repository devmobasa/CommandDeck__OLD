using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CommandDeck;

/// <summary>Relays GDB packets from a private host socket to one confined pseudo-terminal.</summary>
public sealed partial class ExtensionDebugTransport : IDisposable
{
    private readonly SafeFileHandle _master;
    private readonly SafeFileHandle _slave;
    private readonly Socket _listener;
    public string SlavePath { get; }
    public ExtensionDebugTransport( string socketPath )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new PlatformNotSupportedException( );
        }
        byte[] name = new byte[128];
        if ( OpenPty( out var master, out var slave, name, 0, 0 ) != 0 )
        {
            throw new IOException( "Cannot create debugger terminal." );
        }
        _master = new( master, true );
        _slave = new( slave, true );
        _listener = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        try
        {
            if ( Fcntl( _master, LinuxConstants.SetDescriptorFlags, LinuxConstants.DescriptorCloseOnExec ) < 0 || Fcntl( _slave, LinuxConstants.SetDescriptorFlags, LinuxConstants.DescriptorCloseOnExec ) < 0 || Fcntl( _master, LinuxConstants.SetStatusFlags, LinuxConstants.ONonBlocking ) < 0 )
            {
                throw new IOException( "Cannot configure debugger terminal." );
            }
            SlavePath = System.Text.Encoding.UTF8.GetString( name.AsSpan( 0, Array.IndexOf( name, ( byte ) 0 ) ) );
            _listener.Bind( new UnixDomainSocketEndPoint( socketPath ) );
            File.SetUnixFileMode( socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite );
            _listener.Listen( 1 );
        }
        catch
        {
            Dispose( );
            throw;
        }
    }
    public async Task RelayAsync( CancellationToken cancellationToken )
    {
        using var socket = await _listener.AcceptAsync( cancellationToken ).ConfigureAwait( false );
        _listener.Dispose( );
        using var stream = new NetworkStream( socket, false );
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        var read = ReadTerminalAsync( stream, lifetime.Token );
        var write = WriteTerminalAsync( stream, lifetime.Token );
        await Task.WhenAny( read, write ).ConfigureAwait( false );
        await lifetime.CancelAsync( ).ConfigureAwait( false );
        try
        {
            await Task.WhenAll( read, write ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested ) { }
    }
    private async Task ReadTerminalAsync( Stream target, CancellationToken token )
    {
        byte[] buffer = new byte[8192];
        while ( !token.IsCancellationRequested )
        {
            var count = Read( _master, buffer, ( nuint ) buffer.Length );
            if ( count > 0 )
            {
                await target.WriteAsync( buffer.AsMemory( 0, ( int ) count ), token ).ConfigureAwait( false );
            }
            else if ( count == 0 )
            {
                return;
            }
            else if ( Marshal.GetLastPInvokeError( ) is LinuxConstants.TryAgain or LinuxConstants.InputOutputError )
            {
                await Task.Delay( 10, token ).ConfigureAwait( false );
            }
            else
            {
                throw new IOException( "Debugger terminal read failed." );
            }
        }
    }
    private async Task WriteTerminalAsync( Stream source, CancellationToken token )
    {
        byte[] buffer = new byte[8192];
        int count;
        while ( (count = await source.ReadAsync( buffer, token ).ConfigureAwait( false )) > 0 )
        {
            var offset = 0;
            while ( offset < count )
            {
                token.ThrowIfCancellationRequested( );
                var chunk = buffer.AsSpan( offset, count - offset ).ToArray( );
                var written = Write( _master, chunk, ( nuint ) chunk.Length );
                if ( written > 0 )
                {
                    offset += ( int ) written;
                }
                else if ( written < 0 && Marshal.GetLastPInvokeError( ) == LinuxConstants.TryAgain )
                {
                    await Task.Delay( 10, token ).ConfigureAwait( false );
                }
                else
                {
                    throw new IOException( "Debugger terminal write failed." );
                }
            }
        }
    }
    public static async Task<int> ConnectAsync( string socketPath, CancellationToken token )
    {
        using var socket = new Socket( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        await socket.ConnectAsync( new UnixDomainSocketEndPoint( socketPath ), token ).ConfigureAwait( false );
        using var stream = new NetworkStream( socket, false );
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( token );
        var read = stream.CopyToAsync( Console.OpenStandardOutput( ), lifetime.Token );
        var write = Console.OpenStandardInput( ).CopyToAsync( stream, lifetime.Token );
        await Task.WhenAny( read, write ).ConfigureAwait( false );
        await lifetime.CancelAsync( ).ConfigureAwait( false );
        return 0;
    }
    public void Dispose( )
    {
        _listener.Dispose( );
        _master.Dispose( );
        _slave.Dispose( );
    }
    [LibraryImport( "libc", EntryPoint = "openpty", SetLastError = true )]
    private static partial int OpenPty( out int master, out int slave, [Out] byte[] name, nint termios, nint winsize );
    [LibraryImport( "libc", EntryPoint = "fcntl", SetLastError = true )]
    private static partial int Fcntl( SafeFileHandle fd, int command, int value );
    [LibraryImport( "libc", EntryPoint = "read", SetLastError = true )]
    private static partial nint Read( SafeFileHandle fd, [Out] byte[] buffer, nuint count );
    [LibraryImport( "libc", EntryPoint = "write", SetLastError = true )]
    private static partial nint Write( SafeFileHandle fd, byte[] buffer, nuint count );
}
