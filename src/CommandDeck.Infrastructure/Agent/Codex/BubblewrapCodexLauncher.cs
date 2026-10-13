using System.Diagnostics;

namespace CommandDeck;

public sealed class BubblewrapCodexLauncher( string bubblewrapPath ) : IAgentProcessLauncher
{
    private const string SandboxBinaryPath = "/opt/commanddeck/codex";

    public static IReadOnlyList<string> BuildArguments( CodexLaunchRequest request )
    {
        ArgumentNullException.ThrowIfNull( request );
        var home = request.RuntimeHome;
        List<string> arguments =
        [
            "--unshare-all", "--share-net", "--die-with-parent", "--new-session",
            "--ro-bind", "/usr", "/usr",
            "--ro-bind", "/etc", "/etc",
            "--symlink", "usr/lib", "/lib",
            "--symlink", "usr/lib64", "/lib64",
            "--symlink", "usr/bin", "/bin",
            "--symlink", "usr/sbin", "/sbin",
            "--proc", "/proc",
            "--dev", "/dev",
            "--tmpfs", "/tmp",
            "--tmpfs", "/var",
            "--tmpfs", "/home",
            "--tmpfs", "/root",
            "--tmpfs", "/run",
            "--tmpfs", "/opt"
        ];
        if ( Directory.Exists( "/run/systemd/resolve" ) )
        {
            arguments.AddRange( ["--ro-bind", "/run/systemd/resolve", "/run/systemd/resolve"] );
        }
        arguments.AddRange(
        [
            "--dir", home,
            "--bind", home, home,
            "--ro-bind", request.BinaryPath, SandboxBinaryPath,
        ] );
        // Bind the bundled code-mode host next to codex so Code Mode can spawn
        // it inside the sandbox. Codex resolves the host relative to its own
        // path, which is /opt/commanddeck/codex here. Its tool calls remain
        // mediated (item/tool/call); the host inherits this locked sandbox.
        var binaryDir = Path.GetDirectoryName( request.BinaryPath );
        if ( binaryDir is not null )
        {
            var hostSource = Path.Combine( binaryDir, "codex-code-mode-host" );
            if ( File.Exists( hostSource ) )
            {
                arguments.AddRange( ["--ro-bind", hostSource, "/opt/commanddeck/codex-code-mode-host"] );
            }
        }
        arguments.AddRange(
        [
            "--chdir", home,
            "--clearenv",
            "--setenv", "HOME", home,
            "--setenv", "CODEX_HOME", home,
            "--setenv", "XDG_RUNTIME_DIR", Path.Combine(home, "xdg"),
            "--setenv", "XDG_CONFIG_HOME", Path.Combine(home, "xdg", "config"),
            "--setenv", "XDG_CACHE_HOME", Path.Combine(home, "xdg", "cache"),
            "--setenv", "XDG_DATA_HOME", Path.Combine(home, "xdg", "data"),
            "--setenv", "XDG_STATE_HOME", Path.Combine(home, "xdg", "state"),
            "--setenv", "PATH", "/usr/bin:/bin",
            "--setenv", "LANG", "C.UTF-8",
            "--setenv", "TERM", "dumb",
            "--",
            SandboxBinaryPath
        ] );
        arguments.AddRange( CodexArgv.AppServerStdio );
        CodexArgv.AssertSafe( arguments );
        return arguments;
    }

    public ValueTask<IAgentProcessHandle> LaunchAsync(
        CodexLaunchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( request );
        if ( !File.Exists( bubblewrapPath ) )
        {
            throw new AiOperationException(
                AiErrorCategories.Unavailable,
                safeSummary: "Bubblewrap is not installed; the sandboxed agent cannot start." );
        }
        Directory.CreateDirectory( Path.Combine( request.RuntimeHome, "xdg" ) );
        var arguments = BuildArguments( request );
        ProcessStartInfo startInfo = new( bubblewrapPath )
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = request.RuntimeHome,
            CreateNoWindow = true
        };
        startInfo.Environment.Clear( );
        startInfo.Environment["PATH"] = "/usr/bin:/bin";
        startInfo.Environment["LANG"] = "C.UTF-8";
        foreach ( var argument in arguments )
        {
            startInfo.ArgumentList.Add( argument );
        }
        Process process = new( )
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        try
        {
            if ( !process.Start( ) )
            {
                throw new AiOperationException(
                    AiErrorCategories.Unavailable,
                    safeSummary: "The sandboxed agent process could not start." );
            }
        }
        catch ( Exception exception ) when ( exception is System.ComponentModel.Win32Exception or InvalidOperationException )
        {
            process.Dispose( );
            throw new AiOperationException(
                AiErrorCategories.Unavailable,
                safeSummary: "The sandboxed agent process could not start." );
        }
        cancellationToken.ThrowIfCancellationRequested( );
        return ValueTask.FromResult<IAgentProcessHandle>( new ProcessAgentHandle( process ) );
    }
}

public sealed class ProcessAgentHandle : IAgentProcessHandle
{
    private const int MaximumDiagnosticBytes = 8 * 1024;
    private readonly Process _process;
    private readonly Task _stderrDrain;
    private readonly System.Text.StringBuilder _diagnostics = new( );
    private int _disposed;

    public ProcessAgentHandle( Process process )
    {
        _process = process;
        Transport = new LineDelimitedTransport(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream,
            AgentLimits.MaximumFrameBytes );
        Exited = process.WaitForExitAsync( );
        _stderrDrain = Task.Run( DrainStandardErrorAsync );
    }

    public IJsonRpcTransport Transport { get; }

    public Task Exited { get; }

    public string Diagnostics
    {
        get
        {
            lock ( _diagnostics )
            {
                return _diagnostics.ToString( );
            }
        }
    }

    public async ValueTask StopAsync( CancellationToken cancellationToken )
    {
        try
        {
            if ( !_process.HasExited )
            {
                _process.StandardInput.Close( );
            }
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
        }
        using var grace = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        grace.CancelAfter( TimeSpan.FromSeconds( 3 ) );
        try
        {
            await _process.WaitForExitAsync( grace.Token ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException )
        {
            TryKill( );
        }
        TryKill( );
    }

    private void TryKill( )
    {
        try
        {
            if ( !_process.HasExited )
            {
                _process.Kill( entireProcessTree: true );
            }
        }
        catch ( InvalidOperationException )
        {
        }
        catch ( System.ComponentModel.Win32Exception )
        {
        }
    }

    private async Task DrainStandardErrorAsync( )
    {
        try
        {
            var buffer = new char[1024];
            while ( true )
            {
                var read = await _process.StandardError.ReadAsync( buffer ).ConfigureAwait( false );
                if ( read <= 0 )
                {
                    return;
                }
                lock ( _diagnostics )
                {
                    if ( _diagnostics.Length < MaximumDiagnosticBytes )
                    {
                        _diagnostics.Append( buffer, 0, Math.Min( read, MaximumDiagnosticBytes - _diagnostics.Length ) );
                    }
                }
            }
        }
        catch ( Exception exception ) when ( exception is IOException or ObjectDisposedException or InvalidOperationException )
        {
        }
    }

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        await StopAsync( CancellationToken.None ).ConfigureAwait( false );
        await Transport.DisposeAsync( ).ConfigureAwait( false );
        try
        {
            await _stderrDrain.WaitAsync( TimeSpan.FromSeconds( 1 ) ).ConfigureAwait( false );
        }
        catch ( TimeoutException )
        {
        }
        _process.Dispose( );
    }
}
