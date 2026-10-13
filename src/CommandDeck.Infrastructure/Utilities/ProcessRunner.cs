using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CommandDeck;

public sealed class ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public bool TimedOut { get; init; }
    public bool OutputLimitExceeded { get; init; }
    public bool MemoryLimitExceeded { get; init; }
    public bool StandardErrorLimitExceeded { get; init; }
    public bool AnyOutputLimitExceeded => OutputLimitExceeded || StandardErrorLimitExceeded;
}
public sealed class ProcessExecutionLimits
{
    public required int MaximumStandardOutputBytes { get; init; }
    public required int MaximumStandardErrorBytes { get; init; }
    public required long MaximumMemoryBytes { get; init; }
    public bool ClearEnvironment { get; init; } = true;
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
}

public delegate ValueTask<ProcessResult> ExternalProcessRunner(
    string fileName,
    IReadOnlyList<string> arguments,
    TimeSpan timeout,
    CancellationToken cancellationToken );

// Carries the limits and environment so specs can assert them without a real process.
public delegate ValueTask<ProcessResult> BoundedProcessRunner(
    string fileName,
    IReadOnlyList<string> arguments,
    TimeSpan timeout,
    ProcessExecutionLimits limits,
    CancellationToken cancellationToken );

public static partial class ProcessRunner
{
    private const string LinuxResourceLimitExecutable = "/usr/bin/prlimit";
    private const string LinuxSetsidExecutable = "/usr/bin/setsid";
    private const int DefaultMaximumStandardOutputBytes = 1024 * 1024;
    private const int DefaultMaximumStandardErrorBytes = 64 * 1024;

    public static ValueTask<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? standardInput,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken ) =>
        RunCoreAsync(
            fileName,
            arguments,
            standardInput,
            workingDirectory,
            timeout,
            limits: null,
            cancellationToken );

    public static ValueTask<ProcessResult> RunBoundedAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        ProcessExecutionLimits limits,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( limits );
        if ( limits.MaximumStandardOutputBytes <= 0 ||
            limits.MaximumStandardErrorBytes <= 0 ||
            limits.MaximumMemoryBytes <= 0 )
        {
            throw new ArgumentOutOfRangeException(
                nameof( limits ),
                "Process output and memory limits must be positive." );
        }

        return RunCoreAsync(
            fileName,
            arguments,
            standardInput: null,
            workingDirectory,
            timeout,
            limits,
            cancellationToken );
    }

    public static bool ExistsOnPath( string executable )
    {
        if ( Path.IsPathFullyQualified( executable ) )
        {
            return IsExecutableFile( executable );
        }

        var path = Environment.GetEnvironmentVariable( "PATH" );
        if ( string.IsNullOrWhiteSpace( path ) )
        {
            return false;
        }

        foreach ( var directory in path.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries ) )
        {
            var candidate = Path.Combine( directory, executable );
            if ( IsExecutableFile( candidate ) )
            {
                return true;
            }
        }

        return false;
    }

    private static async ValueTask<ProcessResult> RunCoreAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? standardInput,
        string? workingDirectory,
        TimeSpan timeout,
        ProcessExecutionLimits? limits,
        CancellationToken cancellationToken )
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual( timeout, TimeSpan.Zero );

        var (startInfo, ownsProcessGroup, monitorsMemory) = CreateStartInfo(
            fileName,
            arguments,
            standardInput is not null,
            workingDirectory,
            limits );

        using Process process = new( )
        {
            StartInfo = startInfo
        };
        try
        {
            if ( !process.Start( ) )
            {
                return StartFailure( fileName, "Process start returned false." );
            }
        }
        catch ( Exception exception ) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException )
        {
            return StartFailure( fileName, exception.Message );
        }

        var processGroupId = ownsProcessGroup ? process.Id : 0;

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        timeoutSource.CancelAfter( timeout );
        var outputLimitExceeded = 0;
        var errorLimitExceeded = 0;
        var memoryLimitExceeded = 0;
        var outputTask = ReadOutputAsync(
            process.StandardOutput.BaseStream,
            limits?.MaximumStandardOutputBytes ?? DefaultMaximumStandardOutputBytes,
            ( ) =>
            {
                Interlocked.Exchange( ref outputLimitExceeded, 1 );
                TryKill( process, processGroupId );
            },
            timeoutSource.Token );
        var errorTask = ReadOutputAsync(
            process.StandardError.BaseStream,
            limits?.MaximumStandardErrorBytes ?? DefaultMaximumStandardErrorBytes,
            ( ) =>
            {
                Interlocked.Exchange( ref errorLimitExceeded, 1 );
                TryKill( process, processGroupId );
            },
            timeoutSource.Token );

        using CancellationTokenSource monitorCancellation = new( );
        var monitorTask = monitorsMemory && limits is not null
            ? MonitorMemoryAsync(
                process,
                processGroupId,
                limits.MaximumMemoryBytes,
                ( ) => Interlocked.Exchange( ref memoryLimitExceeded, 1 ),
                monitorCancellation.Token )
            : Task.CompletedTask;
        var timedOut = false;
        try
        {
            if ( standardInput is not null )
            {
                await process.StandardInput.WriteAsync( standardInput.AsMemory( ), timeoutSource.Token )
                    .ConfigureAwait( false );
                process.StandardInput.Close( );
            }

            await process.WaitForExitAsync( timeoutSource.Token ).ConfigureAwait( false );
            await Task.WhenAll( outputTask, errorTask ).WaitAsync( timeoutSource.Token )
                .ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when (
            !cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested )
        {
            timedOut = true;
            TryKill( process, processGroupId );
            await process.WaitForExitAsync( CancellationToken.None ).ConfigureAwait( false );
        }
        catch
        {
            timeoutSource.Cancel( );
            TryKill( process, processGroupId );
            await process.WaitForExitAsync( CancellationToken.None ).ConfigureAwait( false );
            try
            {
                await Task.WhenAll( outputTask, errorTask ).ConfigureAwait( false );
            }
            catch ( Exception )
            {
                // Preserve the input, process, or cancellation failure after observing drains.
            }
            throw;
        }
        finally
        {
            monitorCancellation.Cancel( );
        }

        if ( cancellationToken.IsCancellationRequested )
        {
            TryKill( process, processGroupId );
            cancellationToken.ThrowIfCancellationRequested( );
        }

        try
        {
            await monitorTask.ConfigureAwait( false );
        }
        catch ( OperationCanceledException )
        {
            // Normal completion stops the resource monitor.
        }

        var stdout = await outputTask.ConfigureAwait( false );
        var stderr = await errorTask.ConfigureAwait( false );
        return new ProcessResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            StandardOutput = stdout.Text,
            StandardError = stderr.Text,
            TimedOut = timedOut,
            OutputLimitExceeded = outputLimitExceeded != 0 || stdout.Exceeded,
            StandardErrorLimitExceeded = errorLimitExceeded != 0 || stderr.Exceeded,
            MemoryLimitExceeded = memoryLimitExceeded != 0
        };
    }

    private static (ProcessStartInfo StartInfo, bool OwnsProcessGroup, bool MonitorsMemory) CreateStartInfo(
        string fileName,
        IEnumerable<string> arguments,
        bool redirectStandardInput,
        string? workingDirectory,
        ProcessExecutionLimits? limits )
    {
        List<string> argumentList = [.. arguments];
        var processFileName = fileName;
        var monitorsMemory = limits is not null && limits.MaximumMemoryBytes < long.MaxValue;

        if ( limits is not null && OperatingSystem.IsLinux( ) &&
            IsExecutableFile( LinuxResourceLimitExecutable ) )
        {
            // prlimit installs both address-space limits before exec. On failure it never runs the
            // target; RSS cannot exceed the enforced address space, so no sampling is needed.
            monitorsMemory = false;
            processFileName = LinuxResourceLimitExecutable;
            argumentList =
            [
                $"--as={limits.MaximumMemoryBytes}",
                "--",
                fileName,
                .. argumentList
            ];
        }

        var ownsProcessGroup = OperatingSystem.IsLinux( ) && IsExecutableFile( LinuxSetsidExecutable );
        if ( ownsProcessGroup )
        {
            argumentList = ["--", processFileName, .. argumentList];
            processFileName = LinuxSetsidExecutable;
        }

        ProcessStartInfo startInfo = new( )
        {
            FileName = processFileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStandardInput,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace( workingDirectory )
                ? Environment.CurrentDirectory
                : PathExpansion.Expand( workingDirectory )
        };
        foreach ( var argument in argumentList )
        {
            startInfo.ArgumentList.Add( argument );
        }

        if ( limits?.ClearEnvironment == true )
        {
            ConfigureEnvironment( startInfo, limits.EnvironmentVariables );
        }

        return (startInfo, ownsProcessGroup, monitorsMemory);
    }

    private static void ConfigureEnvironment(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string>? configured )
    {
        startInfo.Environment.Clear( );
        var environment = configured ??
                          new Dictionary<string, string>( StringComparer.Ordinal )
                          {
                              ["PATH"] = Environment.GetEnvironmentVariable( "PATH" ) ?? "/usr/bin:/bin",
                              ["LANG"] = "C.UTF-8",
                              ["LC_ALL"] = "C.UTF-8"
                          };
        foreach ( (var key, var value) in environment )
        {
            startInfo.Environment[key] = value;
        }
    }

    private static async Task<BoundedReadResult> ReadOutputAsync(
        Stream stream,
        int? maximumBytes,
        Action exceeded,
        CancellationToken cancellationToken )
    {
        var capacity = maximumBytes is null ? 4_096 : Math.Min( maximumBytes.Value, 64 * 1024 );
        using MemoryStream captured = new( capacity );
        var buffer = new byte[8 * 1024];
        var limitExceeded = false;
        try
        {
            while ( true )
            {
                var read = await stream.ReadAsync( buffer, cancellationToken ).ConfigureAwait( false );
                if ( read == 0 )
                {
                    break;
                }

                var writable = maximumBytes is null
                    ? read
                    : Math.Max( 0, Math.Min( read, maximumBytes.Value - checked(( int ) captured.Length) ) );
                if ( writable > 0 )
                {
                    captured.Write( buffer, 0, writable );
                }

                if ( writable < read && !limitExceeded )
                {
                    limitExceeded = true;
                    exceeded( );
                }
            }
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            // The caller classifies timeout versus cancellation after cleanup.
        }

        return new BoundedReadResult(
            Encoding.UTF8.GetString( captured.GetBuffer( ), 0, checked(( int ) captured.Length) ),
            limitExceeded );
    }

    private static async Task MonitorMemoryAsync(
        Process process,
        int processGroupId,
        long maximumBytes,
        Action exceeded,
        CancellationToken cancellationToken )
    {
        while ( !process.HasExited )
        {
            try
            {
                process.Refresh( );
                if ( process.WorkingSet64 > maximumBytes )
                {
                    exceeded( );
                    TryKill( process, processGroupId );
                    return;
                }
            }
            catch ( InvalidOperationException )
            {
                return;
            }

            await Task.Delay( 20, cancellationToken ).ConfigureAwait( false );
        }
    }

    private static ProcessResult StartFailure( string fileName, string detail ) => new( )
    {
        ExitCode = -1,
        StandardOutput = string.Empty,
        StandardError = $"Unable to start '{fileName}': {detail}"
    };

    private static bool IsExecutableFile( string path )
    {
        if ( !File.Exists( path ) )
        {
            return false;
        }

        if ( !OperatingSystem.IsLinux( ) )
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode( path );
            return (mode & (UnixFileMode.UserExecute |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherExecute)) != 0;
        }
        catch ( Exception exception ) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException )
        {
            return false;
        }
    }

    private static void TryKill( Process process, int processGroupId )
    {
        if ( processGroupId > 0 && OperatingSystem.IsLinux( ) )
        {
            _ = KillNative( -processGroupId, 9 );
        }

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

    [LibraryImport( "libc", EntryPoint = "kill", SetLastError = true )]
    private static partial int KillNative( int processId, int signal );

    private sealed record BoundedReadResult( string Text, bool Exceeded );
}
