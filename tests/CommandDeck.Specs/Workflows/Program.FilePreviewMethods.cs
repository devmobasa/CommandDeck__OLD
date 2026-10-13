namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task VerifyPreviewEditorProtocolAsync(
        UnixProtocolClient client,
        string socket,
        string resultId,
        string invocationReference,
        string editorCapture,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 510,
            Operation = ProtocolOperations.Preview,
            ResultId = resultId,
            InvocationReference = invocationReference,
            Query = "content:content -title:ignored"
        }, cancellationToken );
        var matchMessage = await ReadMessageAsync(
            client, 510, ProtocolMessageTypes.PreviewResult, cancellationToken );
        Equal( 3, matchMessage?.Preview?.MatchLine );
        Equal( 10, matchMessage?.Preview?.MatchColumn );
        True( matchMessage?.Preview?.OpenResultId is not null );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 511,
            Operation = ProtocolOperations.Invoke,
            ResultId = matchMessage!.Preview!.OpenResultId,
            InvocationReference = matchMessage.Preview.OpenInvocationReference,
            ActionId = "edit"
        }, cancellationToken );
        var opened = await ReadMessageAsync(
            client, 511, ProtocolMessageTypes.ActionResult, cancellationToken );
        True( opened?.ActionResult?.Succeeded == true );
        var arguments = await File.ReadAllLinesAsync( editorCapture, cancellationToken );
        True( arguments[0].EndsWith( "preview-note.md", StringComparison.Ordinal ) );
        Equal( "3", arguments[1] );
        Equal( "10", arguments[2] );

        await using var otherClient = await ConnectWithRetryAsync( socket );
        await otherClient.SendAsync( new ProtocolRequest
        {
            RequestId = 512,
            Operation = ProtocolOperations.Invoke,
            ResultId = matchMessage.Preview.OpenResultId,
            InvocationReference = matchMessage.Preview.OpenInvocationReference,
            ActionId = "edit"
        }, cancellationToken );
        var foreign = await ReadMessageAsync(
            otherClient, 512, ProtocolMessageTypes.ActionResult, cancellationToken );
        True( foreign?.ActionResult?.Succeeded == false, "a second socket cannot invoke the preview action" );
        var afterForeign = await File.ReadAllLinesAsync( editorCapture, cancellationToken );
        True( arguments.SequenceEqual( afterForeign ),
            "the foreign invocation must not launch the editor" );
    }

    private static async Task FilePreviewMatchesAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-preview-matches-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            var path = Path.Combine( root, "matches.txt" );
            var lines = Enumerable.Range( 1, 500 ).Select( number => $"line {number}" ).ToArray( );
            lines[299] = "A café with context";
            lines[449] = "Another CAFÉ with context";
            await File.WriteAllTextAsync( path, string.Join( "\r\n", lines ) );
            var service = PreviewService( root, false, [Path.Combine( root, "denied.txt" )] );
            var expression = QueryParser.Parse( "title:line content:\"cafe with\" -content:context" ).Expression;

            var first = await service.ReadAsync( path, CancellationToken.None, expression );
            Equal( 300, first.Preview?.MatchLine );
            Equal( 3, first.Preview?.MatchColumn );
            Equal( 295, first.Preview?.StartLine );
            Equal( 2, first.Preview?.MatchCount );
            Equal( 11, first.Preview?.Text.Split( '\n' ).Length );
            True( first.Preview?.Text.Contains( "line 295", StringComparison.Ordinal ) == true );

            var second = await service.ReadAsync( path, CancellationToken.None, expression, 1 );
            Equal( 450, second.Preview?.MatchLine );
            Equal( 1, second.Preview?.MatchIndex );
            var clamped = await service.ReadAsync( path, CancellationToken.None, expression, 255 );
            Equal( 450, clamped.Preview?.MatchLine );
            True( !(await service.ReadAsync( path, CancellationToken.None, expression, 256 )).Succeeded );

            var noMatch = await service.ReadAsync( path, CancellationToken.None,
                QueryParser.Parse( "content:\"cafe context\"" ).Expression );
            Equal( 0, noMatch.Preview?.MatchCount );
            Equal( 1, noMatch.Preview?.StartLine );

            await File.WriteAllTextAsync( path, new string( 'é', 20000 ) + " needle " + new string( 'é', 20000 ) );
            var needle = QueryParser.Parse( "content:needle" ).Expression;
            var longLine = await service.ReadAsync( path, CancellationToken.None, needle );
            True( longLine.Preview?.Text.Contains( "needle", StringComparison.Ordinal ) == true );
            Equal( 20002, longLine.Preview?.MatchColumn );
            True( System.Text.Encoding.UTF8.GetByteCount( longLine.Preview!.Text ) <= ProtocolLimits.MaximumPreviewBytes );

            await File.WriteAllTextAsync( path, new string( 'x', 1024 * 1024 ) + "needle" );
            var limited = await service.ReadAsync( path, CancellationToken.None, needle );
            True( limited.Preview?.ScanTruncated == true );
            Equal( 0, limited.Preview?.MatchCount );

            await File.WriteAllTextAsync( path, string.Join( '\n', Enumerable.Repeat( "needle", 300 ) ) );
            var capped = await service.ReadAsync( path, CancellationToken.None, needle );
            Equal( 256, capped.Preview?.MatchCount );
            True( capped.Preview?.ScanTruncated == true );

            await File.WriteAllTextAsync( path, "needle\0secret" );
            True( !(await service.ReadAsync( path, CancellationToken.None, needle )).Succeeded );
            var denied = Path.Combine( root, "denied.txt" );
            await File.WriteAllTextAsync( denied, "needle" );
            True( !(await service.ReadAsync( denied, CancellationToken.None, needle )).Succeeded );

            using var canceled = new CancellationTokenSource( );
            canceled.Cancel( );
            await ThrowsAsync<OperationCanceledException>( async ( ) =>
                await service.ReadAsync( path, canceled.Token, needle ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
