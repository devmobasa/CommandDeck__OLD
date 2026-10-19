namespace CommandDeck.Specs;

public static partial class Program
{
    // favorite.move takes the cached result and its new position among
    // bookmarks, clamped to the list.
    private static async Task VerifyFavoriteMoveProtocolAsync(
        UnixProtocolClient client,
        ILauncherPreferenceStore preferencesStore,
        string resultId,
        CancellationToken cancellationToken )
    {
        SearchResult second = new( )
        {
            Id = "file:protocol-second",
            Kind = ResultKinds.File,
            Title = "Second protocol favorite",
            ProviderId = "files",
            SourceId = SourceIds.Files,
            CanCustomize = true
        };
        await preferencesStore.SetFavoriteAsync( second, true, CancellationToken.None );
        async Task<List<string>> MoveAsync( long requestId, int position )
        {
            await client.SendAsync( new ProtocolRequest
            {
                RequestId = requestId,
                Operation = ProtocolOperations.FavoriteMove,
                ResultId = resultId,
                Offset = position
            }, CancellationToken.None );
            var moved = await ReadMessageAsync( client, requestId, ProtocolMessageTypes.PreferencesResult, cancellationToken );
            ArgumentNullException.ThrowIfNull( moved?.Preferences );
            return [.. moved.Preferences.Customizations.Select( static value => value.StableId )];
        }
        var later = await MoveAsync( 150, 9 );
        True( later.IndexOf( second.Id ) < later.IndexOf( resultId ) );
        var earlier = await MoveAsync( 151, 0 );
        True( earlier.IndexOf( resultId ) < earlier.IndexOf( second.Id ) );
        await preferencesStore.SetFavoriteAsync( second, false, CancellationToken.None );
    }
}
