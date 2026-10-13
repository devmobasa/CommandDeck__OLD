using System.Security.Cryptography;
using System.Text.Json;

namespace CommandDeck;

public sealed class ScriptCommandProvider( IReadOnlyList<string> directories ) : ICommandProvider
{
    public string Id => ProviderIds.Scripts;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken cancellationToken )
    {
        if ( !query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Scripts ) )
        {
            return [];
        }
        List<SearchResult> results = [];
        foreach ( var directory in directories.Take( 16 ) )
        {
            var expanded = PathExpansion.Expand( directory );
            if ( !Directory.Exists( expanded ) )
            {
                continue;
            }
            string[] files;
            try
            {
                files = Directory.EnumerateFiles( expanded, "*.commanddeck.json" ).Take( 100 ).ToArray( );
            }
            catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
            {
                continue;
            }
            foreach ( var file in files )
            {
                cancellationToken.ThrowIfCancellationRequested( );
                try
                {
                    var (manifest, digest) = await ReadAsync( file, cancellationToken ).ConfigureAwait( false );
                    if ( !manifest.Title.Contains( query.Text, StringComparison.OrdinalIgnoreCase ) &&
                        !manifest.Description.Contains( query.Text, StringComparison.OrdinalIgnoreCase ) )
                    {
                        continue;
                    }
                    results.Add( new SearchResult
                    {
                        Id = StableId.Create( "script", file ),
                        Title = manifest.Title,
                        Subtitle = manifest.Description,
                        DetailMarkdown = manifest.Description + "\n\n" + file,
                        Kind = ResultKinds.CustomCommand,
                        SourceId = SourceIds.Scripts,
                        ProviderId = ProviderIds.Scripts,
                        Icon = "utilities-terminal",
                        Score = 2,
                        Section = new ResultSection { Id = "scripts", Title = "Script commands", Order = 6 },
                        Actions = [new CommandAction
                        {
                            Id = "run", Title = "Run script", Kind = ActionKinds.RunScript,
                            Inputs = manifest.Inputs, RequiresConfirmation = true,
                            Arguments = new(StringComparer.Ordinal) { ["manifest"] = file, ["digest"] = digest }
                        }]
                    } );
                    if ( results.Count >= Math.Clamp( request.Limit, 1, 100 ) )
                    {
                        return results;
                    }
                }
                catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException )
                {
                    // A broken manifest must not prevent other commands from being discovered.
                }
            }
        }
        return results;
    }

    public static async ValueTask<ActionExecutionResult> ExecuteAsync( CommandAction action, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( action );
        var path = action.Arguments["manifest"];
        var (manifest, digest) = await ReadAsync( path, cancellationToken ).ConfigureAwait( false );
        if ( digest != action.Arguments["digest"] )
        {
            return ActionExecutionResult.Failure( "The script manifest changed. Search again before running it." );
        }
        var directory = Path.GetDirectoryName( path )!;
        var executable = Path.IsPathFullyQualified( manifest.Executable )
            ? manifest.Executable : Path.GetFullPath( manifest.Executable, directory );
        var arguments = manifest.Arguments.Select( argument => ExpandArgument( argument, manifest.Inputs, action.Arguments ) );
        var result = await ProcessRunner.RunBoundedAsync( executable, arguments,
            ResolveWorkingDirectory( manifest.WorkingDirectory, directory ),
            TimeSpan.FromSeconds( manifest.TimeoutSeconds ), new ProcessExecutionLimits
            {
                MaximumStandardOutputBytes = 64 * 1024,
                MaximumStandardErrorBytes = 16 * 1024,
                MaximumMemoryBytes = long.MaxValue,
                ClearEnvironment = false
            }, cancellationToken ).ConfigureAwait( false );
        var output = result.StandardOutput + (result.StandardError.Length > 0 ? "\n\nstderr:\n" + result.StandardError : "");
        if ( output.Length == 0 )
        {
            output = "(No output)";
        }
        var exceeded = result.OutputLimitExceeded || result.StandardErrorLimitExceeded;
        var message = result.TimedOut ? "Script timed out" : exceeded ? "Script exceeded its output limit" : "Script exited with code " + result.ExitCode;
        return result.ExitCode == 0 && !result.TimedOut && !exceeded
            ? ActionExecutionResult.Success( message, output ) : ActionExecutionResult.Failure( message, output );
    }

    private static string ResolveWorkingDirectory( string? configured, string manifestDirectory )
    {
        if ( configured is null )
        {
            return manifestDirectory;
        }
        return Path.IsPathFullyQualified( configured ) || configured.StartsWith( '~' ) || configured.StartsWith( '$' )
                    ? PathExpansion.Expand( configured ) : Path.GetFullPath( configured, manifestDirectory );
    }

    private static string ExpandArgument( string argument, List<ActionInput> inputs, Dictionary<string, string> values )
    {
        // Whole-argument substitution keeps user input in one argv entry, including empty/spaced values.
        var input = inputs.FirstOrDefault( input => argument == "{" + input.Name + "}" );
        return input is null ? argument : values["input:" + input.Name];
    }

    private static async Task<(ScriptCommandManifest Manifest, string Digest)> ReadAsync( string path, CancellationToken cancellationToken )
    {
        await using var stream = File.OpenRead( path );
        if ( stream.Length > 64 * 1024 )
        {
            throw new InvalidOperationException( "Script manifest exceeds 64 KiB." );
        }
        var bytes = new byte[checked(( int ) stream.Length)];
        await stream.ReadExactlyAsync( bytes, cancellationToken ).ConfigureAwait( false );
        var manifest = JsonSerializer.Deserialize( bytes, CommandDeckJsonContext.Default.ScriptCommandManifest );
        if ( manifest is null )
        {
            throw new InvalidOperationException( "Empty script manifest." );
        }

        Validate( manifest );
        return (manifest, Convert.ToHexString( SHA256.HashData( bytes ) ));
    }

    private static void Validate( ScriptCommandManifest manifest )
    {
        if ( manifest.Description is null || string.IsNullOrWhiteSpace( manifest.Title ) || string.IsNullOrWhiteSpace( manifest.Executable ) ||
            manifest.Executable.Contains( '\0' ) || manifest.TimeoutSeconds is < 1 or > 300 ||
            manifest.Arguments is null || manifest.Arguments.Count > 64 ||
            manifest.Arguments.Any( static argument => argument is null || argument.Contains( '\0' ) ) ||
            manifest.Inputs is null || manifest.Inputs.Count > 16 )
        {
            throw new InvalidOperationException( "Invalid script command manifest." );
        }
        HashSet<string> names = new( StringComparer.Ordinal );
        foreach ( var input in manifest.Inputs )
        {
            if ( input is null || string.IsNullOrWhiteSpace( input.Name ) || input.Name.Length > 64 ||
                !input.Name.All( static value => char.IsAsciiLetterOrDigit( value ) || value is '-' or '_' ) || !names.Add( input.Name ) )
            {
                throw new InvalidOperationException( "Invalid script input names." );
            }
        }
    }
}
