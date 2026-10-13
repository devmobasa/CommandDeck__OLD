namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionDebugLeasesAreExplicitBoundedAndRevokedAsync( )
    {
        var manifest = PackageFixture( );
        ExtensionInstallation installed = new( )
        {
            Key = "one",
            Origin = "development",
            Digest = new string( 'a', 64 ),
            Manifest = manifest,
            Enabled = true
        };
        using var development = new ExtensionDevelopment( );
        var command = manifest.Commands[0].Id;
        foreach ( var invalid in new[] { installed with { Origin = "local" }, installed with { Enabled = false }, installed with { Removed = true } } )
        {
            Throws<InvalidOperationException>( ( ) => development.Arm( invalid, command, installed.Digest, 30 ) );
        }
        Throws<InvalidOperationException>( ( ) => development.Arm( installed, "foreign", installed.Digest, 30 ) );
        Throws<InvalidOperationException>( ( ) => development.Arm( installed, command, new string( 'b', 64 ), 30 ) );
        Throws<InvalidOperationException>( ( ) => development.Arm( installed, command, installed.Digest, 301 ) );
        var armed = development.Arm( installed, command, installed.Digest, 30 );
        True( development.Take( installed with { Key = "other" }, command, true ) is null );
        True( development.Take( installed, "other", true ) is null );
        Throws<InvalidOperationException>( ( ) => development.Take( installed, command, false ) );
        var lease = development.Take( installed, command, true )!;
        True( lease.State.Consumed );
        True( development.Take( installed, command, true ) is null );
        development.Revoke( installed.Key );
        True( lease.Token.IsCancellationRequested );
        True( !Directory.Exists( Path.GetDirectoryName( armed.SocketPath ) ) );
        var expiring = development.Arm( installed, command, installed.Digest, 1 );
        await Task.Delay( 1300 );
        True( development.State( installed.Key ) is null );
        True( !Directory.Exists( Path.GetDirectoryName( expiring.SocketPath ) ) );
        for ( var index = 0; index < 12; index++ )
        {
            development.Record( installed.Key, new( DateTimeOffset.UtcNow, "s" + index, command, installed.Digest, "failed", new string( 'x', 5000 ) ) );
        }
        var safe = development.Read( installed.Key, false );
        Equal( 8, safe.Length );
        True( safe.All( value => value.Stderr is null ) );
        Equal( "s4", safe[0].SessionId );
        Equal( 4096, development.Read( installed.Key, true )[0].Stderr!.Length );
        True( development.Read( "other", true ).Length == 0 );

        using var bounded = new ExtensionDevelopment( );
        var diagnostic = new ExtensionDiagnostic( DateTimeOffset.UtcNow, "session", command, installed.Digest, "failed", null );
        for ( var index = 0; index < 128; index++ )
        {
            bounded.Record( $"k{index:D3}", diagnostic );
        }
        bounded.Record( "k000", diagnostic );
        bounded.Record( "k128", diagnostic );

        Equal( 2, bounded.Read( "k000", false ).Length );
        Equal( 0, bounded.Read( "k001", false ).Length );
        Equal( 1, bounded.Read( "k128", false ).Length );
    }

    private static async Task ExtensionDevelopmentAttachmentCannotReplaceProductionAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var config = await WriteManagedSpecConfigAsync( root, new AiOptions { Enabled = false } );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None );
            async Task<ExtensionInventory> Change( ExtensionManagementRequest request ) => await runtime.ManageExtensionsAsync( request, null, 0, 32, CancellationToken.None );
            var original = WritePackageFixture( root, PackageFixture( ) );
            var inspected = await Change( new( )
            {
                Action = "inspect",
                ArchivePath = original
            } );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Change( new( ) { Action = "attach", ArchivePath = original, Digest = inspected.ArchiveDigest, Confirmed = true } ) );
            var installed = await Change( new( )
            {
                Action = "install",
                ArchivePath = original,
                Digest = inspected.ArchiveDigest,
                Confirmed = true
            } );
            var key = installed.Packages[0].Key;
            await Change( new( )
            {
                Action = "enable",
                Key = key,
                ExpectedRevision = installed.Revision
            } );
            var enabled = await Change( new( )
            {
                Action = "enable",
                ExpectedRevision = installed.Revision + 1
            } );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await Change( new( ) { Action = "debug", Key = key, CommandId = "uuid", Digest = inspected.ArchiveDigest, DebugSeconds = 30, Confirmed = true, ExpectedRevision = enabled.Revision } ) );
            var devManifest = PackageFixture( "1.1.0" ) with
            {
                Id = "org.example.utilities.development"
            };
            var archive = WritePackageFixture( root, devManifest );
            var review = await Change( new( )
            {
                Action = "inspect",
                ArchivePath = archive
            } );
            var attached = await Change( new( )
            {
                Action = "attach",
                ArchivePath = archive,
                Digest = review.ArchiveDigest,
                ExpectedRevision = enabled.Revision,
                Confirmed = true
            } );
            Equal( 2, attached.Total );
            Equal( inspected.ArchiveDigest, attached.Packages.Single( value => value.Key == key ).Digest );
            True( !attached.Packages.Single( value => value.Origin == "development" ).Enabled );
            var developmentKey = attached.Packages.Single( value => value.Origin == "development" ).Key;
            await Change( new( )
            {
                Action = "enable",
                Key = developmentKey,
                ExpectedRevision = attached.Revision
            } );
            var rootResults = await runtime.SearchAsync( new SearchRequest { Query = "UUID", Limit = 10 }, CancellationToken.None );
            True( rootResults.All( value => value.SourceId != ExtensionStableIds.SourceId( developmentKey ) ) );
            var filter = runtime.Contributions.Filters.Single( value => value.SourceId == ExtensionStableIds.SourceId( developmentKey ) );
            var scopedResults = await runtime.SearchAsync( new SearchRequest { Query = filter.Token + " UUID", Limit = 10 }, CancellationToken.None );
            True( scopedResults.Any( value => value.SourceId == ExtensionStableIds.SourceId( developmentKey ) ) );
            var logs = await Change( new( )
            {
                Action = "logs",
                Key = key
            } );
            Equal( 0, logs.Diagnostics.Length );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }
}
