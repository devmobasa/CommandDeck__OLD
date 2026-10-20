namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class SteppingTimeProvider( DateTimeOffset start ) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow( ) => Now;
    }

    // Runs the batch script with a non-login bash, so the spec never reads the
    // developer's profile, and records each call.
    private sealed class RecordingBashRunner
    {
        public List<(string File, IReadOnlyList<string> Arguments, ProcessExecutionLimits Limits)> Calls { get; } = [];

        public ValueTask<ProcessResult> RunAsync( string file, IReadOnlyList<string> arguments, TimeSpan timeout,
            ProcessExecutionLimits limits, CancellationToken cancellationToken )
        {
            lock ( Calls )
            {
                Calls.Add( (file, arguments, limits) );
            }

            return ProcessRunner.RunBoundedAsync( "/bin/bash", ["-c", arguments[^1]], null, timeout, limits, cancellationToken );
        }
    }

    // Omarchy rows indexed with `when` and `checked` come back from a catalog
    // search marked from one bounded login-bash batch: a failed guard dims the
    // row and halves its score, a holding check marks it, and an `exit` or a
    // stdin read in one condition cannot spoil the others. Answers are cached,
    // and stale ones are served at once while a refresh runs.
    private static async Task OmarchyMenuStateMarksRowsFromOneBatchAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) || !File.Exists( "/bin/bash" ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux bash." );
        }

        const string menu = """
            {
              "style": {"label":"Style"},
              "style.dark": {"label":"Dark mode","action":"dark","when":"true","checked":"[[ 2 -gt 1 ]]"},
              "style.light": {"label":"Light mode","action":"light","when":"exit 0","checked":"exit 1"},
              "style.stale": {"label":"Stale mode","action":"stale","when":"false"},
              "style.quiet": {"label":"Quiet mode","action":"quiet","when":"read line"}
            }
            """;
        var items = OmarchyMenuScanner.ParseDocuments( [new OmarchyMenuDocument( "menu.jsonc", menu )] );
        True( items.Single( value => value.StableId == "omarchy:style.dark" ).PayloadJson!
            .Contains( "\"checked\":\"[[ 2 -gt 1 ]]\"", StringComparison.Ordinal ) );

        var root = Directory.CreateTempSubdirectory( "commanddeck-omarchy-state-" ).FullName;
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( AsAsync( items ), null, default );
            RecordingBashRunner runner = new( );
            SteppingTimeProvider clock = new( DateTimeOffset.UnixEpoch.AddDays( 20_000 ) );
            using OmarchyMenuState state = new( runner.RunAsync, clock, TimeSpan.FromSeconds( 10 ) );
            CatalogSearchProvider provider = new( catalog, null, state );
            async Task<Dictionary<string, SearchResult>> SearchAsync( ) =>
                (await provider.SearchAsync( QueryParser.Parse( "mode" ), new SearchRequest { Query = "mode" }, default ))
                    .ToDictionary( static value => value.Id, StringComparer.Ordinal );
            var plain = (await new CatalogSearchProvider( catalog ).SearchAsync(
                QueryParser.Parse( "mode" ), new SearchRequest { Query = "mode" }, default ))
                .ToDictionary( static value => value.Id, StringComparer.Ordinal );

            var rows = await SearchAsync( );
            Equal( 1, runner.Calls.Count );
            Equal( "-lc", runner.Calls[0].Arguments[0] );
            True( !runner.Calls[0].Limits.ClearEnvironment && runner.Calls[0].Limits.MaximumMemoryBytes > 0 );
            True( !runner.Calls[0].Arguments[^1].Contains( "mode", StringComparison.Ordinal ) );
            Equal( "true", rows["omarchy:style.dark"].Metadata.GetValueOrDefault( ResultMetadataKeys.OmarchyChecked ) );
            True( !rows["omarchy:style.dark"].Metadata.ContainsKey( ResultMetadataKeys.OmarchyUnavailable ) );
            True( !rows["omarchy:style.light"].Metadata.ContainsKey( ResultMetadataKeys.OmarchyUnavailable ) &&
                !rows["omarchy:style.light"].Metadata.ContainsKey( ResultMetadataKeys.OmarchyChecked ) );
            Equal( "true", rows["omarchy:style.stale"].Metadata.GetValueOrDefault( ResultMetadataKeys.OmarchyUnavailable ) );
            Equal( plain["omarchy:style.stale"].Score * 0.5, rows["omarchy:style.stale"].Score );
            Equal( "true", rows["omarchy:style.quiet"].Metadata.GetValueOrDefault( ResultMetadataKeys.OmarchyUnavailable ) );

            await SearchAsync( );
            Equal( 1, runner.Calls.Count );

            clock.Now = clock.Now.AddSeconds( 21 );
            var stale = await SearchAsync( );
            Equal( "true", stale["omarchy:style.stale"].Metadata.GetValueOrDefault( ResultMetadataKeys.OmarchyUnavailable ) );
            var deadline = DateTime.UtcNow.AddSeconds( 10 );
            while ( runner.Calls.Count < 2 && DateTime.UtcNow < deadline )
            {
                await Task.Delay( 20 );
            }

            Equal( 2, runner.Calls.Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    // A batch that answers nothing is cached like an answer. Concurrent
    // searches share one batch; later searches neither run nor wait for it
    // until it is stale, and then refresh it without waiting. A failed refresh
    // keeps a condition's last real answer.
    private static async Task OmarchyMenuStateCachesUnansweredConditionsAsync( )
    {
        SteppingTimeProvider clock = new( DateTimeOffset.UnixEpoch.AddDays( 20_000 ) );
        var calls = 0;
        var output = "";
        var gate = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        async ValueTask<ProcessResult> RunAsync( string file, IReadOnlyList<string> arguments, TimeSpan timeout,
            ProcessExecutionLimits limits, CancellationToken cancellationToken )
        {
            Interlocked.Increment( ref calls );
            await gate.Task.WaitAsync( cancellationToken );
            return new ProcessResult { ExitCode = 0, StandardOutput = output, StandardError = "" };
        }
        using OmarchyMenuState state = new( RunAsync, clock, TimeSpan.FromSeconds( 5 ) );
        static SearchResult Row( string condition ) => new( )
        {
            Id = "omarchy:" + condition,
            Kind = ResultKinds.Omarchy,
            Title = condition,
            SourceId = SourceIds.OmarchyControl,
            Metadata = new( StringComparer.Ordinal ) { [OmarchyMenuState.CheckedKey] = condition }
        };
        static bool Checked( IReadOnlyList<SearchResult> rows ) =>
            rows[0].Metadata.GetValueOrDefault( ResultMetadataKeys.OmarchyChecked ) == "true";

        var first = state.ApplyAsync( [Row( "broken" )], default ).AsTask( );
        var second = state.ApplyAsync( [Row( "broken" )], default ).AsTask( );
        gate.SetResult( );
        await Task.WhenAll( first, second );
        Equal( 1, calls );
        True( !Checked( first.Result ) && !Checked( second.Result ) );
        True( !Checked( await state.ApplyAsync( [Row( "broken" )], default ) ) );
        Equal( 1, calls );

        gate = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        clock.Now = clock.Now.AddSeconds( 21 );
        var refreshing = state.ApplyAsync( [Row( "broken" )], default );
        True( refreshing.IsCompleted );
        var deadline = DateTime.UtcNow.AddSeconds( 10 );
        while ( Volatile.Read( ref calls ) < 2 && DateTime.UtcNow < deadline )
        {
            await Task.Delay( 20 );
        }

        Equal( 2, calls );
        gate.SetResult( );

        output = "0:1\n";
        True( Checked( await state.ApplyAsync( [Row( "known" )], default ) ) );
        Equal( 3, calls );
        output = "";
        clock.Now = clock.Now.AddSeconds( 21 );
        True( Checked( await state.ApplyAsync( [Row( "known" ), Row( "never-seen" )], default ) ) );
        Equal( 4, calls );
        clock.Now = clock.Now.AddSeconds( 10 );
        True( Checked( await state.ApplyAsync( [Row( "known" )], default ) ) );
        Equal( 4, calls );
    }

    private static void OmarchyMenuStateScriptIsolatesEachCondition( )
    {
        var script = OmarchyMenuState.Script( ["true", "exit 3"] );
        True( script.Contains( "if ( true\n) >/dev/null 2>&1 </dev/null; then echo 0:1; else echo 0:0; fi", StringComparison.Ordinal ) );
        True( script.Contains( "if ( exit 3\n)", StringComparison.Ordinal ) );
        var parsed = OmarchyMenuState.Parse( "0:1\njunk\n1:0\n2:1\n-1:1\n1:x\n", 2 ).ToArray( );
        True( parsed.SequenceEqual( [(0, true), (1, false)] ) );
    }
}
