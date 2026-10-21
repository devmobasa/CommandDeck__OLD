using System.Buffers.Binary;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardRichAdmissionPublishesAtomicSelectionsAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-rich-admission-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            ClipboardEntryIdentity identity;
            long revision;
            using ( ClipboardHistoryStore store = new( path, new( ) ) )
            {
                EnableRichFixture( store );
                await FeedRichAdmissionAsync( store, ["text/plain", "text/html"], ["caption"u8.ToArray( ), "<b>caption</b>"u8.ToArray( )] );
                identity = store.SnapshotEntries( new( ), false ).Single( );
                Equal( 32, identity.Id.Length );
                True( store.Pin( identity.Id, true ) );
                revision = store.ReadDocument( identity.Id, identity.Incarnation )!.Summary.Revision;
                store.MutateEntry( identity.Id, identity.Incarnation, revision, "keywords", ["retained"] );
                revision++;
                await FeedRichAdmissionAsync( store, ["text/html", "text/plain"], ["<b>caption</b>"u8.ToArray( ), "caption"u8.ToArray( )] );
                var recopy = store.ReadDocument( identity.Id, identity.Incarnation )!;
                True( recopy.Summary.Pinned );
                Equal( revision + 1, recopy.Summary.Revision );
                Equal( "retained", recopy.Summary.Keywords.Single( ) );
                Equal( 1L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM entries" ) );
                Equal( 2L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_offers" ) );
                Equal( 2, Directory.GetFiles( path + ".payloads" ).Length );
                revision = recopy.Summary.Revision;
                Throws<InvalidOperationException>( ( ) => store.ReadCopyText( identity.Id, identity.Incarnation, revision ) );
                True( store.ReadDocument( identity.Id, identity.Incarnation ) is not null,
                    "unsupported legacy Copy cannot revoke a valid rich entry" );
            }

            using ClipboardHistoryStore reopened = new( path, new( ) );
            Equal( "caption", reopened.ReadDocument( identity.Id, identity.Incarnation, revision )!.Text );
            True( !reopened.ReadProjectionMetadata( identity.Id, identity.Incarnation, revision )!.ExtractedText );
            await FeedRichAdmissionAsync( reopened, ["text/plain"], ["caption"u8.ToArray( )] );
            Equal( 2, reopened.Search( "", 10 ).Count );
            True( reopened.Search( "", 10 ).Any( static entry => entry.Id.Length == 64 ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardRichAdmissionPersistsInertMetadataAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-rich-metadata-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            using ( ClipboardHistoryStore store = new( path, new( ) ) )
            {
                EnableRichFixture( store );
                ClipboardHtmlProjectionService html = new( Environment.ProcessPath!, ( _, _, _, _, _ ) => ValueTask.FromResult(
                    new ProcessResult { ExitCode = 0, StandardOutput = "{\"available\":true,\"text\":\"extracted fixture\",\"truncated\":false}", StandardError = "" } ) );
                await FeedRichAdmissionAsync( store, ["text/html"], ["<p>raw fixture</p>"u8.ToArray( )], new( html ) );
                var htmlEntry = store.Search( "extracted fixture", 10 ).Single( );
                Equal( 0, store.Search( "raw fixture", 10 ).Count );
                var identity = store.SnapshotEntries( QueryParser.Parse( "extracted" ).Expression, false ).Single( );
                var revision = store.ReadDocument( identity.Id, identity.Incarnation )!.Summary.Revision;
                True( store.ReadProjectionMetadata( identity.Id, identity.Incarnation, revision )!.ExtractedText );
                True( htmlEntry.Id.Length == 32 );

                await FeedRichAdmissionAsync( store, ["x-special/gnome-copied-files", "text/uri-list", "text/plain"],
                    ["copy\nfile:///fixture/never-opened.txt"u8.ToArray( ), "file:///fixture/never-opened.txt\n"u8.ToArray( ), "file display"u8.ToArray( )] );
                var file = store.SnapshotEntries( QueryParser.Parse( "never-opened" ).Expression, false ).Single( );
                var fileDocument = store.ReadDocument( file.Id, file.Incarnation )!;
                Equal( "/fixture/never-opened.txt", fileDocument.Text );
                Equal( "file:///fixture/never-opened.txt", store.ReadProjectionMetadata( file.Id, file.Incarnation, fileDocument.Summary.Revision )!.Destinations.Single( ) );
                Equal( "file:///fixture/never-opened.txt", store.ReadProjectionMetadata( file.Id, file.Incarnation, fileDocument.Summary.Revision )!.OpenDestination );

                await FeedRichAdmissionAsync( store, ["text/plain", "text/uri-list"],
                    ["independent fallback"u8.ToArray( ), "file:///a\nhttps://mixed.invalid/"u8.ToArray( )] );
                var fallback = store.SnapshotEntries( QueryParser.Parse( "independent" ).Expression, false ).Single( );
                var fallbackRevision = store.ReadDocument( fallback.Id, fallback.Incarnation )!.Summary.Revision;
                True( store.ReadProjectionMetadata( fallback.Id, fallback.Incarnation, fallbackRevision )!.ReducedFidelity );
                Equal( "independent fallback", store.ReadCopyText( fallback.Id, fallback.Incarnation, fallbackRevision ) );
                Equal( 5L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_offers" ) );
            }

            using ClipboardHistoryStore reopened = new( path, new( ) );
            Equal( 3, reopened.Search( "", 10 ).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardRichAdmissionRejectsRevocationAndAliasConflictAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-rich-rejection-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            using ClipboardHistoryStore store = new( path, new( ) );
            EnableRichFixture( store );
            await ThrowsAsync<InvalidDataException>( ( ) => FeedRichAdmissionAsync( store, ["text/html", "TEXT/HTML", "text/plain"],
                ["one"u8.ToArray( ), "two"u8.ToArray( ), "fallback"u8.ToArray( )] ) );
            await ThrowsAsync<InvalidDataException>( ( ) => FeedRichAdmissionAsync( store, ["x-special/gnome-copied-files", "text/plain"],
                ["cut\nfile:///a"u8.ToArray( ), "fallback"u8.ToArray( )] ) );
            await ThrowsAsync<InvalidOperationException>( ( ) => FeedRichAdmissionAsync( store, ["text/html"],
                ["<b>revoked</b>"u8.ToArray( )], beforePublish: ( ) => store.Clear( ) ) );
            Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM entries" ) );
            Equal( 0L, ClipboardDatabaseScalar( path, "SELECT count(*) FROM clipboard_payloads" ) );
            Equal( 0, Directory.GetFiles( path + ".payloads" ).Length );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void EnableRichFixture( ClipboardHistoryStore store, int quotaMiB = 256 )
    {
        var preview = store.PreparePolicyChange( store.Policy.Revision, new( )
        {
            Consent = ClipboardConsent.Rich,
            QuotaMiB = quotaMiB
        } );
        store.ApplyPolicyChange( preview, confirmed: true );
    }

    private static async Task FeedRichAdmissionAsync( ClipboardHistoryStore store, string[] names, byte[][] contents,
        ClipboardPayloadProjection? projections = null, Action? beforePublish = null )
    {
        var selection = Guid.NewGuid( );
        var plan = ClipboardOfferPlan.Create( names, ClipboardConsent.Rich );
        using var admission = store.AdmitRichCapture( names, store.CaptureEpoch, 71, selection, 3,
            projections ?? new( new( "/missing-clipboard-projection-fixture" ) ) );
        uint sequence = 3;
        foreach ( var offer in plan.Offers )
        {
            admission.BeginOffer( offer.InventoryIndex );
            var bytes = contents[offer.InventoryIndex];
            for ( var offset = 0; offset < bytes.Length; )
            {
                var length = Math.Min( ClipboardHelperHeader.MaximumPayload, bytes.Length - offset );
                await admission.AcceptAsync( new( new( ClipboardHelperFrameType.OfferChunk, 71, selection,
                    offer.InventoryIndex, sequence++, length ), bytes.AsMemory( offset, length ) ), default );
                offset += length;
            }

            var size = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian( size, ( ulong ) bytes.Length );
            await admission.AcceptAsync( new( new( ClipboardHelperFrameType.OfferEnd, 71, selection, offer.InventoryIndex, sequence++, 8 ), size ), default );
        }

        beforePublish?.Invoke( );
        var count = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian( count, ( uint ) plan.Offers.Count );
        await admission.AcceptAsync( new( new( ClipboardHelperFrameType.SelectionEnd, 71, selection, -1, sequence, 4 ), count ), default );
        True( admission.Complete );
    }
}
