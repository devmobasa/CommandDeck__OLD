using System.Diagnostics;
using System.Text;

namespace CommandDeck.PackageHelper;

internal sealed record PackageProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public bool TimedOut { get; init; }
    public bool OutputLimitExceeded { get; init; }
}

/// <summary>
/// Runs one literal argv with a cleared environment, a bounded amount of
/// captured output, and a hard timeout. The helper never builds a shell
/// string and never forwards caller-supplied environment.
/// </summary>
internal static class BoundedProcess
{
    public const int DefaultOutputLimitBytes = 1024 * 1024;
    public const string SafePath = "/usr/bin:/bin";

    public static async Task<PackageProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        int maximumOutputBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken )
    {
        ArgumentException.ThrowIfNullOrEmpty( fileName );
        ArgumentNullException.ThrowIfNull( arguments );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( maximumOutputBytes );

        ProcessStartInfo startInfo = new( )
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            WorkingDirectory = "/"
        };
        foreach ( var argument in arguments )
        {
            startInfo.ArgumentList.Add( argument );
        }

        startInfo.Environment.Clear( );
        startInfo.Environment["PATH"] = SafePath;
        startInfo.Environment["LANG"] = "C.UTF-8";

        using Process process = new( )
        {
            StartInfo = startInfo
        };
        try
        {
            if ( !process.Start( ) )
            {
                return StartFailure( );
            }
        }
        catch ( Exception exception ) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException )
        {
            return StartFailure( );
        }

        var truncated = false;
        var outputTask = ReadAsync(
            process.StandardOutput,
            maximumOutputBytes,
            ( ) => truncated = true,
            cancellationToken );
        var errorTask = ReadAsync(
            process.StandardError,
            maximumOutputBytes,
            ( ) => truncated = true,
            cancellationToken );

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        timeoutSource.CancelAfter( timeout );
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync( timeoutSource.Token ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException )
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            TryKill( process );
            await process.WaitForExitAsync( CancellationToken.None ).ConfigureAwait( false );
            if ( !timedOut )
            {
                throw;
            }
        }

        var standardOutput = await outputTask.ConfigureAwait( false );
        var standardError = await errorTask.ConfigureAwait( false );
        return new PackageProcessResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            TimedOut = timedOut,
            OutputLimitExceeded = truncated
        };
    }

    private static async Task<string> ReadAsync(
        StreamReader reader,
        int maximumBytes,
        Action exceeded,
        CancellationToken cancellationToken )
    {
        using MemoryStream captured = new( Math.Min( maximumBytes, 64 * 1024 ) );
        var buffer = new byte[8 * 1024];
        var limitReached = false;
        while ( true )
        {
            var read = await reader.BaseStream
                .ReadAsync( buffer, cancellationToken )
                .ConfigureAwait( false );
            if ( read == 0 )
            {
                break;
            }

            var writable = Math.Max( 0, Math.Min( read, maximumBytes - ( int ) captured.Length ) );
            if ( writable > 0 )
            {
                captured.Write( buffer, 0, writable );
            }

            if ( writable < read && !limitReached )
            {
                limitReached = true;
                exceeded( );
            }
        }

        return Encoding.UTF8.GetString( captured.GetBuffer( ), 0, ( int ) captured.Length );
    }

    private static PackageProcessResult StartFailure( ) => new( )
    {
        ExitCode = -1,
        StandardOutput = string.Empty,
        StandardError = "The package manager could not be started."
    };

    private static void TryKill( Process process )
    {
        try
        {
            if ( !process.HasExited )
            {
                process.Kill( entireProcessTree: true );
            }
        }
        catch ( InvalidOperationException )
        {
        }
    }
}
