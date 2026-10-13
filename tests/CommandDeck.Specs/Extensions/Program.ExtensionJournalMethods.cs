using System.Security.Cryptography;
using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionJournalBoundsAndLegacyAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var directory = Path.Combine( root, "journal" );
            Directory.CreateDirectory( directory );
            var legacy = Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( "A\0service\0legacy-key" ) ) );
            await File.WriteAllTextAsync( Path.Combine( directory, legacy ), "attempted" );
            using ( var journal = new FileRemoteEffectJournal( directory, 2, 4 ) )
            {
                Equal( JournalAdmission.AlreadyAttempted, await journal.TryRecordAttemptAsync( "A", "service", "legacy-key", CancellationToken.None ) );
                Equal( JournalAdmission.Recorded, await journal.TryRecordAttemptAsync( "A", "service", "one", CancellationToken.None ) );
                Equal( JournalAdmission.Recorded, await journal.TryRecordAttemptAsync( "A", "service", "two", CancellationToken.None ) );
                Equal( JournalAdmission.PackageFull, await journal.TryRecordAttemptAsync( "A", "service", "three", CancellationToken.None ) );
                Equal( JournalAdmission.Recorded, await journal.TryRecordAttemptAsync( "B", "service", "one", CancellationToken.None ) );
                Equal( JournalAdmission.JournalFull, await journal.TryRecordAttemptAsync( "B", "service", "two", CancellationToken.None ) );
                Equal( JournalAdmission.AlreadyAttempted, await journal.TryRecordAttemptAsync( "A", "service", "one", CancellationToken.None ) );
                Equal( 2, await journal.CountAsync( "A", CancellationToken.None ) );
                Equal( 1, await journal.CountAsync( "B", CancellationToken.None ) );
                await journal.ClearAsync( "A", false, CancellationToken.None );
                Equal( 0, await journal.CountAsync( "A", CancellationToken.None ) );
                Equal( 1, await journal.CountAsync( "B", CancellationToken.None ) );
                True( File.Exists( Path.Combine( directory, legacy ) ) );
            }
            using var reopened = new FileRemoteEffectJournal( directory, 2, 4 );
            Equal( 1, await reopened.CountAsync( "B", CancellationToken.None ) );
            using CancellationTokenSource canceled = new( );
            canceled.Cancel( );
            await ThrowsAsync<OperationCanceledException>( async ( ) => await reopened.CountAsync( "B", canceled.Token ) );
            Equal( JournalAdmission.AlreadyAttempted, await reopened.TryRecordAttemptAsync( "A", "service", "legacy-key", CancellationToken.None ) );
            await reopened.ClearAsync( "A", true, CancellationToken.None );
            Equal( 1, await reopened.CountAsync( "B", CancellationToken.None ) );
            Equal( JournalAdmission.Recorded, await reopened.TryRecordAttemptAsync( "A", "service", "legacy-key", CancellationToken.None ) );
            await ThrowsAsync<ArgumentException>( async ( ) => await reopened.TryRecordAttemptAsync( "../escape", "service", "key", CancellationToken.None ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task ExtensionManagementResetAndEraseAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var config = await WriteManagedSpecConfigAsync( root, new AiOptions { Enabled = false } );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None );
            async Task<ExtensionInventory> Manage( ExtensionManagementRequest request ) => await runtime.ManageExtensionsAsync( request, null, 0, 32, CancellationToken.None );
            var archive = WritePackageFixture( root, PackageFixture( ) );
            var inspection = await Manage( new( ) { Action = "inspect", ArchivePath = archive } );
            var installed = await Manage( new( ) { Action = "install", ArchivePath = archive, Digest = inspection.ArchiveDigest, Confirmed = true } );
            var key = installed.Packages.Single( ).Key;
            var journal = runtime.ExtensionConnections.Journal;
            Equal( JournalAdmission.Recorded, await journal.TryRecordAttemptAsync( key, "service", "disabled-request", CancellationToken.None ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Manage( new( ) { Action = "effects-reset", Key = key, ExpectedRevision = installed.Revision } ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Manage( new( ) { Action = "effects-reset", Key = key, ExpectedRevision = installed.Revision - 1, Confirmed = true } ) );
            Equal( 1, await journal.CountAsync( key, CancellationToken.None ) );
            var disabledReset = await Manage( new( ) { Action = "effects-reset", Key = key, ExpectedRevision = installed.Revision, Confirmed = true } );
            Equal( 0, disabledReset.EffectAttempts );
            True( !disabledReset.Packages.Single( ).Enabled );
            var enabled = await Manage( new( ) { Action = "enable", Key = key, ExpectedRevision = installed.Revision } );
            enabled = await Manage( new( ) { Action = "enable", ExpectedRevision = enabled.Revision } );
            Equal( JournalAdmission.Recorded, await journal.TryRecordAttemptAsync( key, "service", "request-one", CancellationToken.None ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Manage( new( ) { Action = "effects-reset", Key = key, ExpectedRevision = enabled.Revision } ) );
            Equal( 1, await journal.CountAsync( key, CancellationToken.None ) );
            var reset = await Manage( new( ) { Action = "effects-reset", Key = key, ExpectedRevision = enabled.Revision, Confirmed = true } );
            Equal( 0, reset.EffectAttempts );
            Equal( JournalAdmission.Recorded, await journal.TryRecordAttemptAsync( key, "service", "request-one", CancellationToken.None ) );
            var removed = await Manage( new( ) { Action = "remove", Key = key, ExpectedRevision = reset.Revision, Confirmed = true } );
            Equal( 1, removed.EffectAttempts );
            var erased = await Manage( new( ) { Action = "remove", Key = key, ExpectedRevision = removed.Revision, EraseSettings = true, Confirmed = true } );
            Equal( 0, erased.EffectAttempts );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }
}
