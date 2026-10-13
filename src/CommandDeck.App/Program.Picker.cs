using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunPickerAsync( CliArguments arguments, CancellationToken cancellationToken )
    {
        if ( !Console.IsInputRedirected )
        {
            Console.Error.WriteLine( "Pipe one choice per line into commanddeck pick." );
            return 2;
        }
        var bytes = new byte[1024 * 1024 + 1];
        var count = 0;
        await using ( var input = Console.OpenStandardInput( ) )
        {
            while ( count < bytes.Length )
            {
                var read = await input.ReadAsync( bytes.AsMemory( count ), cancellationToken ).ConfigureAwait( false );
                if ( read == 0 )
                {
                    break;
                }
                count += read;
            }
        }
        var session = PickerSession.Parse( bytes.AsSpan( 0, count ), arguments.GetOption( "--prompt" ) ?? "Choose an item", arguments.HasFlag( "--label-tab" ) );
        if ( session.Choices.Count == 0 )
        {
            return 1;
        }
        var shell = Path.Combine( AppContext.BaseDirectory, "picker", "shell.qml" );
        if ( !File.Exists( shell ) )
        {
            throw new InvalidOperationException( "Picker UI assets are missing. Reinstall CommandDeck with its picker directory." );
        }
        if ( !ProcessRunner.ExistsOnPath( "qs" ) )
        {
            throw new InvalidOperationException( "The picker requires Quickshell (qs)." );
        }
        var directory = Directory.CreateTempSubdirectory( "commanddeck-pick-" ).FullName;
        try
        {
            if ( OperatingSystem.IsLinux( ) )
            {
                File.SetUnixFileMode( directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            }
            var inputPath = Path.Combine( directory, "choices.json" );
            var outputPath = Path.Combine( directory, "selection" );
            await using ( var input = SafeRegularFile.CreatePrivateWriteStream( inputPath ) )
            {
                await JsonSerializer.SerializeAsync( input, session, CommandDeckJsonContext.Default.PickerSession, cancellationToken ).ConfigureAwait( false );
            }
            await using ( var output = SafeRegularFile.CreatePrivateWriteStream( outputPath ) )
            {
            }
            var result = await ProcessRunner.RunBoundedAsync( "env",
                ["COMMANDDECK_PICKER_INPUT=" + inputPath, "COMMANDDECK_PICKER_OUTPUT=" + outputPath, "qs", "--no-color", "-p", shell],
                directory, TimeSpan.FromMinutes( 30 ), new ProcessExecutionLimits
                {
                    MaximumStandardOutputBytes = 16 * 1024,
                    MaximumStandardErrorBytes = 16 * 1024,
                    MaximumMemoryBytes = long.MaxValue,
                    ClearEnvironment = false
                }, cancellationToken ).ConfigureAwait( false );
            if ( result.ExitCode == 1 )
            {
                return 1;
            }
            if ( result.ExitCode != 0 || result.TimedOut || result.AnyOutputLimitExceeded )
            {
                Console.Error.WriteLine( "The picker could not complete. Check that the Wayland session and Quickshell are available." );
                return 2;
            }
            var selection = await File.ReadAllTextAsync( outputPath, cancellationToken ).ConfigureAwait( false );
            if ( !int.TryParse( selection, out var index ) || index < 0 || index >= session.Choices.Count )
            {
                Console.Error.WriteLine( "The picker returned no valid selection." );
                return 2;
            }
            Console.WriteLine( arguments.HasFlag( "--index" ) ? index.ToString( ) : session.Choices[index].Value );
            return 0;
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }
}
