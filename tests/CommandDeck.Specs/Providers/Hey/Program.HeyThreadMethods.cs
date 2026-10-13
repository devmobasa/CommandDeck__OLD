namespace CommandDeck.Specs;

public static partial class Program
{
    private const string HeyThreadMarkdown = "## Jane Cooper · 21 Sep 2026 · #7001\r\n\r\nattached is the\u0007 invoice​\tfor you\r\n";

    private static async Task HeyClientReadsWholeThreadsAsync( )
    {
        HeyFake fake = new( )
        {
            Respond = _ => HeyResult( HeyThreadMarkdown )
        };
        var thread = await fake.Client( ).ReadThreadAsync( 9001, CancellationToken.None );

        Equal( "thread|read|9001|--markdown", string.Join( '|', fake.Calls.Single( ).Args ) );
        Equal( HeyClient.ThreadTimeout, fake.Calls[0].Timeout );
        Equal( HeyClient.ThreadStandardOutputBytes, fake.Calls[0].Limits.MaximumStandardOutputBytes );
        Equal( ( HeyFailure? ) null, thread.Failure );
        True( !thread.Trimmed );
        Equal( "## Jane Cooper · 21 Sep 2026 · #7001\n\nattached is the invoice\tfor you", thread.Markdown );

        HeyFake longer = new( )
        {
            Respond = _ => HeyResult( new string( 'a', HeyClient.MaximumThreadCharacters - 1 ) + "😀 tail" )
        };
        var trimmed = await longer.Client( ).ReadThreadAsync( 9001, CancellationToken.None );
        True( trimmed.Trimmed );
        Equal( HeyClient.MaximumThreadCharacters - 1, trimmed.Markdown.Length );

        async Task<HeyFailureKind?> KindFor( ProcessResult result )
        {
            HeyFake failing = new( )
            {
                Respond = _ => result
            };
            return (await failing.Client( ).ReadThreadAsync( 9001, CancellationToken.None )).Failure?.Kind;
        }

        Equal( HeyFailureKind.SignedOut, await KindFor( HeyResult( HeySignedOutJson, 1 ) ) );
        Equal( HeyFailureKind.Rejected, await KindFor( HeyResult( """{"ok":false,"error":"thread not found","code":"not_found"}""", 1 ) ) );
        Equal( HeyFailureKind.Failed, await KindFor( HeyResult( "", 4 ) ) );
        Equal( HeyFailureKind.TimedOut, await KindFor( new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", TimedOut = true } ) );
        Equal( HeyFailureKind.Failed, await KindFor( new ProcessResult { ExitCode = -1, StandardOutput = "", StandardError = "", OutputLimitExceeded = true } ) );

        HeyClient missing = new( fake.Runner, static ( ) => null, HeyEnvironment );
        Equal( HeyFailureKind.NotInstalled, (await missing.ReadThreadAsync( 9001, CancellationToken.None )).Failure?.Kind );
        await ThrowsAsync<ArgumentOutOfRangeException>( async ( ) => await fake.Client( ).ReadThreadAsync( 0, CancellationToken.None ) );
    }

    // Read Thread shows the text in the output dialog, titled with the subject.
    private static async Task HeyReadThreadShowsTheTextAsync( )
    {
        HeyFake fake = new( )
        {
            Respond = _ => HeyResult( HeyThreadMarkdown )
        };
        LinuxActionExecutor executor = new( hey: fake.Client( ) );
        var row = HeyPrivateRow( );
        CommandAction Read( string topicId ) => new( )
        {
            Id = HeyProvider.ReadActionId,
            Title = "Read Thread",
            Kind = ActionKinds.HeyReadThread,
            Arguments = new( StringComparer.Ordinal )
            {
                ["topicId"] = topicId
            }
        };

        var shown = await executor.ExecuteAsync( row, Read( "9001" ), "", default );
        True( shown.Succeeded );
        Equal( row.Title, shown.Message );
        True( shown.DetailMarkdown!.StartsWith( "## Jane Cooper", StringComparison.Ordinal ) );

        HeyFake longer = new( )
        {
            Respond = _ => HeyResult( new string( 'a', HeyClient.MaximumThreadCharacters + 10 ) )
        };
        var trimmed = await new LinuxActionExecutor( hey: longer.Client( ) ).ExecuteAsync( row, Read( "9001" ), "", default );
        True( trimmed.DetailMarkdown!.EndsWith( "The rest of this thread is in HEY.", StringComparison.Ordinal ) );

        HeyFake signedOut = new( )
        {
            Respond = _ => HeyResult( HeySignedOutJson, 1 )
        };
        var refused = await new LinuxActionExecutor( hey: signedOut.Client( ) ).ExecuteAsync( row, Read( "9001" ), "", default );
        True( !refused.Succeeded );
        Equal( "Not logged in", refused.Message );

        var calls = fake.Calls.Count;
        True( !(await executor.ExecuteAsync( row, Read( "-1" ), "", default )).Succeeded );
        True( !(await executor.ExecuteAsync( row, Read( "9001 --html" ), "", default )).Succeeded );
        Equal( calls, fake.Calls.Count );
        True( !(await new LinuxActionExecutor( ).ExecuteAsync( row, Read( "9001" ), "", default )).Succeeded );
    }
}
