using System.Text;
using System.Text.Json;

namespace CommandDeck;

/// <summary>One isolated projection worker; raw markup is never returned as a preview fallback.</summary>
public sealed class ClipboardHtmlProjectionService( string nativeExecutable, BoundedProcessRunner? runner = null )
{
    private static readonly UTF8Encoding StrictUtf8 = new( false, true );
    private readonly BoundedProcessRunner _run = runner ?? RunAsync;

    public async ValueTask<ClipboardHtmlProjection> ProjectAsync( ReadOnlyMemory<byte> html, CancellationToken token )
    {
        using var worker = ClipboardProjectionWorker.TryAcquire( token );
        if ( worker is null )
        {
            return Unavailable( "WorkerBusy" );
        }

        return await ProjectAdmittedAsync( html, token ).ConfigureAwait( false );
    }

    internal async ValueTask<ClipboardHtmlProjection> ProjectAdmittedAsync( ReadOnlyMemory<byte> html, CancellationToken token )
    {
        token.ThrowIfCancellationRequested( );
        if ( !OperatingSystem.IsLinux( ) || html.Length is 0 or > ClipboardHtmlProjector.MaximumInputBytes )
        {
            return Unavailable( "InputLimit" );
        }

        try
        {
            _ = StrictUtf8.GetCharCount( html.Span );
        }
        catch ( DecoderFallbackException )
        {
            return Unavailable( "InvalidEncoding" );
        }

        if ( !File.Exists( "/usr/bin/bwrap" ) || !File.Exists( "/usr/bin/prlimit" ) || !File.Exists( nativeExecutable ) )
        {
            return Unavailable( "SandboxUnavailable" );
        }

        var root = Path.Combine( Path.GetTempPath( ), "commanddeck-clipboard-html-" + Guid.NewGuid( ).ToString( "N" ) );
        try
        {
            Directory.CreateDirectory( root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            var snapshot = Path.Combine( root, "input" );
            using ( FileStream stream = new( snapshot, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            } ) )
            {
                await stream.WriteAsync( html, token ).ConfigureAwait( false );
            }

            var result = await _run( "/usr/bin/bwrap", BuildArguments( Path.GetFullPath( nativeExecutable ), snapshot ), TimeSpan.FromSeconds( 2 ),
                new ProcessExecutionLimits
                {
                    MaximumStandardOutputBytes = 256 * 1024,
                    MaximumStandardErrorBytes = 4096,
                    MaximumMemoryBytes = 192L * 1024 * 1024,
                    ClearEnvironment = true
                }, token ).ConfigureAwait( false );
            token.ThrowIfCancellationRequested( );
            if ( result.ExitCode != 0 || result.TimedOut || result.AnyOutputLimitExceeded || result.MemoryLimitExceeded )
            {
                return Unavailable( "WorkerFailed" );
            }

            var projection = JsonSerializer.Deserialize( result.StandardOutput, CommandDeckJsonContext.Default.ClipboardHtmlProjection );
            if ( projection is null || projection.Text is null || StrictUtf8.GetByteCount( projection.Text ) > ClipboardHtmlProjector.MaximumTextBytes ||
                !projection.Available && projection.Text.Length != 0 )
            {
                return Unavailable( "InvalidWorkerResponse" );
            }

            return projection;
        }
        catch ( Exception exception ) when ( exception is IOException or JsonException or EncoderFallbackException )
        {
            return Unavailable( "WorkerFailed" );
        }
        finally
        {
            if ( Directory.Exists( root ) )
            {
                Directory.Delete( root, recursive: true );
            }
        }
    }

    internal static IReadOnlyList<string> BuildArguments( string executable, string snapshot ) =>
    [
        "--unshare-all", "--unshare-user", "--disable-userns", "--die-with-parent", "--new-session",
        "--cap-drop", "ALL", "--clearenv",
        "--ro-bind", "/usr", "/usr", "--ro-bind-try", "/lib", "/lib", "--ro-bind-try", "/lib64", "/lib64",
        "--ro-bind-try", "/etc/ld.so.cache", "/etc/ld.so.cache",
        "--ro-bind", executable, "/worker", "--ro-bind", snapshot, "/input",
        "--proc", "/proc", "--dev", "/dev", "--size", "1048576", "--tmpfs", "/tmp", "--remount-ro", "/",
        "--setenv", "HOME", "/tmp", "--setenv", "TMPDIR", "/tmp", "--setenv", "PATH", "/usr/bin",
        "--setenv", "MALLOC_ARENA_MAX", "1", "--setenv", "DOTNET_gcServer", "0",
        "--setenv", "DOTNET_GCHeapHardLimit", "2000000", "--setenv", "DOTNET_GCRegionRange", "4000000",
        "--setenv", "DOTNET_GCRegionSize", "100000", "--chdir", "/tmp", "--", "/worker", "clipboard-html-project"
    ];

    private static ClipboardHtmlProjection Unavailable( string reason ) => new( false, "", false, reason );

    private static ValueTask<ProcessResult> RunAsync( string file, IReadOnlyList<string> arguments, TimeSpan timeout,
        ProcessExecutionLimits limits, CancellationToken token ) => ProcessRunner.RunBoundedAsync( file, arguments, null, timeout, limits, token );
}
