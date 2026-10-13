namespace CommandDeck;

// One background thread reads every root's inotify instance. It sleeps in
// poll(2) until the kernel has events or disposal wakes it; nothing polls on
// a timer.
internal sealed class InotifyWatchLoop : IDisposable
{
    private const int ReadBufferBytes = 64 * 1024;

    private readonly IInotifySystem _system;
    private readonly InotifyTreeWatcher[] _watchers;
    private readonly int _wake;
    private readonly Thread _thread;
    private volatile bool _stopping;

    public InotifyWatchLoop( IInotifySystem system, IReadOnlyList<InotifyTreeWatcher> watchers )
    {
        _system = system;
        _watchers = [.. watchers];
        _wake = system.CreateWake( );
        if ( _wake < 0 )
        {
            throw new IOException( $"Could not create the watcher wake-up descriptor (errno {-_wake})." );
        }

        _thread = new Thread( Run )
        {
            IsBackground = true,
            Name = "CommandDeck inotify"
        };
        _thread.Start( );
    }

    public void Dispose( )
    {
        _stopping = true;
        _system.Wake( _wake );
        _thread.Join( );
        _system.Close( _wake );
    }

    private void Run( )
    {
        int[] descriptors = [.. _watchers.Select( static watcher => watcher.Descriptor ), _wake];
        var readable = new bool[descriptors.Length];
        var buffer = new byte[ReadBufferBytes];
        while ( !_stopping )
        {
            if ( _system.Poll( descriptors, readable ) < 0 || readable[^1] || _stopping )
            {
                return;
            }

            for ( var index = 0; index < _watchers.Length; index++ )
            {
                if ( readable[index] )
                {
                    Drain( _watchers[index], buffer );
                }
            }
        }
    }

    private void Drain( InotifyTreeWatcher watcher, byte[] buffer )
    {
        while ( !_stopping )
        {
            var count = _system.Read( watcher.Descriptor, buffer );
            if ( count <= 0 )
            {
                return;
            }

            try
            {
                watcher.ProcessEvents( buffer.AsSpan( 0, count ) );
            }
            catch ( Exception )
            {
                // A failure while applying events must not stop watching.
                // Treat the read as lost so the root is reconciled.
                watcher.ReportLostEvents( );
            }
        }
    }
}
