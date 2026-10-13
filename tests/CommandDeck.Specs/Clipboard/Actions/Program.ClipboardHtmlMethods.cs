using System.Text;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardHtmlProjectsInertText( )
    {
        var result = HtmlProjection( "<head><title>secret</title></head><p>Hello &amp; <b>world</b></p>" +
            "<script>secret</script><style>secret</style><template>secret</template><object>secret</object>" +
            "<iframe>secret</iframe><div hidden>secret</div><div aria-hidden='TRUE'>secret</div>" +
            "<p><a href='https://attribute.invalid'>visible</a><img src='https://resource.invalid/pixel'></p>" +
            "<pre> x  y\n z\n</pre>" );
        True( result.Available );
        Equal( "Hello & world\nvisible\n x  y\n z\n", result.Text );
        True( !result.Truncated );
        Equal( "one two\nthree", HtmlProjection( "<div>one \n <span> two </span><br>three</div>" ).Text );
        Equal( "😀 ©", HtmlProjection( "\uFEFF<p>&#x1F600; &copy;</p>" ).Text );
        Equal( "after", HtmlProjection( "<div hidden><p>hidden</p></div><p>after</p>" ).Text );
        True( HtmlProjection( "<math><annotation-xml encoding='text/html'><script>secret</script>" +
            "<p>visible</p></annotation-xml></math>" ).Text == "visible" );
    }

    private static void ClipboardHtmlBoundsAndRejectsInvalidInputs( )
    {
        foreach ( var markup in new[] { "<meta charset='windows-1252'><p>safe</p>",
            "<meta http-equiv='Content-Type' content='text/html; charset=iso-8859-1'>safe" } )
        {
            var rejected = HtmlProjection( markup );
            True( !rejected.Available );
            Equal( "", rejected.Text );
            Equal( "ConflictingCharset", rejected.Reason );
        }

        True( HtmlProjection( "<meta charset=UTF-8><p>safe</p>" ).Available );
        True( !ClipboardHtmlProjector.Project( [0xff] ).Available );
        True( !ClipboardHtmlProjector.Project( new byte[1024 * 1024 + 1] ).Available );
        var truncated = HtmlProjection( new string( 'x', 32766 ) + "😀never" );
        Equal( new string( 'x', 32766 ), truncated.Text );
        True( truncated.Truncated );
        var exact = HtmlProjection( "<p>" + new string( 'x', 32768 ) + "</p>" );
        Equal( 32768, Encoding.UTF8.GetByteCount( exact.Text ) );
        True( !exact.Truncated, "a discarded final block separator does not truncate source text" );
        Equal( "StructureLimit", HtmlProjection( string.Concat( Enumerable.Repeat( "<div>", 257 ) ) + "text" ).Reason );
        Equal( "StructureLimit", HtmlProjection( string.Concat( Enumerable.Repeat( "<i></i>", 100001 ) ) ).Reason );
    }

    private static async Task ClipboardHtmlWorkerBoundsAndCancellationAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        using CancellationTokenSource cancellation = new( );
        TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource reaped = new( TaskCreationOptions.RunContinuationsAsynchronously );
        string? snapshot = null;
        ClipboardHtmlProjectionService service = new( Environment.ProcessPath!, async ( file, args, timeout, limits, token ) =>
        {
            Equal( "/usr/bin/bwrap", file );
            Equal( TimeSpan.FromSeconds( 2 ), timeout );
            Equal( 192L * 1024 * 1024, limits.MaximumMemoryBytes );
            Equal( 4096, limits.MaximumStandardErrorBytes );
            True( limits.ClearEnvironment );
            True( args.Contains( "--unshare-all" ) && args.Contains( "--disable-userns" ) );
            True( !args.Contains( "--share-net" ) );
            var inputIndex = args.ToList( ).IndexOf( "/input" );
            snapshot = args[inputIndex - 1];
            Equal( "<p>fixture</p>", await File.ReadAllTextAsync( snapshot, token ) );
            if ( OperatingSystem.IsLinux( ) )
            {
                Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode( snapshot ) );
            }
            entered.SetResult( );
            try
            {
                await Task.Delay( Timeout.Infinite, token );
            }
            catch ( OperationCanceledException )
            {
                await reaped.Task;
                throw;
            }

            throw new InvalidOperationException( "Unreachable fixture." );
        } );
        var work = service.ProjectAsync( "<p>fixture</p>"u8.ToArray( ), cancellation.Token ).AsTask( );
        await entered.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
        cancellation.Cancel( );
        try
        {
            Equal( "WorkerBusy", (await service.ProjectAsync( "safe"u8.ToArray( ), CancellationToken.None )).Reason );
            True( File.Exists( snapshot ), "input and slot survive until child has been reaped" );
        }
        finally
        {
            reaped.TrySetResult( );
        }

        await ThrowsAsync<OperationCanceledException>( ( ) => work );
        True( !File.Exists( snapshot ) );
        ClipboardHtmlProjectionService failed = new( Environment.ProcessPath!, ( _, _, _, _, _ ) => ValueTask.FromResult(
            new ProcessResult { ExitCode = 1, StandardOutput = "<b>raw</b>", StandardError = "private diagnostic" } ) );
        var failure = await failed.ProjectAsync( "<b>raw</b>"u8.ToArray( ), CancellationToken.None );
        True( !failure.Available && failure.Text.Length == 0 );
    }

    private static async Task<int> RunClipboardHtmlAotAsync( string executable )
    {
        ClipboardHtmlProjectionService service = new( executable );
        var result = await service.ProjectAsync( Encoding.UTF8.GetBytes(
            "<meta charset=utf-8><p>AOT &amp; 😀</p><script>hidden</script><img src='https://invalid.invalid/x'>" ), CancellationToken.None );
        Equal( "AOT & 😀", result.Text );
        True( result.Available && !result.Truncated, JsonSerializer.Serialize( result, CommandDeckJsonContext.Default.ClipboardHtmlProjection ) );
        var hostile = await service.ProjectAsync( Encoding.UTF8.GetBytes( string.Concat( Enumerable.Repeat( "<div>", 257 ) ) ), CancellationToken.None );
        True( !hostile.Available && hostile.Text.Length == 0 );
        Console.WriteLine( "Native AOT clipboard HTML: isolated projection and structural rejection passed." );
        return 0;
    }

    private static ClipboardHtmlProjection HtmlProjection( string text ) => ClipboardHtmlProjector.Project( Encoding.UTF8.GetBytes( text ) );
}
