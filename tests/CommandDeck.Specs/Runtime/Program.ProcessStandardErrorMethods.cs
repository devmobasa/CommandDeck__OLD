using System.Globalization;

namespace CommandDeck.Specs;

public static partial class Program
{
    private const string StandardErrorFlood = "head -c 204800 /dev/zero | tr '\\0' e >&2; exit 1";

    private static ProcessExecutionLimits SmallStreamLimits( ) => new( )
    {
        MaximumStandardOutputBytes = 64 * 1024,
        MaximumStandardErrorBytes = 64 * 1024,
        MaximumMemoryBytes = 512L * 1024 * 1024,
        ClearEnvironment = true
    };

    // Too much error output is its own finding: a caller that reads standard
    // output must not report it as a long answer.
    private static async Task ProcessRunnerReportsStandardErrorOverflowSeparatelyAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var errors = await ProcessRunner.RunBoundedAsync( "/bin/sh", ["-c", StandardErrorFlood], null,
            TimeSpan.FromSeconds( 5 ), SmallStreamLimits( ), CancellationToken.None );
        True( errors.StandardErrorLimitExceeded, "too much error output is reported" );
        True( !errors.OutputLimitExceeded, "it is not reported as too much output" );
        True( errors.AnyOutputLimitExceeded, "callers enforcing either stream's limit still reject the result" );
        True( errors.ExitCode != 0 );

        var output = await ProcessRunner.RunBoundedAsync( "/usr/bin/head",
            ["-c", (256 * 1024).ToString( CultureInfo.InvariantCulture ), "/dev/zero"], null,
            TimeSpan.FromSeconds( 5 ), SmallStreamLimits( ), CancellationToken.None );
        True( output.OutputLimitExceeded && !output.StandardErrorLimitExceeded, "too much output keeps its own flag" );

        // Callers that name an output limit or report truncation still do so
        // when it is the error output that ran over.
        SearchResult row = new( )
        {
            Id = "command:flood",
            Title = "Flood",
            Kind = ResultKinds.CustomCommand,
            SourceId = SourceIds.Scripts
        };
        var run = await new LinuxActionExecutor( ).ExecuteAsync( row, new CommandAction
        {
            Id = "run",
            Title = "Run",
            Kind = ActionKinds.RunArgv,
            Arguments = new( StringComparer.Ordinal ) { ["argv"] = "/bin/sh\u001f-c\u001f" + StandardErrorFlood }
        }, "", CancellationToken.None );
        Equal( "'/bin/sh' exceeded its output limit.", run.Message );

        var root = Directory.CreateTempSubdirectory( "commanddeck-agent-stderr-" ).FullName;
        try
        {
            AgentCommandTool tool = new( root );
            var outcome = await AgentCommandTool.ExecuteAsync( tool.Plan( ["/bin/sh", "-c", StandardErrorFlood], root ), CancellationToken.None );
            True( outcome.Truncated, "the assistant is told its command's output was cut" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
