namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardSettingsPreviewsAreAtomicAndRevisionBound( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-settings-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new( ) );
            store.Enabled = true;
            for ( var index = 0; index < 20; index++ )
            {
                store.Capture( "settings entry " + index );
            }

            var first = store.Search( "", 100 ).First( );
            store.Pin( first.Id, true );
            var before = store.Policy;
            var change = store.PreparePolicyChange( before.Revision, new( )
            {
                MaximumEntries = 10,
                RetentionHours = 24,
                QuotaMiB = 64
            } );
            Equal( 10, change.DeleteIds.Count );
            Equal( 1, change.PinnedCount );
            Equal( 20, store.Search( "", 100 ).Count );
            Throws<InvalidOperationException>( ( ) => store.ApplyPolicyChange( change, false ) );
            Equal( before, store.Policy );
            store.Capture( "new capture invalidates preview" );
            Throws<InvalidOperationException>( ( ) => store.ApplyPolicyChange( change, true ) );
            Equal( before, store.Policy );
            change = store.PreparePolicyChange( before.Revision, new( )
            {
                MaximumEntries = 10,
                RetentionHours = 24,
                QuotaMiB = 64
            } );
            store.ApplyPolicyChange( change, true );
            Equal( 10, store.Search( "", 100 ).Count );
            True( store.Read( first.Id ) is not null, "pins survive limit changes" );
            Equal( 64, store.Policy.QuotaMiB );
            Equal( 24, store.Policy.RetentionHours );
            Throws<InvalidOperationException>( ( ) => store.ApplyPolicyChange( change, true ) );
            Throws<InvalidOperationException>( ( ) => store.PreparePolicyChange( store.Policy.Revision, new( ) { MaximumEntries = 9 } ) );
            store.ApplyPolicyChange( store.PreparePolicyChange( store.Policy.Revision, new( )
            {
                MaximumEntries = 20
            } ), false );
            foreach ( var entry in store.Search( "", 100 ).Take( 6 ) )
            {
                True( store.Pin( entry.Id, true ), "fixture pins fit the current allocation" );
            }

            Throws<InvalidOperationException>( ( ) => store.PreparePolicyChange( store.Policy.Revision, new( ) { MaximumEntries = 10 } ) );
            Equal( 20, store.Policy.MaximumEntries );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void ClipboardSettingsPreviewTokensAreOwnerBoundAndOneUse( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-settings-token-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new( ) );
            ClipboardBrowserClock clock = new( );
            ClipboardHistoryService service = new( store, clock );
            service.RegisterOwner( "one" );
            service.RegisterOwner( "two" );
            ProtocolRequest prepare = new( )
            {
                RequestId = 1,
                Operation = ProtocolOperations.ClipboardSettingsSet,
                Clipboard = new( )
                {
                    PolicyRevision = store.Policy.Revision,
                    Patch = new( )
                    {
                        MaximumEntries = 10
                    }
                }
            };
            var review = service.Handle( "one", prepare ).PolicyReview!;
            Equal( 200, store.Policy.MaximumEntries );
            ProtocolRequest confirm = ConfirmPolicy( review );
            Throws<InvalidOperationException>( ( ) => service.Handle( "two", confirm ) );
            clock.Timestamp = 120;
            Throws<InvalidOperationException>( ( ) => service.Handle( "one", confirm ) );
            review = service.Handle( "one", prepare ).PolicyReview!;
            confirm = ConfirmPolicy( review );
            var applied = service.Handle( "one", confirm );
            Equal( 10, applied.Policy!.MaximumEntries );
            Throws<InvalidOperationException>( ( ) => service.Handle( "one", confirm ) );
            Equal( 10, store.Policy.MaximumEntries );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static ProtocolRequest ConfirmPolicy( ClipboardPolicyReview review ) => new( )
    {
        RequestId = 2,
        Operation = ProtocolOperations.ClipboardSettingsSet,
        Confirmed = true,
        ConfirmationToken = review.Token,
        Clipboard = new( )
        {
            PolicyRevision = review.PolicyRevision,
            StoreRevision = review.StoreRevision
        }
    };

    private static void ClipboardStartupClearWaitsForMigrationReviewAndRunsOnce( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-clipboard-startup-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        ClipboardTestClock clock = new( );
        try
        {
            WriteLegacyClipboard( path, 30, 20, clock.Now.AddDays( -10 ).ToUnixTimeSeconds( ), true );
            using ( ClipboardHistoryStore store = new( path, new( ), clock ) )
            {
                var change = store.PreparePolicyChange( store.Policy.Revision, new( )
                {
                    ClearOnStartup = true
                } );
                store.ApplyPolicyChange( change, true );
                True( store.MigrationReview.Required, "changing settings does not acknowledge migration" );
                Equal( 20, store.MigrationReview.EntryCount );
                True( store.MigrationReview.PendingStartupClear, "startup remains incomplete while migration review is pending" );
            }

            using ( ClipboardHistoryStore restarted = new( path, new( ), clock ) )
            {
                var review = restarted.MigrationReview;
                True( review.Required && review.PendingStartupClear, "pending startup clear remains behind migration review" );
                Equal( 20, review.EntryCount );
                Throws<InvalidOperationException>( ( ) => restarted.AcknowledgeMigration( restarted.Policy.Revision, review.StoreRevision, false ) );
                Equal( 20, restarted.MigrationReview.EntryCount );
                var epoch = restarted.ClearEpoch;
                restarted.AcknowledgeMigration( restarted.Policy.Revision, review.StoreRevision, true );
                Equal( 0, restarted.MigrationReview.EntryCount );
                True( restarted.ClearEpoch > epoch, "confirmed startup clear revokes existing browser identities" );
                restarted.Capture( "after startup" );
                restarted.Prune( );
                restarted.AcknowledgeMigration( restarted.Policy.Revision, restarted.StoreRevision, true );
                Equal( 1, restarted.Search( "", 10 ).Count );
            }

            using ClipboardHistoryStore nextStartup = new( path, new( ), clock );
            Equal( 0, nextStartup.Search( "", 10 ).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
