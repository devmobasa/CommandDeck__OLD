namespace CommandDeck;

public sealed class ProjectProvider( IReadOnlyList<ProjectOptions> projects ) : ICommandProvider
{
    public string Id => ProviderIds.Projects;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query, SearchRequest request, CancellationToken cancellationToken )
    {
        if ( !query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Projects ) )
        {
            return [];
        }
        var selected = projects.Take( 100 ).Where( project =>
            project.Title.Contains( query.Text, StringComparison.OrdinalIgnoreCase ) ||
            project.Path.Contains( query.Text, StringComparison.OrdinalIgnoreCase ) )
            .Take( Math.Clamp( request.Limit, 1, 20 ) );
        return await Task.WhenAll( selected.Select( project => BuildAsync( project, cancellationToken ) ) )
            .ConfigureAwait( false );
    }

    private static async Task<SearchResult> BuildAsync( ProjectOptions project, CancellationToken cancellationToken )
    {
        var path = Path.GetFullPath( Environment.ExpandEnvironmentVariables(
            project.Path.StartsWith( "~/", StringComparison.Ordinal )
                ? Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), project.Path[2..] )
                : project.Path ) );
        var exists = Directory.Exists( path );
        var status = exists ? "Git status unavailable" : "Checkout is missing";
        if ( exists )
        {
            try
            {
                var git = await ProcessRunner.RunBoundedAsync( "git",
                    ["--no-optional-locks", "-C", path, "status", "--short", "--branch", "--untracked-files=no"],
                    null, TimeSpan.FromMilliseconds( 200 ), new ProcessExecutionLimits
                    {
                        MaximumStandardOutputBytes = 8 * 1024,
                        MaximumStandardErrorBytes = 1024,
                        MaximumMemoryBytes = long.MaxValue,
                        ClearEnvironment = false
                    }, cancellationToken ).ConfigureAwait( false );
                if ( git.AnyOutputLimitExceeded )
                {
                    status = git.StandardOutput.Trim( ) + "\n(status truncated)";
                }
                else if ( git.ExitCode == 0 && !git.TimedOut )
                {
                    status = git.StandardOutput.Trim( );
                }
            }
            catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
            {
                status = "Git status unavailable";
            }
        }
        List<CommandAction> actions = [];
        if ( exists )
        {
            actions.Add( Argv( "editor", "Open in " + project.Editor, path,
                [project.Editor, .. project.EditorArguments.Select( argument => argument.Replace( "{path}", path, StringComparison.Ordinal ) )] ) );
            actions.Add( Argv( "terminal", "Open terminal here", path, ["xdg-terminal-exec", "--dir=" + path] ) );
            actions.Add( HerdrProjectAction.Create( path ) );
        }
        actions.Add( new CommandAction
        {
            Id = "copy-path",
            Title = "Copy project path",
            Kind = ActionKinds.CopyText,
            Arguments = new( StringComparer.Ordinal ) { ["text"] = path }
        } );
        return new SearchResult
        {
            Id = StableId.Create( "project", path ),
            Title = project.Title,
            Subtitle = path,
            Kind = ResultKinds.CustomCommand,
            ProviderId = ProviderIds.Projects,
            SourceId = SourceIds.Projects,
            Icon = "folder-development",
            Score = 2,
            DetailMarkdown = path + "\n\n" + status,
            Section = new ResultSection { Id = "projects", Title = "Projects", Order = 6 },
            Metadata = new( StringComparer.Ordinal ) { ["path"] = path },
            Actions = actions
        };
    }

    private static CommandAction Argv( string id, string title, string path, IReadOnlyList<string> argv )
    {
        if ( argv.Any( static argument => argument.Contains( '\u001f' ) || argument.Contains( '\0' ) ) )
        {
            throw new InvalidOperationException( "Project commands cannot contain control separators." );
        }
        return new CommandAction
        {
            Id = id,
            Title = title,
            Kind = ActionKinds.RunArgv,
            Arguments = new( StringComparer.Ordinal )
            {
                ["argv"] = string.Join( '\u001f', argv ),
                ["workingDirectory"] = path
            }
        };
    }
}
