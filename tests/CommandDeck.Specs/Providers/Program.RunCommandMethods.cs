namespace CommandDeck.Specs;

public static partial class Program
{
    private static void RunCommandArgumentsFollowTheInvocationRules( )
    {
        Equal( "fixture·--flag·two words·a\"b c\"·", string.Join( '·',
            QueryParser.SplitArguments( "fixture  --flag \"two words\" a\"b c\" \"\"" ) ) );

        // Shell syntax is ordinary text: no pipe, redirection or variable applies.
        Equal( "echo·$HOME·>·out.txt·|·&&·ls", string.Join( '·',
            QueryParser.SplitArguments( "echo $HOME > out.txt | && ls" ) ) );
        Throws<InvalidOperationException>( ( ) => QueryParser.SplitArguments( "fixture \"open" ) );

        Equal( "fixture --flag 'two words' 'it'\\''s' '' '~/x' '$HOME' a=b,c:d@1.0/%",
            RunCommandProvider.CommandLine( ["fixture", "--flag", "two words", "it's", "", "~/x", "$HOME", "a=b,c:d@1.0/%"] ) );
    }

    private static async Task RunCommandRowNeedsAnExecutableProgramAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-run-command-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var bin = Directory.CreateDirectory( Path.Combine( root, "bin" ) ).FullName;
            var ran = Path.Combine( root, "ran" );
            var fixture = Path.Combine( bin, "some-fixture-binary" );
            await WriteExecutableAsync( fixture, $"#!/bin/sh\n: > '{ran}'\n" );
            var dollar = Directory.CreateDirectory( Path.Combine( root, "d$x" ) ).FullName;
            await WriteExecutableAsync( Path.Combine( dollar, "tool" ), $"#!/bin/sh\n: > '{ran}'\n" );
            await File.WriteAllTextAsync( Path.Combine( bin, "not-executable" ), "#!/bin/sh\n" );
            Environment.SetEnvironmentVariable( "PATH", bin );
            RunCommandProvider provider = new( );

            async Task<IReadOnlyList<SearchResult>> FindAsync( string query ) =>
                await provider.SearchAsync( QueryParser.Parse( query ), new SearchRequest { Query = query }, default );

            var row = (await FindAsync( "> some-fixture-binary --flag \"two words\"" )).Single( );
            Equal( "Run some-fixture-binary --flag 'two words'", row.Title );
            Equal( ResultKinds.CustomCommand, row.Kind );
            Equal( SourceIds.CommandsQuicklinks, row.SourceId );
            Equal( RunCommandProvider.UsageId, row.Metadata[ResultMetadataKeys.CatalogResultId] );
            True( ResultPlacements.IsExplicit( row ), "the Run row is the query's explicit answer" );
            Equal( "run|run-in-terminal|copy-command", string.Join( '|', row.Actions.Select( static action => action.Id ) ) );
            foreach ( var action in row.Actions.Take( 2 ) )
            {
                Equal( ActionKinds.LaunchArgv, action.Kind );
                Equal( "some-fixture-binary|--flag|two words", string.Join( '|', LaunchArgumentCodec.Decode( action.Arguments["argv"] ) ) );
                True( !action.RequiresConfirmation && !action.IsDestructive );
            }

            Equal( "false", row.Actions[0].Arguments["terminal"] );
            Equal( "true", row.Actions[1].Arguments["terminal"] );
            Equal( ActionKinds.CopyText, row.Actions[2].Kind );
            Equal( "some-fixture-binary --flag 'two words'", row.Actions[2].Arguments["text"] );

            // The ID follows the argv, so a row can only ever run what it showed.
            var other = (await FindAsync( "> some-fixture-binary --other" )).Single( );
            True( other.Id != row.Id );
            Equal( row.Id, (await FindAsync( ">some-fixture-binary   --flag \"two words\"" )).Single( ).Id );

            var absolute = (await FindAsync( $"> {fixture} -x" )).Single( );
            Equal( $"{fixture}|-x", string.Join( '|', LaunchArgumentCodec.Decode( absolute.Actions[0].Arguments["argv"] ) ) );

            foreach ( var query in new[]
            {
                "> no-such-program-for-commanddeck --flag", "> not-executable", $"> {Path.Combine( bin, "not-executable" )}",
                "> ./some-fixture-binary", "> bin/some-fixture-binary", "> ~/bin/some-fixture-binary", $"> {bin}",
                $"> {Path.Combine( dollar, "tool" )}",
                "> some-fixture-binary \"open", "> some-fixture-binary \u001b[31m", "> ", ">", "> \"\" --flag",
                "some-fixture-binary --flag", "app: some-fixture-binary --flag", "= some-fixture-binary"
            } )
            {
                True( (await FindAsync( query )).Count == 0, query );
            }

            True( !File.Exists( ran ), "searching never runs the program" );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task CommandModeListsTheRunRowFirstAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-run-command-mode-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            await WriteExecutableAsync( Path.Combine( root, "some-fixture-binary" ), "#!/bin/sh\n" );
            Environment.SetEnvironmentVariable( "PATH", root );
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( SingleItem( new IndexedItem
            {
                StableId = "app:recent.desktop",
                Kind = ResultKinds.Application,
                Title = "Recent application",
                ProviderId = ProviderIds.Applications,
                SourceId = SourceIds.Applications
            } ), null, default );
            RunCommandProvider run = new( );
            CatalogSearchProvider catalogProvider = new( catalog );
            SearchCoordinator search = new( [catalogProvider, run], new SearchOptions( ) );

            var results = await search.SearchAsync( new SearchRequest { Query = "> some-fixture-binary --flag", Limit = 20 }, default );
            Equal( "Run some-fixture-binary --flag", results[0].Title );
            True( results.Any( static result => result.Title == "Recent application" ),
                "the other command-mode results still follow" );
            True( (await search.SearchAsync( new SearchRequest { Query = "> missing-program", Limit = 20 }, default ))
                .All( static result => result.ProviderId != ProviderIds.RunCommand ) );

            var preferences = LauncherPreferences.CreateDefault( );
            True( ProviderEligibilityPolicy.Select( [catalogProvider, run], QueryParser.Parse( "> x" ), preferences, null ).Contains( run ) );
            True( !ProviderEligibilityPolicy.Select( [catalogProvider, run], QueryParser.Parse( "x" ), preferences, null ).Contains( run ) );

            var row = results[0];
            Equal( AgentActionRisks.Denied, AgentLauncherTools.ClassifyRisk( row, row.Actions[0] ) );
            Equal( AgentActionRisks.Denied, AgentLauncherTools.ClassifyRisk( row, row.Actions[1] ) );
            await catalog.DisposeAsync( );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( root, recursive: true );
        }

        static async IAsyncEnumerable<IndexedItem> SingleItem( IndexedItem item )
        {
            await Task.CompletedTask;
            yield return item;
        }
    }

    // The fake launchers record their argv and never start the program.
    private static async Task RunCommandLaunchesDetachedLikeApplicationsAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-run-command-launch-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var capture = Path.Combine( root, "calls" );
            var ran = Path.Combine( root, "ran" );
            await WriteExecutableAsync( Path.Combine( root, "some-fixture-binary" ), $"#!/bin/sh\n: > '{ran}'\n" );
            await WriteExecutableAsync( Path.Combine( root, "uwsm-app" ), ArgvRecorder( capture ) );
            Environment.SetEnvironmentVariable( "PATH", root );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, new LinuxActionExecutor( ),
                new SearchCoordinator( [new RunCommandProvider( )], options.Search ) );

            var row = (await runtime.SearchAsync(
                new SearchRequest { Query = "> some-fixture-binary --flag \"two words\" $HOME a$$b", Limit = 20 }, default )).Single( );
            True( !File.Exists( capture ), "the search alone launches nothing" );

            True( (await runtime.InvokeAsync( row.Id, "run", false, default )).Succeeded );
            True( (await runtime.InvokeAsync( row.Id, "run-in-terminal", false, default )).Succeeded );
            File.Delete( Path.Combine( root, "uwsm-app" ) );
            await WriteExecutableAsync( Path.Combine( root, "systemd-run" ), ArgvRecorder( capture ) );
            await WriteExecutableAsync( Path.Combine( root, "xdg-terminal-exec" ), "#!/bin/sh\n" );
            True( (await runtime.InvokeAsync( row.Id, "run-in-terminal", false, default )).Succeeded );
            // Both launchers start a transient unit, where "$$" is a literal "$".
            Equal(
                "uwsm-app|-t|service|-S|both|--|some-fixture-binary|--flag|two words|$$HOME|a$$$$b\n" +
                "uwsm-app|-t|service|-S|both|-T|--|some-fixture-binary|--flag|two words|$$HOME|a$$$$b\n" +
                "systemd-run|--user|--quiet|--collect|xdg-terminal-exec|--|some-fixture-binary|--flag|two words|$$HOME|a$$$$b\n",
                await File.ReadAllTextAsync( capture ) );
            True( !File.Exists( ran ), "the fixture launchers never start the program" );

            // systemd-run resolves the executable itself, so only arguments are escaped.
            File.WriteAllText( capture, string.Empty );
            var direct = await new LinuxActionExecutor( ).ExecuteAsync( row, new CommandAction
            {
                Id = "run",
                Title = "Run",
                Kind = ActionKinds.LaunchArgv,
                Arguments = new( StringComparer.Ordinal ) { ["argv"] = LaunchArgumentCodec.Encode( ["/opt/d$x/tool", "$HOME"] ) }
            }, string.Empty, default );
            True( direct.Succeeded );
            Equal( "systemd-run|--user|--quiet|--collect|/opt/d$x/tool|$$HOME\n", await File.ReadAllTextAsync( capture ) );

            // Usage is kept under one fixed ID, never under the command line.
            var usage = await catalog.UsageHistory.GetUsageSignalsAsync( [RunCommandProvider.UsageId, row.Id], default );
            Equal( 3L, usage[RunCommandProvider.UsageId].UseCount );
            True( !usage.ContainsKey( row.Id ) );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( root, recursive: true );
        }
    }
}
