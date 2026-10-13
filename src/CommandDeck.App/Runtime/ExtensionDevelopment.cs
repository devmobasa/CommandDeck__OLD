namespace CommandDeck;

/// <summary>Ephemeral, single-artifact author leases and bounded per-session diagnostics.</summary>
internal sealed class ExtensionDevelopment : IDisposable
{
    private readonly object _gate = new( );
    private readonly Dictionary<string, DebugLease> _leases = new( StringComparer.Ordinal );
    private readonly Dictionary<string, Queue<ExtensionDiagnostic>> _diagnostics = new( StringComparer.Ordinal );
    private readonly Dictionary<string, long> _diagnosticOrder = new( StringComparer.Ordinal );
    private long _recordSequence;

    public ExtensionDebugState Arm( ExtensionInstallation installation, string command, string digest, int seconds )
    {
        if ( installation.Origin != ExtensionOrigins.Development || installation.Removed || !installation.Enabled || installation.Digest != digest ||
            !installation.Manifest.Commands.Any( value => value.Id == command ) || seconds is < 1 or > 300 ||
            ExtensionRuntimeSupport.Select( installation.Manifest )?.Deployment != CommandDeck.ExtensionProtocol.ExtensionTargets.NativeAot )
        {
            throw new InvalidOperationException( "Select an enabled development artifact and declared command, with its current digest and a 1–300 second lease." );
        }
        lock ( _gate )
        {
            if ( _leases.Remove( installation.Key, out var old ) )
            {
                old.Dispose( );
            }
            var lease = new DebugLease( command, digest, seconds );
            _leases.Add( installation.Key, lease );
            return lease.State;
        }
    }

    public DebugLease? Take( ExtensionInstallation installation, string command, bool visibleDebugLease )
    {
        lock ( _gate )
        {
            if ( !_leases.TryGetValue( installation.Key, out var lease ) || lease.State.CommandId != command )
            {
                return null;
            }
            if ( lease.Expired || lease.State.Digest != installation.Digest )
            {
                _leases.Remove( installation.Key );
                lease.Dispose( );
                return null;
            }
            if ( lease.State.Consumed )
            {
                return null;
            }
            if ( !visibleDebugLease )
            {
                throw new InvalidOperationException( "This renderer cannot display debug leases. Update it or revoke the lease before opening." );
            }
            lease.Consume( );
            return lease;
        }
    }
    public ExtensionDebugState? State( string key )
    {
        lock ( _gate )
        {
            return _leases.TryGetValue( key, out var lease ) && !lease.Expired ? lease.State : null;
        }
    }
    public void Revoke( string? key )
    {
        lock ( _gate )
        {
            foreach ( var name in _leases.Keys.Where( name => key is null || name == key ).ToArray( ) )
            {
                if ( _leases.Remove( name, out var lease ) )
                {
                    lease.Dispose( );
                }
            }
        }
    }
    public void Record( string key, ExtensionDiagnostic value )
    {
        lock ( _gate )
        {
            if ( !_diagnostics.TryGetValue( key, out var entries ) )
            {
                if ( _diagnostics.Count >= 128 )
                {
                    var oldest = _diagnosticOrder.MinBy( entry => entry.Value ).Key;
                    _diagnostics.Remove( oldest );
                    _diagnosticOrder.Remove( oldest );
                }
                _diagnostics.Add( key, entries = new( ) );
            }
            _diagnosticOrder[key] = ++_recordSequence;
            entries.Enqueue( value with
            {
                Stderr = value.Stderr is { Length: > 4096 } text ? text[^4096..] : value.Stderr
            } );
            while ( entries.Count > 8 )
            {
                entries.Dequeue( );
            }
        }
    }
    public ExtensionDiagnostic[] Read( string key, bool stderr )
    {
        lock ( _gate )
        {
            return _diagnostics.TryGetValue( key, out var entries ) ? entries.Select( value => stderr ? value : value with { Stderr = null } ).ToArray( ) : [];
        }
    }
    public void Dispose( )
    {
        Revoke( null );
        lock ( _gate )
        {
            _diagnostics.Clear( );
            _diagnosticOrder.Clear( );
        }
    }

    internal sealed class DebugLease : IDisposable
    {
        private readonly CancellationTokenSource _expiry;
        private readonly string _directory;
        private readonly CancellationToken _token;
        private int _disposed;
        public DebugLease( string command, string digest, int seconds )
        {
            _directory = Directory.CreateTempSubdirectory( "commanddeck-debug-" ).FullName;
            if ( OperatingSystem.IsLinux( ) )
            {
                File.SetUnixFileMode( _directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            }
            State = new( command, digest, DateTimeOffset.UtcNow.AddSeconds( seconds ), Path.Combine( _directory, "debug.sock" ), false );
            _expiry = new( TimeSpan.FromSeconds( seconds ) );
            _token = _expiry.Token;
            _token.Register( DeleteEndpoint );
        }
        public ExtensionDebugState State { get; private set; }
        public CancellationToken Token => _token;
        public bool Expired => _token.IsCancellationRequested;
        public void Consume( ) => State = State with { Consumed = true };
        public void Dispose( )
        {
            if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
            {
                return;
            }
            _expiry.Cancel( );
            _expiry.Dispose( );
        }
        private void DeleteEndpoint( )
        {
            try
            {
                Directory.Delete( _directory, true );
            }
            catch ( IOException ) { }
        }
    }
}
