using System.Buffers;

namespace CommandDeck;

// Command mode's first row: `> program arguments` offers to run that program when
// it is a name found on PATH or an absolute path to an executable file. The text
// is split by QueryParser.SplitArguments, never by a shell, so pipes, redirection
// and variables need `sh -c`. Nothing runs until this row's own action is
// invoked. The row ID follows the argv, so an invocation runs exactly the argv it
// showed; usage is recorded under one fixed ID, never the command line.
public sealed class RunCommandProvider : ICommandProvider
{
    public const string UsageId = "run-command";

    // Words a POSIX shell reads literally; any other word is single-quoted.
    private static readonly SearchValues<char> ShellLiteral = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_@%+=:,./-" );

    public string Id => ProviderIds.RunCommand;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        IReadOnlyList<SearchResult> results = query.IsCommandMode && TryParse( query.Text, out var argv )
            ? [Build( argv )]
            : [];

        return ValueTask.FromResult( results );
    }

    // The argv of the text when its program can run. A relative path such as
    // ./tool, ~, control characters and a program path with "$" (which a terminal
    // launch would expand) never qualify.
    public static bool TryParse( string text, out string[] argv )
    {
        try
        {
            argv = QueryParser.SplitArguments( text );
        }
        catch ( InvalidOperationException )
        {
            argv = [];
            return false;
        }

        if ( argv.Length == 0 || argv.Any( static argument => argument.Any( char.IsControl ) ) )
        {
            return false;
        }

        var program = argv[0];
        return program.Length > 0 && !program.Contains( '$' ) &&
            (Path.IsPathFullyQualified( program ) || !program.Contains( '/' )) &&
            ProcessRunner.ExistsOnPath( program );
    }

    // The command line as a shell would need it, for the title and Copy Command.
    public static string CommandLine( IReadOnlyList<string> argv ) =>
        string.Join( ' ', argv.Select( static argument =>
            argument.Length > 0 && argument.AsSpan( ).IndexOfAnyExcept( ShellLiteral ) < 0
                ? argument
                : $"'{argument.Replace( "'", "'\\''", StringComparison.Ordinal )}'" ) );

    private static SearchResult Build( string[] argv )
    {
        var encoded = LaunchArgumentCodec.Encode( argv );
        var line = CommandLine( argv );

        return new SearchResult
        {
            Id = StableId.Create( UsageId, encoded ),
            Kind = ResultKinds.CustomCommand,
            Title = $"Run {line}",
            Subtitle = "Program · runs without a shell",
            Icon = "utilities-terminal",
            ProviderId = ProviderIds.RunCommand,
            SourceId = SourceIds.CommandsQuicklinks,
            Score = 1,
            CanCustomize = false,
            Details = [new DetailField( "Command", line, Monospace: true )],
            Metadata = new Dictionary<string, string>( StringComparer.Ordinal )
            {
                [ResultMetadataKeys.CatalogResultId] = UsageId,
                [ResultPlacements.MetadataKey] = ResultPlacements.Explicit
            },
            Actions =
            [
                Launch( "run", "Run", encoded, terminal: false ),
                Launch( "run-in-terminal", "Run in Terminal", encoded, terminal: true ),
                new CommandAction
                {
                    Id = "copy-command",
                    Title = "Copy Command",
                    Kind = ActionKinds.CopyText,
                    Arguments = new TextActionPayload( line ).ToArguments( )
                }
            ]
        };
    }

    private static CommandAction Launch( string id, string title, string encoded, bool terminal ) => new( )
    {
        Id = id,
        Title = title,
        Kind = ActionKinds.LaunchArgv,
        Arguments = new Dictionary<string, string>( StringComparer.Ordinal )
        {
            ["argv"] = encoded,
            ["terminal"] = terminal ? "true" : "false"
        }
    };
}
