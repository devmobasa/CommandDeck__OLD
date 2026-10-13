using System.Diagnostics;
using System.Security.Cryptography;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExerciseClipboardNativeServingAsync( Process server, string root, string socket,
        string? executable, CancellationToken token )
    {
        using ClipboardHistoryStore store = new( Path.Combine( root, "served.db" ), new ClipboardHistoryOptions( ) );
        EnableRichFixture( store );
        await FeedRichAdmissionAsync( store, ["text/plain", "text/html"], ["served text"u8.ToArray( ), "<b>served</b>"u8.ToArray( )] );
        var starts = 0;
        await using ClipboardServing serving = new( ( ) =>
        {
            Interlocked.Increment( ref starts );
            var start = ClipboardPrivateFixtureStart( "--clipboard-native-fixture", root );
            start.Environment["WAYLAND_DISPLAY"] = socket;
            if ( executable is not null )
            {
                start.FileName = Path.GetFullPath( executable );
                start.ArgumentList.Clear( );
                start.ArgumentList.Add( "clipboard-host" );
                start.ArgumentList.Add( "--capture" );
            }

            return start;
        } );

        var identity = store.SnapshotEntries( new( ), false ).Single( );
        var revision = store.ReadDocument( identity.Id, identity.Incarnation )!.Summary.Revision;
        using ( var selection = store.ReadCopySelection( identity.Id, identity.Incarnation, revision ) )
        {
            Equal( 2, selection.Offers.Count );
            True( (await serving.ServeAsync( selection.Offers, token )).Succeeded, "retained offers are served natively" );
        }

        Equal( "text/plain,text/html," + ClipboardOfferPlan.OwnerMime, await Command( "offers" ) );
        Equal( Convert.ToBase64String( "<b>served</b>"u8 ), await Command( "paste text/html" ) );
        Equal( Convert.ToBase64String( "served text"u8 ), await Command( "paste text/plain" ) );
        Equal( "", await Command( "paste " + ClipboardOfferPlan.OwnerMime ) );

        // Cancelling a write in flight must discard it before another manifest reaches the same helper.
        using ( ClipboardPayloadDirectory payloads = new( Path.Combine( root, "cancelled.data" ) ) )
        {
            var payloadId = Guid.NewGuid( );
            var bytes = new byte[ClipboardServeProtocol.MaximumOfferBytes];
            using ( var writer = payloads.CreateStaging( payloadId ) )
            {
                writer.Stream.Write( bytes );
            }

            payloads.Publish( payloadId );
            using var lease = payloads.OpenRead( payloadId );
            using CancellationTokenSource cancellation = new( );
            var cancelledCopy = serving.ServeAsync(
                [new( "application/x-commanddeck-cancel-fixture", lease, bytes.Length, Convert.ToHexStringLower( SHA256.HashData( bytes ) ) )],
                cancellation.Token ).AsTask( );
            await Task.Delay( 5, token );
            True( !cancelledCopy.IsCompleted, "large synthetic write remains in flight until cancellation" );
            cancellation.Cancel( );
            await ThrowsAsync<OperationCanceledException>( ( ) => cancelledCopy );
        }

        Equal( "text/plain,text/html," + ClipboardOfferPlan.OwnerMime, await Command( "offers" ) );

        using ( var selection = store.ReadCopySelection( identity.Id, identity.Incarnation, revision ) )
        {
            True( (await serving.ServeAsync( selection.Offers, token )).Succeeded,
                "a new write is admitted after cancellation cleanup" );
        }

        Equal( 1, starts );

        // A damaged digest fails closed before any selection is set, and the helper is replaced afterwards.
        using ( var selection = store.ReadCopySelection( identity.Id, identity.Incarnation, revision ) )
        {
            var damaged = selection.Offers.Select( offer => offer with { Digest = new string( '0', 64 ) } ).ToArray( );
            True( !(await serving.ServeAsync( damaged, token )).Succeeded, "digest mismatch rejects serving" );
        }

        Equal( "text/plain,text/html," + ClipboardOfferPlan.OwnerMime, await Command( "offers" ) );

        // The browser copy path replaces the served selection, records the recopy and keeps the old identity.
        await FeedRichAdmissionAsync( store, ["text/plain"], ["second"u8.ToArray( )] );
        ClipboardHistoryService service = new( store, serve: serving.ServeAsync );
        service.RegisterOwner( "one" );
        var instance = Guid.NewGuid( ).ToString( "N" );
        var session = service.Open( "one", instance, 1 );
        var second = service.Query( "one", session, QueryParser.Parse( "second" ).Expression, false, 1 ).Entries.Single( );
        var before = store.StoreRevision;
        var response = await service.HandleAsync( "one", new( )
        {
            RequestId = 1,
            Operation = ProtocolOperations.ClipboardAction,
            Clipboard = new( )
            {
                FrontendInstance = instance,
                OpenGeneration = 1,
                SessionId = session,
                EntryId = second.Id,
                Incarnation = second.Incarnation,
                EntryRevision = second.Revision,
                QueryGeneration = 1,
                Action = "copy"
            }
        }, token );
        True( response.Action!.Succeeded, response.Action.Message ?? "copy failed" );
        True( store.StoreRevision > before, "explicit recopy advances the store revision" );
        Equal( second.Revision, store.ReadDocument( second.Id, second.Incarnation )!.Summary.Revision );
        Equal( Convert.ToBase64String( "second"u8 ), await Command( "paste text/plain" ) );
        Equal( "2", await Command( "cancelled" ) );
        Equal( 1, starts );

        async Task<string> Command( string command )
        {
            await server.StandardInput.WriteLineAsync( command );
            return (await server.StandardOutput.ReadLineAsync( token ))!;
        }
    }
}
