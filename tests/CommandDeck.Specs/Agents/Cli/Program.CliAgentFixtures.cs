using System.Text;
using System.Threading.Channels;

namespace CommandDeck.Specs;

public static partial class Program
{
    /// <summary>A readable stream the spec feeds line by line; Complete() ends it.</summary>
    private sealed class ScriptedOutputStream : Stream
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>( );
        private byte[]? _current;
        private int _offset;

        public void Write( string text ) => _chunks.Writer.TryWrite( Encoding.UTF8.GetBytes( text ) );

        public void WriteLine( string line ) => Write( line + "\n" );

        public void Complete( ) => _chunks.Writer.TryComplete( );

        public override async ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
        {
            while ( _current is null || _offset >= _current.Length )
            {
                if ( !await _chunks.Reader.WaitToReadAsync( cancellationToken ) )
                {
                    return 0;
                }
                if ( _chunks.Reader.TryRead( out var chunk ) )
                {
                    _current = chunk;
                    _offset = 0;
                }
            }
            var count = Math.Min( buffer.Length, _current.Length - _offset );
            _current.AsSpan( _offset, count ).CopyTo( buffer.Span );
            _offset += count;
            return count;
        }

        public override int Read( byte[] buffer, int offset, int count ) =>
            ReadAsync( buffer.AsMemory( offset, count ) ).AsTask( ).GetAwaiter( ).GetResult( );

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException( );
        public override long Position
        {
            get => throw new NotSupportedException( ); set => throw new NotSupportedException( );
        }
        public override void Flush( )
        {
        }
        public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( );
        public override void SetLength( long value ) => throw new NotSupportedException( );
        public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException( );
    }

    /// <summary>Captures what the backend writes to the child's stdin; closing it signals the script.</summary>
    private sealed class ScriptedInputStream : Stream
    {
        private readonly MemoryStream _captured = new( );

        public TaskCompletionSource Closed { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public string Text
        {
            get
            {
                lock ( _captured )
                {
                    return Encoding.UTF8.GetString( _captured.ToArray( ) );
                }
            }
        }

        public override void Write( byte[] buffer, int offset, int count )
        {
            lock ( _captured )
            {
                _captured.Write( buffer, offset, count );
            }
        }

        public override void Close( )
        {
            Closed.TrySetResult( );
            base.Close( );
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException( );
        public override long Position
        {
            get => throw new NotSupportedException( ); set => throw new NotSupportedException( );
        }
        public override void Flush( )
        {
        }
        public override int Read( byte[] buffer, int offset, int count ) => throw new NotSupportedException( );
        public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( );
        public override void SetLength( long value ) => throw new NotSupportedException( );
    }

    private sealed class ScriptedCliProcess : ICliAgentProcess
    {
        private readonly TaskCompletionSource<int> _exit = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public ScriptedOutputStream Stdout { get; } = new( );
        public ScriptedInputStream Stdin { get; } = new( );
        public bool Terminated { get; private set; }
        public Stream Output => Stdout;
        public Stream Input => Stdin;
        public Task<int> Exited => _exit.Task;
        public string Diagnostics { get; set; } = string.Empty;
        public TaskCompletionSource DisposeEntered { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );
        public TaskCompletionSource? BlockDisposal { get; set; }

        public void Exit( int code )
        {
            Stdout.Complete( );
            _exit.TrySetResult( code );
        }

        public void Terminate( )
        {
            Terminated = true;
            Exit( -9 );
        }

        public async ValueTask DisposeAsync( )
        {
            Terminate( );
            DisposeEntered.TrySetResult( );
            if ( BlockDisposal is { } release )
            {
                await release.Task;
            }
        }
    }

    /// <summary>
    /// Replaces Bubblewrap with an in-process script keyed on argv. Every launch is
    /// recorded so the spec can assert the sandbox request shape.
    /// </summary>
    private sealed class ScriptedCliLauncher : ICliAgentLauncher
    {
        public List<CliAgentLaunchRequest> Requests { get; } = [];
        public List<ScriptedCliProcess> Processes { get; } = [];
        public required Func<CliAgentLaunchRequest, ScriptedCliProcess, Task> Script { get; init; }

        public ValueTask<ICliAgentProcess> LaunchAsync( CliAgentLaunchRequest request, CancellationToken cancellationToken )
        {
            Requests.Add( request );
            ScriptedCliProcess process = new( );
            Processes.Add( process );
            _ = Task.Run( ( ) => Script( request, process ), CancellationToken.None );
            return ValueTask.FromResult<ICliAgentProcess>( process );
        }
    }

    private static string CreateFakeCliBinary( string root, string name )
    {
        var path = Path.Combine( root, name );
        File.WriteAllText( path, "#!/bin/sh\necho fake\n" );
        if ( OperatingSystem.IsLinux( ) )
        {
            File.SetUnixFileMode( path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        }
        return path;
    }

    private static AgentOptions CreateCliAgentOptions( string root, string? claude = null, string? cursor = null, string? grok = null ) => new( )
    {
        ClaudeBinaryPath = claude,
        CursorBinaryPath = cursor,
        GrokBinaryPath = grok,
        RuntimeDirectory = Path.Combine( root, "runtime" ),
        StateDirectory = Path.Combine( root, "state" ),
        BubblewrapPath = "/usr/bin/bwrap",
        SystemdRunPath = "/usr/bin/systemd-run"
    };

    private static bool HasArgumentPair( IReadOnlyList<string> argv, string flag, string value )
    {
        for ( var index = 0; index + 1 < argv.Count; index++ )
        {
            if ( argv[index] == flag && argv[index + 1] == value )
            {
                return true;
            }
        }
        return false;
    }
}
