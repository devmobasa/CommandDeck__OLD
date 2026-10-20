namespace CommandDeck.Specs;

public static partial class Program
{
    // favorite.move swaps a cached bookmark with the shown bookmark it names,
    // so a bookmark ahead of both (one a view leaves out) keeps its place.
    private static async Task VerifyFavoriteMoveProtocolAsync(
        UnixProtocolClient client,
        string firstFavoriteId,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 150,
            Operation = ProtocolOperations.Search,
            Query = "context-report"
        }, CancellationToken.None );
        List<string> reports = [];
        await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ) )
        {
            var message = DeserializeMessage( line );
            if ( message?.Type == ProtocolMessageTypes.Result && message.RequestId == 150 &&
                message.Result is { Kind: ResultKinds.File, CanCustomize: true } )
            {
                reports.Add( message.Result.Id );
            }

            if ( message?.Type == ProtocolMessageTypes.Done && message.RequestId == 150 )
            {
                break;
            }
        }

        Equal( 2, reports.Count );
        async Task<string> SendAsync( ProtocolRequest request, string type )
        {
            await client.SendAsync( request, CancellationToken.None );
            var reply = await ReadMessageAsync( client, request.RequestId, type, cancellationToken );
            ArgumentNullException.ThrowIfNull( reply );
            return string.Join( ' ', reply.Preferences?.Customizations
                .Where( static value => value.IsFavorite ).Select( static value => value.StableId ) ?? [] );
        }
        async Task BookmarkAsync( long requestId, string resultId, bool enabled ) =>
            await SendAsync( new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.FavoriteSet,
                ResultId = resultId,
                Enabled = enabled
            }, ProtocolMessageTypes.CustomizationResult );
        Task<string> MoveAsync( long requestId, string resultId, string targetResultId ) =>
            SendAsync( new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.FavoriteMove,
                ResultId = resultId,
                TargetResultId = targetResultId
            }, ProtocolMessageTypes.PreferencesResult );

        await BookmarkAsync( 151, reports[0], true );
        await BookmarkAsync( 152, reports[1], true );
        Equal( $"{firstFavoriteId} {reports[1]} {reports[0]}", await MoveAsync( 153, reports[1], reports[0] ) );
        Equal( $"{firstFavoriteId} {reports[0]} {reports[1]}", await MoveAsync( 154, reports[1], reports[0] ) );

        await BookmarkAsync( 155, reports[1], false );
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 156,
            Operation = ProtocolOperations.FavoriteMove,
            ResultId = reports[0],
            TargetResultId = reports[1]
        }, CancellationToken.None );
        var refused = await ReadMessageAsync( client, 156, ProtocolMessageTypes.Error, cancellationToken );
        True( refused?.Error?.Contains( "bookmarked", StringComparison.Ordinal ) == true );
        await BookmarkAsync( 157, reports[0], false );
    }
}
