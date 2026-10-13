using System.Diagnostics;
using System.Text;

namespace CommandDeck;

public sealed class ExtensionProcess : IExtensionProcess
{
    private readonly Process _process;
    private readonly ExtensionLaunchProfile _profile;
    private Task _diagnostics = Task.CompletedTask;
    private int _started;
    private readonly CancellationTokenSource _lifetime = new( );
    private readonly StringBuilder _diagnosticRing = new( );
    private readonly object _gate = new( );
    private int _disposed;

    internal ExtensionProcess( Process process, string unit, string controlGroup, CancellationToken operationToken,
        ExtensionLaunchProfile profile = ExtensionLaunchProfile.Interactive )
    {
        _process = process;
        _profile = profile;
        Unit = unit;
        ControlGroup = controlGroup;
        operationToken.ThrowIfCancellationRequested( );
    }

    internal void StartDiagnostics( )
    {
        if ( Interlocked.Exchange( ref _started, 1 ) == 0 )
        {
            _diagnostics = Task.Run( ReadDiagnosticsAsync, CancellationToken.None );
        }
    }

    public string Unit { get; }
    public string ControlGroup { get; }
    public Stream Input => _process.StandardOutput.BaseStream;
    public Stream Output => _process.StandardInput.BaseStream;
    public Task Exited => _process.WaitForExitAsync( );

    public string ReadDiagnostics( )
    {
        lock ( _gate )
        {
            return _diagnosticRing.ToString( );
        }
    }

    private async Task ReadDiagnosticsAsync( )
    {
        var buffer = new char[2048];
        try
        {
            long windowStart = Environment.TickCount64;
            var characters = 0;
            while ( !_lifetime.IsCancellationRequested )
            {
                var count = await _process.StandardError.ReadAsync( buffer, _lifetime.Token ).ConfigureAwait( false );
                if ( count == 0 )
                {
                    return;
                }
                if ( Environment.TickCount64 - windowStart >= 1000 )
                {
                    characters = 0;
                    windowStart = Environment.TickCount64;
                }
                characters += count;
                if ( characters > 65536 )
                {
                    KillControlGroup( );
                    return;
                }
                lock ( _gate )
                {
                    foreach ( var character in buffer.AsSpan( 0, count ) )
                    {
                        _diagnosticRing.Append( char.IsControl( character ) && character is not '\n' and not '\t' ? '�' : character );
                    }
                    if ( _diagnosticRing.Length > 16384 )
                    {
                        _diagnosticRing.Remove( 0, _diagnosticRing.Length - 16384 );
                    }
                }
            }
        }
        catch ( Exception exception ) when ( exception is IOException or OperationCanceledException or ObjectDisposedException ) { }
    }

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        try
        {
            if ( _profile == ExtensionLaunchProfile.Search )
            {
                // Search owns a short query budget; its slot must not wait for systemd's stop timeout.
                KillControlGroup( );
            }
            else
            {
                // An interactive startup deadline does not own the eventual session shutdown.
                using CancellationTokenSource cleanup = new( TimeSpan.FromSeconds( 3 ) );
                try
                {
                    await ExtensionSupervisor.StopUnitAsync( Unit, cleanup.Token ).ConfigureAwait( false );
                }
                catch ( OperationCanceledException ) when ( cleanup.IsCancellationRequested )
                {
                    KillControlGroup( );
                }
            }
        }
        finally
        {
            await _lifetime.CancelAsync( ).ConfigureAwait( false );
            KillControlGroup( );

            try
            {
                await Task.WhenAll( _process.WaitForExitAsync( ), _diagnostics ).WaitAsync( TimeSpan.FromMilliseconds( 250 ) ).ConfigureAwait( false );
            }
            catch ( TimeoutException ) { }

            _process.Dispose( );
            _lifetime.Dispose( );
            lock ( _gate )
            {
                _diagnosticRing.Clear( );
            }
        }
    }

    private void KillControlGroup( )
    {
        try
        {
            File.WriteAllText( "/sys/fs/cgroup" + ControlGroup + "/cgroup.kill", "1" );
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or System.Security.SecurityException )
        {
            try
            {
                if ( !_process.HasExited )
                {
                    _process.Kill( entireProcessTree: true );
                }
            }
            catch ( InvalidOperationException ) { }
        }
    }
}
