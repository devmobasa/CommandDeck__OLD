using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionInstallRejectsUnrunnableTargetsAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            using var registry = await ExtensionPackageRegistry.OpenAsync( catalog, Path.Combine( root, "artifacts" ), CancellationToken.None );
            catalog.Contributions = registry;
            var manifest = PackageFixture( ) with
            {
                Targets = new( ) { ["linux-arm64"] = new( "bin/linux-arm64/utility", "native-aot", ManifestValidation.RuntimeProfile ) }
            };
            await using var staged = await ExtensionArchive.StageAsync( WritePackageFixture( root, manifest ), registry.ArtifactRoot, CancellationToken.None );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await registry.InstallAsync( staged, "local", 0, true, [], true, CancellationToken.None ) );
            Equal( 0L, registry.Document.Revision );
            Equal( 0, registry.Document.Installations.Length );
            True( !Directory.Exists( Path.Combine( registry.ArtifactRoot, ExtensionPackageRegistry.InstallationKey( "local", manifest.Id ) ) ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task ExtensionRollbackRejectsUnrunnableRetainedTargetsAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            var artifactRoot = Path.Combine( root, "artifacts" );
            string key, currentDigest, oldDigest;
            using ( var registry = await ExtensionPackageRegistry.OpenAsync( catalog, artifactRoot, CancellationToken.None ) )
            {
                catalog.Contributions = registry;
                await using var current = await ExtensionArchive.StageAsync( WritePackageFixture( root, PackageFixture( ) ), artifactRoot, CancellationToken.None );
                var installation = await registry.InstallAsync( current, "local", 0, true, [], true, CancellationToken.None );
                key = installation.Key;
                currentDigest = installation.Digest;
                var arm = PackageFixture( ) with { Version = "0.9.0", Targets = new( ) { ["linux-arm64"] = new( "bin/linux-arm64/utility", "native-aot", ManifestValidation.RuntimeProfile ) } };
                await using var previous = await ExtensionArchive.StageAsync( WritePackageFixture( root, arm ), artifactRoot, CancellationToken.None );
                new ExtensionArtifactStore( artifactRoot ).Materialize( previous, key );
                oldDigest = previous.Digest;
                // Seed a retained version from an older host; current hosts reject it at install time.
                var document = registry.Document with
                {
                    Revision = registry.Document.Revision + 1,
                    Installations = [installation with { Versions = [.. installation.Versions, new( arm.Version, oldDigest )] }]
                };
                await catalog.CommitExtensionRegistryAsync( document, registry.Snapshot, registry.Document.Revision, CancellationToken.None );
            }
            using var reopened = await ExtensionPackageRegistry.OpenAsync( catalog, artifactRoot, CancellationToken.None );
            catalog.Contributions = reopened;
            var revision = reopened.Document.Revision;
            await ThrowsAsync<InvalidOperationException>( async ( ) => await reopened.RollbackAsync( key, oldDigest, revision, CancellationToken.None ) );
            Equal( revision, reopened.Document.Revision );
            Equal( currentDigest, reopened.Find( key ).Digest );
        }
        finally { Directory.Delete( root, true ); }
    }

    private static async Task ExtensionMultiTargetOpensX64Async( )
    {
        SessionViewCommand command = new( );
        var manifest = PackageFixture( ) with
        {
            Commands = [new( ) { Id = command.Id, Title = "Session command" }],
            Targets = new( )
            {
                ["linux-arm64"] = new( "bin/linux-arm64/utility", "native-aot", ManifestValidation.RuntimeProfile ),
                ["linux-x64"] = new( "bin/linux-x64/utility", "native-aot", ManifestValidation.RuntimeProfile )
            }
        };
        await using var harness = await ExtensionSessionHarness.CreateAsync( command, manifest: manifest );
        await harness.OpenAsync( );
        Equal( "Initial", harness.LatestView.Content!.Value.GetProperty( "title" ).GetString( ) );
    }
}
