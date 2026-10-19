namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SnippetInputsAndExpansionAsync( )
    {
        const string template = "Review {topic} on {date} at {time}: {clipboard}. Again: {topic}";
        SnippetProvider provider = new( [new( ) { Id = "review", Title = "Review prompt", Text = template }] );
        var rows = await provider.SearchAsync( QueryParser.Parse( "snip: review" ), new SearchRequest { Query = "snip: review" }, default );
        Equal( 1, rows.Count );
        Equal( 1, rows[0].Actions[0].Inputs.Count );
        var action = ActionInputValues.Bind( rows[0].Actions[0], new Dictionary<string, string> { ["topic"] = "{clipboard} $(touch /tmp/never)" } );
        var text = SnippetTemplate.Expand( template, action.Arguments,
            new SnippetClock( new DateTimeOffset( 2026, 9, 6, 14, 30, 0, TimeSpan.Zero ), TimeZoneInfo.Utc ),
            new Dictionary<int, string> { [0] = "literal {date}" } );
        Equal( "Review {clipboard} $(touch /tmp/never) on 2026-09-06 at 14:30: literal {date}. Again: {clipboard} $(touch /tmp/never)", text );
        Throws<InvalidOperationException>( ( ) => ActionInputValues.Bind( rows[0].Actions[0], null ) );
        Throws<InvalidOperationException>( ( ) => ActionInputValues.Bind( rows[0].Actions[0], new Dictionary<string, string> { ["argv"] = "sh" } ) );
        Throws<InvalidOperationException>( ( ) => ActionInputValues.Validate( new Dictionary<string, string> { ["topic"] = new string( 'a', 8193 ) } ) );
        Throws<InvalidOperationException>( ( ) => ActionInputValues.Bind( new CommandAction
        {
            Id = "default",
            Title = "Default",
            Kind = ActionKinds.SnippetCopy,
            Inputs = [new( ) { Name = "topic", DefaultValue = new string( 'a', 8193 ) }]
        }, null ) );
        var previewAction = new CommandAction
        {
            Id = "preview",
            Title = "Preview",
            Kind = ActionKinds.SnippetPreview,
            Arguments = new( StringComparer.Ordinal ) { ["template"] = "Hello {name}", ["input:name"] = "Ada" }
        };
        var preview = await new LinuxActionExecutor( ).ExecuteAsync( rows[0], previewAction, "", default );
        True( preview.Succeeded, "preview succeeds without clipboard tools" );
        Equal( "Hello Ada", preview.DetailMarkdown );
        var wire = System.Text.Json.JsonSerializer.Serialize( new ProtocolRequest
        {
            Version = 3,
            RequestId = 1,
            Operation = ProtocolOperations.Invoke,
            ResultId = rows[0].Id,
            ActionId = rows[0].Actions[0].Id,
            InputValues = new( StringComparer.Ordinal ) { ["topic"] = "test" }
        }, CommandDeckJsonContext.Default.ProtocolRequest );
        var decoded = System.Text.Json.JsonSerializer.Deserialize( wire, CommandDeckJsonContext.Default.ProtocolRequest )!;
        ProtocolRequestValidator.Validate( decoded );
        Equal( "test", decoded.InputValues!["topic"] );
    }

    private static async Task SnippetClipboardReadIsBoundedAsync( )
    {
        var exceeded = false;
        var calls = 0;
        BoundedProcessRunner runner = ( executable, arguments, timeout, limits, _ ) =>
        {
            calls++;
            Equal( "wl-paste", executable );
            Equal( "--no-newline,--type,text", string.Join( ',', arguments ) );
            Equal( TimeSpan.FromSeconds( 2 ), timeout );
            Equal( 64 * 1024, limits.MaximumStandardOutputBytes );
            Equal( 4 * 1024, limits.MaximumStandardErrorBytes );
            True( limits.ClearEnvironment, "clipboard reader receives an explicit environment" );
            True( limits.EnvironmentVariables is not null );
            True( limits.EnvironmentVariables!.ContainsKey( "PATH" ) );
            True( limits.EnvironmentVariables.Keys.All( static name =>
                name is "PATH" or "LANG" or "XDG_RUNTIME_DIR" or "WAYLAND_DISPLAY" ) );

            return ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = exceeded ? string.Empty : "🙂",
                StandardError = string.Empty,
                OutputLimitExceeded = exceeded
            } );
        };
        var action = new CommandAction
        {
            Id = "preview",
            Title = "Preview",
            Kind = ActionKinds.SnippetPreview,
            Arguments = new( StringComparer.Ordinal ) { ["template"] = "Clipboard: {clipboard}" }
        };
        SearchResult result = new( )
        {
            Id = "snippet:test",
            Kind = ResultKinds.CustomCommand,
            SourceId = SourceIds.Snippets,
            Title = "Test"
        };
        LinuxActionExecutor executor = new( snippetClipboardRunner: runner );

        var preview = await executor.ExecuteAsync( result, action, "", default );
        True( preview.Succeeded );
        Equal( "Clipboard: 🙂", preview.DetailMarkdown );

        exceeded = true;
        var rejected = await executor.ExecuteAsync( result, action, "", default );
        True( !rejected.Succeeded );
        Equal( "Clipboard text exceeds 64 KiB.", rejected.Message );
        Equal( 2, calls );
    }
}
