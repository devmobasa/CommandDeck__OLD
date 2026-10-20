namespace CommandDeck.Specs;

public static partial class Program
{
    private const string SequenceNotStarted =
        "Start a clipboard sequence first: run Restart clipboard sequence, or use Start sequence in Clipboard History.";

    private const string SequenceNeedsConsent = "Enable clipboard capture before using clipboard history here.";
    private const string SequenceEnded = "The clipboard sequence has no newer entry. Copy something, or start the sequence again.";

    private static async Task ClipboardSequenceRestartCopiesLaterEntriesInOrderAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-sequence-" ).FullName;
        try
        {
            ClipboardSequenceClock clock = new( new DateTimeOffset( 2026, 9, 6, 12, 0, 0, TimeSpan.Zero ) );
            var path = Path.Combine( root, "history.db" );
            using ( ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions { MaximumEntries = 20, RetentionHours = 24 }, clock ) )
            {
                ClipboardSequenceFixture fixture = new( store );
                Equal( SequenceNeedsConsent, (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
                Equal( SequenceNeedsConsent, (await fixture.RunAsync( ClipboardSequenceCommands.RestartId )).Message );
                store.Enabled = true;
                Equal( SequenceNotStarted, (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );

                // Restart begins with the next entry copied; earlier history is not replayed.
                store.Capture( "before" );
                True( (await fixture.RunAsync( ClipboardSequenceCommands.RestartId )).Succeeded );
                Equal( SequenceEnded, (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
                await FeedNativeAdmissionAsync( store, ["text/plain"], ["one"u8.ToArray( )] );
                store.Capture( "two" );
                EnableRichFixture( store );
                await FeedRichAdmissionAsync( store, ["text/plain"], ["three"u8.ToArray( )] );

                clock.Tick( );
                Equal( "Copied clipboard sequence entry 1; 2 more queued.", (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
                fixture.FailNextServe = true;
                Equal( "Clipboard copy failed; the sequence did not advance.", (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
                True( store.Pin( store.Search( "three", 1 ).Single( ).Id, true ), "pins do not reorder the sequence" );
                Equal( "Copied clipboard sequence entry 2; 1 more queued.", (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
                Equal( "Copied clipboard sequence entry 3; 0 more queued.", (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
                Equal( SequenceEnded, (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
                Equal( "one,two,three", string.Join( ',', fixture.Served ) );

                // Entries copied later, including a recopied older one, join the end; deleted ones are skipped.
                foreach ( var text in new[] { "four", "before", "five", "six" } )
                {
                    clock.Tick( );
                    store.Capture( text );
                }

                store.Delete( store.Search( "five", 1 ).Single( ).Id );
                for ( var step = 0; step < 3; step++ )
                {
                    await fixture.RunAsync( ClipboardSequenceCommands.NextId );
                }

                Equal( "one,two,three,four,before,six", string.Join( ',', fixture.Served ) );
                Equal( SequenceEnded, (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );

                // Pausing keeps the sequence; Clear and withdrawing consent end it.
                store.SetCapturePolicy( store.Policy.Revision, ClipboardConsent.Text, paused: true );
                Equal( SequenceEnded, (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
                store.SetCapturePolicy( store.Policy.Revision, ClipboardConsent.Text, paused: false );
                store.Clear( );
                Equal( SequenceNotStarted, (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
                True( (await fixture.RunAsync( ClipboardSequenceCommands.RestartId )).Succeeded );
                store.Enabled = false;
                store.Enabled = true;
                Equal( SequenceNotStarted, (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
                True( (await fixture.RunAsync( ClipboardSequenceCommands.RestartId )).Succeeded );
            }

            // The position lives only in daemon memory.
            using ClipboardHistoryStore reopened = new( path, new ClipboardHistoryOptions { MaximumEntries = 20, RetentionHours = 24 }, clock );
            Equal( SequenceNotStarted, (await new ClipboardSequenceFixture( reopened ).RunAsync( ClipboardSequenceCommands.NextId )).Message );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardSequenceStartsAtBrowserAndSearchEntriesAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-sequence-" ).FullName;
        try
        {
            ClipboardSequenceClock clock = new( new DateTimeOffset( 2026, 9, 6, 12, 0, 0, TimeSpan.Zero ) );
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ),
                new ClipboardHistoryOptions { MaximumEntries = 20, RetentionHours = 24 }, clock );
            store.Enabled = true;
            foreach ( var text in new[] { "a", "b", "c", "d" } )
            {
                store.Capture( text );
            }

            ClipboardSequenceFixture fixture = new( store );
            store.StartSequenceAt( store.Search( "d", 1 ).Single( ).Id );
            await fixture.RunAsync( ClipboardSequenceCommands.NextId );
            Equal( SequenceEnded, (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
            Equal( "d", string.Join( ',', fixture.Served ) );
            fixture.Served.Clear( );

            // The browser starts at a snapshot entry: that entry, then every newer one by last copy.
            ClipboardHistoryService service = new( store );
            var request = OpenClipboardCopyRequest( service );
            var entries = service.Query( "owner", request.Clipboard!.SessionId!, new( ), false, 2 ).Entries;
            var b = entries.Single( entry => store.ReadDocument( entry.Id, entry.Incarnation )!.Text == "b" );
            var started = await service.HandleAsync( "owner", SequenceStartRequest( request, b, 2, ClipboardActionIds.SequenceStart ), default );
            Equal( "Clipboard sequence starts here with 3 entries.", started.Action!.Message );
            Throws<InvalidOperationException>( ( ) => service.Handle( "owner", SequenceStartRequest( request, b, 1, ClipboardActionIds.SequenceStart ) ) );
            Throws<InvalidOperationException>( ( ) => service.Handle( "owner", SequenceStartRequest( request, b, 2, ClipboardSequenceCommands.NextAction ) ) );

            // Copying moves an entry to the newest position without copying it twice.
            await fixture.RunAsync( ClipboardSequenceCommands.NextId );
            clock.Tick( );
            await fixture.RunAsync( ClipboardSequenceCommands.NextId );
            clock.Tick( );
            store.Capture( "a" );
            clock.Tick( );
            await fixture.RunAsync( ClipboardSequenceCommands.NextId );
            clock.Tick( );
            await fixture.RunAsync( ClipboardSequenceCommands.NextId );
            clock.Tick( );
            Equal( SequenceEnded, (await fixture.RunAsync( ClipboardSequenceCommands.NextId )).Message );
            Equal( "b,c,d,a", string.Join( ',', fixture.Served ) );

            // clip: rows offer the same start; last-copy order is now b, c, d, a.
            True( store.Pin( store.Search( "d", 1 ).Single( ).Id, true ), "pins do not reorder the sequence" );
            var row = (await fixture.History.SearchAsync( QueryParser.Parse( "clip: c" ), new SearchRequest { Query = "clip: c" }, default ))
                .Single( result => result.DetailMarkdown == "c" );
            var action = row.Actions.Single( candidate => candidate.Id == ClipboardActionIds.SequenceStart );
            Equal( "Clipboard sequence starts here with 3 entries.", (await fixture.Executor.ExecuteAsync( row, action, "clip: c", default )).Message );
            for ( var step = 0; step < 3; step++ )
            {
                await fixture.RunAsync( ClipboardSequenceCommands.NextId );
            }

            Equal( "b,c,d,a,c,d,a", string.Join( ',', fixture.Served ) );
            clock.Tick( );
            await ClipboardSequenceTextFallbackAsync( store );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    // Without native serving, plain-text entries are copied with wl-copy.
    private static async Task ClipboardSequenceTextFallbackAsync( ClipboardHistoryStore store )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-action-payload-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        var originalFixture = Environment.GetEnvironmentVariable( ActionPayloadFixtureDirectory );
        try
        {
            File.CreateSymbolicLink( Path.Combine( root, "wl-copy" ), Environment.ProcessPath! );
            Environment.SetEnvironmentVariable( "PATH", root );
            Environment.SetEnvironmentVariable( ActionPayloadFixtureDirectory, root );
            LinuxActionExecutor executor = new( clipboardHistory: new( store ) );
            var restart = ClipboardSequenceRow( ClipboardSequenceCommands.RestartId );
            True( (await executor.ExecuteAsync( restart, restart.Actions[0], "", default )).Succeeded );
            store.Capture( "plain fallback" );
            var next = ClipboardSequenceRow( ClipboardSequenceCommands.NextId );
            Equal( "Copied clipboard sequence entry 1; 0 more queued.", (await executor.ExecuteAsync( next, next.Actions[0], "", default )).Message );
            Equal( "plain fallback", await File.ReadAllTextAsync( Path.Combine( root, "stdin" ) ) );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Environment.SetEnvironmentVariable( ActionPayloadFixtureDirectory, originalFixture );
            Directory.Delete( root, recursive: true );
        }
    }

    private static ProtocolRequest SequenceStartRequest( ProtocolRequest open, ClipboardEntrySummary entry, long queryGeneration, string action ) => new( )
    {
        RequestId = 2,
        Operation = ProtocolOperations.ClipboardAction,
        Clipboard = new( )
        {
            FrontendInstance = open.Clipboard!.FrontendInstance,
            OpenGeneration = 1,
            SessionId = open.Clipboard.SessionId,
            QueryGeneration = queryGeneration,
            EntryId = entry.Id,
            Incarnation = entry.Incarnation,
            EntryRevision = entry.Revision,
            Action = action
        }
    };

    private static SearchResult ClipboardSequenceRow( string id ) => new( )
    {
        Id = id,
        Title = id,
        Kind = ResultKinds.CustomCommand,
        SourceId = SourceIds.CommandsQuicklinks,
        ProviderId = ProviderIds.Commands,
        Actions = [ClipboardSequenceCommands.CreateAction( id )]
    };

    private sealed class ClipboardSequenceFixture
    {
        public ClipboardSequenceFixture( ClipboardHistoryStore store )
        {
            History = new( store, serve: Serve );
            Executor = new( clipboardHistory: History );
        }

        public ClipboardHistoryProvider History { get; }
        public LinuxActionExecutor Executor { get; }
        public List<string> Served { get; } = [];
        public bool FailNextServe { get; set; }

        public async Task<ActionExecutionResult> RunAsync( string commandId )
        {
            var row = ClipboardSequenceRow( commandId );
            return await Executor.ExecuteAsync( row, row.Actions[0], "", default );
        }

        private ValueTask<ActionExecutionResult> Serve( IReadOnlyList<ClipboardServeOffer> offers, CancellationToken cancellationToken )
        {
            if ( FailNextServe )
            {
                FailNextServe = false;
                return ValueTask.FromResult( ActionExecutionResult.Failure( "Serving is busy." ) );
            }

            using MemoryStream bytes = new( );
            offers[0].Lease.Stream.Position = 0;
            offers[0].Lease.Stream.CopyTo( bytes );
            Served.Add( System.Text.Encoding.UTF8.GetString( bytes.ToArray( ) ) );
            return ValueTask.FromResult( ActionExecutionResult.Success( ) );
        }
    }

    private sealed class ClipboardSequenceClock( DateTimeOffset start ) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Tick( ) => _now += TimeSpan.FromSeconds( 1 );

        public override DateTimeOffset GetUtcNow( ) => _now;
    }
}
