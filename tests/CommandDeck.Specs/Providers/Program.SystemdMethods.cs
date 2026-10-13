namespace CommandDeck.Specs;

public static partial class Program
{
    private const string SystemdListing = """
        [{"unit":"backup.service","load":"loaded","active":"inactive","sub":"dead","description":"Backup"},
         {"unit":"backup.timer","load":"loaded","active":"active","sub":"waiting","description":"Schedule"},
         {"unit":"*.service","load":"loaded","active":"active","sub":"running","description":"Invalid glob"}]
        """;

    private sealed class SystemdFake
    {
        public List<string[]> Calls { get; } = [];
        public string? IdentityOverride { get; set; }
        public string FragmentPath { get; set; } = "/missing/backup.service";
        public bool FailControl { get; set; }
        public bool TruncateLogs { get; set; }
        public string LogText { get; set; } = "<script>literal logs</script>\n\u001b[31mred";

        public ValueTask<ProcessResult> RunAsync( string file, IReadOnlyList<string> args, TimeSpan timeout,
            ProcessExecutionLimits limits, CancellationToken token )
        {
            token.ThrowIfCancellationRequested( );
            Equal( TimeSpan.FromSeconds( 3 ), timeout );
            Equal( 16 * 1024, limits.MaximumStandardOutputBytes );
            Equal( 4096, limits.MaximumStandardErrorBytes );
            True( !limits.ClearEnvironment );
            Calls.Add( [file, .. args] );
            var unit = IdentityOverride ?? args[^1];
            var output = args.Contains( "show" )
                ? $"Id={unit}\nLoadState=loaded\nActiveState=active\nFragmentPath={FragmentPath}\nNextElapseUSecRealtime=tomorrow\nLastTriggerUSec=yesterday"
                : file == "journalctl" ? LogText : "";
            return ValueTask.FromResult( new ProcessResult
            {
                ExitCode = FailControl && !args.Contains( "show" ) ? 1 : 0,
                StandardOutput = output,
                StandardError = "fixture failure",
                OutputLimitExceeded = file == "journalctl" && TruncateLogs
            } );
        }
    }

    private static async Task SystemdControlsAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-systemd-controls-" ).FullName;
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            SystemdUserServiceProvider provider = new( ( file, args, _, token ) =>
            {
                token.ThrowIfCancellationRequested( );
                Equal( "systemctl", file );
                True( args.Contains( "list-units" ) );
                True( args.Contains( "--type=service,timer" ) );
                return ValueTask.FromResult( new ProcessResult { ExitCode = 0, StandardOutput = SystemdListing, StandardError = "" } );
            }, static ( ) => true );
            SystemdFake fake = new( );
            LinuxActionExecutor executor = new( systemdRunner: fake.RunAsync );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, executor,
                new SearchCoordinator( [provider], options.Search ) );
            var results = await runtime.SearchAsync( new SearchRequest { Query = "svc:", Limit = 20 }, default );
            Equal( 2, results.Count );
            Equal( 0, fake.Calls.Count );

            foreach ( var result in results )
            {
                foreach ( var operation in new[] { SystemdUnitActionIds.Start, SystemdUnitActionIds.Stop,
                    SystemdUnitActionIds.Restart } )
                {
                    var before = fake.Calls.Count;
                    var action = result.Actions.Single( action => action.Id == operation );
                    True( action.RequiresConfirmation );
                    Equal( AgentActionRisks.Confirm, AgentLauncherTools.ClassifyRisk( result, action ) );
                    True( !(await runtime.InvokeAsync( result.Id, operation, false, default )).Succeeded );
                    Equal( before, fake.Calls.Count );
                    True( (await runtime.InvokeAsync( result.Id, operation, true, default )).Succeeded );
                    Equal( before + 2, fake.Calls.Count );
                    Equal( $"systemctl|--user|--no-ask-password|--no-block|{operation}|--|{result.Title}",
                        string.Join( '|', fake.Calls[^1] ) );
                }
            }

            var service = results.Single( result => result.Title == "backup.service" );
            fake.IdentityOverride = "other.service";
            var beforeMismatch = fake.Calls.Count;
            True( !(await runtime.InvokeAsync( service.Id, SystemdUnitActionIds.Stop, true, default )).Succeeded );
            Equal( beforeMismatch + 1, fake.Calls.Count );
            fake.IdentityOverride = null;
            fake.FailControl = true;
            True( !(await runtime.InvokeAsync( service.Id, SystemdUnitActionIds.Start, true, default )).Succeeded );
            fake.FailControl = false;

            var logs = await runtime.InvokeAsync( service.Id, SystemdUnitActionIds.Logs, false, default );
            True( logs.Succeeded );
            True( logs.DetailMarkdown?.Contains( "<script>literal logs</script>", StringComparison.Ordinal ) == true );
            True( logs.DetailMarkdown?.Contains( '\u001b' ) == false );
            Equal( "journalctl|--user|--unit=backup.service|--lines=100|--no-pager|--output=short-iso", string.Join( '|', fake.Calls[^1] ) );
            fake.TruncateLogs = true;
            True( (await runtime.InvokeAsync( service.Id, SystemdUnitActionIds.Logs, false, default )).DetailMarkdown?.Contains( "truncated", StringComparison.Ordinal ) == true );
            fake.LogText = new string( 'é', 20000 );
            var boundedLogs = await runtime.InvokeAsync( service.Id, SystemdUnitActionIds.Logs, false, default );
            True( System.Text.Encoding.UTF8.GetByteCount( boundedLogs.DetailMarkdown! ) < 17 * 1024 );
            var timer = results.Single( result => result.Title == "backup.timer" );
            var status = await runtime.InvokeAsync( timer.Id, SystemdUnitActionIds.Status, false, default );
            True( status.DetailMarkdown?.Contains( "NextElapseUSecRealtime: tomorrow", StringComparison.Ordinal ) == true );

            var beforeForged = fake.Calls.Count;
            True( !(await runtime.InvokeAsync( SystemdUnitResultIds.ForUnit( "forged.service" ),
                SystemdUnitActionIds.Stop, true, default )).Succeeded );
            True( !(await runtime.InvokeAsync( service.Id, "enable", true, default )).Succeeded );
            Equal( beforeForged, fake.Calls.Count );
            using var canceled = new CancellationTokenSource( );
            canceled.Cancel( );
            await ThrowsAsync<OperationCanceledException>( async ( ) =>
                await runtime.InvokeAsync( service.Id, SystemdUnitActionIds.Logs, false, canceled.Token ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task SystemdEditingAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-systemd-editor-" ).FullName;
        try
        {
            var path = Path.Combine( root, "literal $(never); unit.service" );
            await File.WriteAllTextAsync( path, "[Service]\nExecStart=/usr/bin/true" );
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            var defaults = LauncherPreferences.CreateDefault( );
            await catalog.ReplacePreferencesAsync( new LauncherPreferences
            {
                Sources = defaults.Sources,
                PreferredEditor = EditorKinds.Graphical,
                GraphicalEditor = new EditorAdapter { Executable = "/usr/bin/test", Arguments = ["{path}", "=", path] },
                TerminalEditor = defaults.TerminalEditor
            }, default );

            SystemdFake fake = new( )
            {
                FragmentPath = path
            };
            LinuxActionExecutor executor = new( catalog, systemdRunner: fake.RunAsync );
            SystemdUserServiceProvider provider = new( ( _, _, _, _ ) => ValueTask.FromResult(
                new ProcessResult { ExitCode = 0, StandardOutput = SystemdListing, StandardError = "" } ), static ( ) => true );
            var result = (await provider.SearchAsync( QueryParser.Parse( "svc: backup.service" ), new SearchRequest( ), default )).Single( );
            var edit = result.Actions.Single( action => action.Id == SystemdUnitActionIds.Edit );
            var opened = await executor.ExecuteAsync( result, edit, "", default );
            True( opened.Succeeded );
            True( opened.Message?.Contains( "daemon-reload", StringComparison.Ordinal ) == true );
            Equal( 1, fake.Calls.Count );

            fake.FragmentPath = "/missing/unit.service";
            True( !(await executor.ExecuteAsync( result, edit, "", default )).Succeeded );
            edit.Arguments["unit"] = "*.service";
            var before = fake.Calls.Count;
            True( !(await executor.ExecuteAsync( result, edit, "", default )).Succeeded );
            Equal( before, fake.Calls.Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
