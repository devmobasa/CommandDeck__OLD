using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static ExtensionManifest PackageFixture( string version = "1.0.0" ) => new( )
    {
        ManifestVersion = 1,
        Id = "org.example.utilities",
        Version = version,
        Name = "Utilities",
        Description = "Local utilities",
        License = "MIT",
        Protocol = new( 1, 0 ),
        Execution = new( "restricted" ),
        Targets = new( )
        {
            ["linux-x64"] = new( "bin/linux-x64/utility", "native-aot", ManifestValidation.RuntimeProfile )
        },
        Commands = [new( ) { Id = "uuid", Title = "Create UUID", Keywords = ["identifier"] }]
    };

    private static void HandwrittenExtensionDefaultsSurviveDeserialization( )
    {
        var bytes = File.ReadAllBytes( Path.Combine( FindRepositoryRoot( ), "samples/extensions/UuidTools/commanddeck.extension.json" ) );
        var manifest = ManifestValidation.Parse( bytes );
        Equal( 0, manifest.Preferences.Length );
        Equal( "view", manifest.Commands[0].Mode );
        Equal( 0, manifest.Commands[0].Keywords.Length );
        Equal( 0, manifest.Commands[0].RequiredFeatures.Length );
        var missingMinor = Encoding.UTF8.GetString( bytes ).Replace( ", \"minimumMinor\": 0", "", StringComparison.Ordinal );
        True( missingMinor != Encoding.UTF8.GetString( bytes ) );
        Throws<JsonException>( ( ) => ManifestValidation.Parse( Encoding.UTF8.GetBytes( missingMinor ) ) );
        var explicitNull = Encoding.UTF8.GetString( bytes ).Insert( 1, "\"preferences\":null," );
        Throws<InvalidDataException>( ( ) => ManifestValidation.Parse( Encoding.UTF8.GetBytes( explicitNull ) ) );
        var list = JsonSerializer.Deserialize( "{\"kind\":\"list\",\"id\":\"list\",\"title\":\"Results\"}", ExtensionViewJsonContext.Default.ExtensionView )!;
        CommandDeck.Extensions.Views.ViewValidation.Validate( list );
        Equal( 0, (( CommandDeck.Extensions.Views.ListView ) list).Items.Length );
        var withItem = JsonSerializer.Deserialize( """{"kind":"list","id":"list","title":"Results","items":[{"id":"a","title":"A"}]}""", ExtensionViewJsonContext.Default.ExtensionView )!;
        CommandDeck.Extensions.Views.ViewValidation.Validate( withItem );
        Equal( 0, (( CommandDeck.Extensions.Views.ListView ) withItem).Items[0].Actions.Length );
        var nullActions = JsonSerializer.Deserialize( """{"kind":"list","id":"list","title":"Results","items":[{"id":"a","title":"A","actions":null}]}""", ExtensionViewJsonContext.Default.ExtensionView )!;
        Throws<InvalidDataException>( ( ) => CommandDeck.Extensions.Views.ViewValidation.Validate( nullActions ) );
        Throws<JsonException>( ( ) => JsonSerializer.Deserialize( """{"kind":"list","id":"list","title":"Results","items":[{"id":"a","title":"A","unexpected":true}]}""", ExtensionViewJsonContext.Default.ExtensionView ) );

        var form = JsonSerializer.Deserialize( "{\"kind\":\"form\",\"id\":\"form\",\"title\":\"Form\",\"fields\":[{\"id\":\"name\",\"label\":\"Name\",\"kind\":\"text\"}],\"submit\":{\"id\":\"submit\",\"kind\":\"callback\",\"title\":\"Save\"}}", ExtensionViewJsonContext.Default.ExtensionView )!;
        CommandDeck.Extensions.Views.ViewValidation.Validate( form );
        Equal( 8192, (( CommandDeck.Extensions.Views.FormView ) form).Fields[0].MaxLength );
    }

    private static string WritePackageFixture( string root, ExtensionManifest manifest, string? extraPath = null, int attributes = 0 )
    {
        var path = Path.Combine( root, Guid.NewGuid( ).ToString( "N" ) + ".cdext" );
        using var archive = ZipFile.Open( path, ZipArchiveMode.Create );
        using ( var output = archive.CreateEntry( "commanddeck.extension.json" ).Open( ) )
        {
            output.Write( JsonSerializer.SerializeToUtf8Bytes( manifest, ExtensionManifestJsonContext.Default.ExtensionManifest ) );
        }
        foreach ( var (rid, target) in manifest.Targets )
        {
            using var output = archive.CreateEntry( target.Entrypoint ).Open( );
            byte[] header = new byte[64];
            "\u007fELF"u8.CopyTo( header );
            header[4] = 2;
            header[5] = 1;
            header[6] = 1;
            header[16] = 3;
            header[18] = rid == "linux-x64" ? ( byte ) 62 : ( byte ) 183;
            output.Write( header );
        }
        if ( extraPath is not null )
        {
            var entry = archive.CreateEntry( extraPath );
            entry.ExternalAttributes = attributes;
            using var output = entry.Open( );
            output.Write( "unexpected"u8 );
        }
        return path;
    }

    private static async Task ExtensionPackagesRejectUnsafeArchivesAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), "commanddeck-package-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( root );
        try
        {
            var artifacts = Path.Combine( root, "artifacts" );
            foreach ( var (path, mode) in new[] { ("../escape", 0), ("/absolute", 0), ("bin/linux-x64/link", 0xA000 << 16), ("COMMANDDECK.EXTENSION.JSON", 0), ("bin", 0) } )
            {
                var archive = WritePackageFixture( root, PackageFixture( ), path, mode );
                await ThrowsAsync<InvalidDataException>( async ( ) => await ExtensionArchive.StageAsync( archive, artifacts, CancellationToken.None ) );
                Equal( 0, Directory.GetDirectories( artifacts ).Length );
            }
            var valid = WritePackageFixture( root, PackageFixture( ) );
            await ThrowsAsync<InvalidDataException>( async ( ) => await ExtensionArchive.StageAsync( valid, artifacts, CancellationToken.None, new string( '0', 64 ) ) );
            await using var staged = await ExtensionArchive.StageAsync( valid, artifacts, CancellationToken.None );
            Equal( "org.example.utilities", staged.Manifest.Id );
            True( File.Exists( Path.Combine( staged.StagingDirectory, "content/bin/linux-x64/utility" ) ) );
            var json = JsonSerializer.Serialize( PackageFixture( ), ExtensionManifestJsonContext.Default.ExtensionManifest );
            Throws<InvalidDataException>( ( ) => ManifestValidation.Parse( Encoding.UTF8.GetBytes( json.Insert( 1, "\"manifestVersion\":1," ) ) ) );
            Throws<InvalidDataException>( ( ) => ManifestValidation.Validate( PackageFixture( "1.0.0-01" ) ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ExtensionPackageLifecyclePreservesIdentityAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), "commanddeck-registry-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            using var registry = await ExtensionPackageRegistry.OpenAsync( catalog, Path.Combine( root, "artifacts" ), CancellationToken.None );
            catalog.Contributions = registry;
            var stale = await catalog.GetPreferencesAsync( CancellationToken.None );
            await using var staged = await ExtensionArchive.StageAsync( WritePackageFixture( root, PackageFixture( ) ), registry.ArtifactRoot, CancellationToken.None );
            var installed = await registry.InstallAsync( staged, "local", 0, true, [], false, CancellationToken.None );
            Equal( 0, registry.Snapshot.Commands.Count( command => command.Available ) );
            await registry.SetEnabledAsync( null, true, registry.Document.Revision, CancellationToken.None );
            SearchCoordinator search = new( [new ExtensionCommandProvider( registry, catalog )], new SearchOptions( ), catalog, catalog, contributions: registry );
            var result = (await search.SearchAsync( new SearchRequest { Query = "UUID", Limit = 10 }, CancellationToken.None )).Single( );
            Equal( ExtensionStableIds.CommandId( installed.Key, "uuid" ), result.Id );
            Equal( 1, result.Actions.Count );
            Equal( ActionKinds.ExtensionOpen, result.Actions[0].Kind );
            Equal( 0, result.Actions[0].Arguments.Count ); // Discovery carries no executable or effect payload.
            await catalog.SetAliasAsync( result, "uid", CancellationToken.None );
            Equal( result.Id, (await catalog.FindAliasAsync( "uid", CancellationToken.None ))!.Id );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await catalog.ReplacePreferencesAsync( stale, CancellationToken.None ) );
            var preferences = await catalog.GetPreferencesAsync( CancellationToken.None );
            SourcePreferencePatch patch = new( )
            {
                RegistryRevision = preferences.RegistryRevision,
                PreferenceRevision = preferences.PreferenceRevision,
                Changes = [new( ) { SourceId = result.SourceId, EnabledInSearch = false }]
            };
            await catalog.PatchSourcePreferencesAsync( patch, CancellationToken.None );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await catalog.ReplacePreferencesAsync( preferences, CancellationToken.None ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await catalog.PatchSourcePreferencesAsync( patch, CancellationToken.None ) );
            Equal( 0, (await search.SearchAsync( new SearchRequest { Query = "UUID" }, CancellationToken.None )).Count );
            await using var updated = await ExtensionArchive.StageAsync( WritePackageFixture( root, PackageFixture( "1.1.0" ) ), registry.ArtifactRoot, CancellationToken.None );
            await registry.InstallAsync( updated, "local", registry.Document.Revision, true, null, false, CancellationToken.None );
            await registry.RollbackAsync( installed.Key, installed.Digest, registry.Document.Revision, CancellationToken.None );
            Equal( "1.0.0", registry.Find( installed.Key ).Manifest.Version );
            await catalog.ReplaceAllAsync( AsyncEnumerable.Empty<IndexedItem>( ), null, CancellationToken.None );
            Equal( result.Id, (await catalog.FindAliasAsync( "uid", CancellationToken.None ))!.Id );
            var filter = registry.Snapshot.Filters.Single( filter => filter.SourceId == result.SourceId );
            await registry.SetEnabledAsync( installed.Key, false, registry.Document.Revision, CancellationToken.None );
            Throws<InvalidOperationException>( ( ) => QueryParser.Parse( filter.Token, registry: registry.Snapshot ) );
            True( await catalog.FindAliasAsync( "uid", CancellationToken.None ) is null );
            await registry.RemoveAsync( installed.Key, false, registry.Document.Revision, CancellationToken.None );
            await using var reinstalled = await ExtensionArchive.StageAsync( WritePackageFixture( root, PackageFixture( "1.2.0" ) ), registry.ArtifactRoot, CancellationToken.None );
            await registry.InstallAsync( reinstalled, "local", registry.Document.Revision, true, null, false, CancellationToken.None );
            using var reopened = await ExtensionPackageRegistry.OpenAsync( catalog, registry.ArtifactRoot, CancellationToken.None );
            catalog.Contributions = reopened;
            Equal( result.Id, reopened.Snapshot.Commands.Single( ).StableId );
            Equal( result.Id, (await catalog.FindAliasAsync( "uid", CancellationToken.None ))!.Id );
            True( !(await catalog.GetPreferencesAsync( CancellationToken.None )).Sources.Single( source => source.SourceId == result.SourceId ).EnabledInSearch );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ExtensionInventoryBoundsPreserveSourcesAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), "commanddeck-inventory-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            using var registry = await ExtensionPackageRegistry.OpenAsync( catalog, Path.Combine( root, "artifacts" ), CancellationToken.None );
            catalog.Contributions = registry;
            for ( var index = 0; index < ManifestValidation.MaximumPackages; index++ )
            {
                var manifest = PackageFixture( ) with
                {
                    Id = "org.example.utility-" + index
                };
                await using var staged = await ExtensionArchive.StageAsync( WritePackageFixture( root, manifest ), registry.ArtifactRoot, CancellationToken.None );
                await registry.InstallAsync( staged, "local", registry.Document.Revision, true, [], false, CancellationToken.None );
            }
            Equal( 128, registry.Document.Installations.Length );
            var preferences = await catalog.GetPreferencesAsync( CancellationToken.None );
            Equal( 128 + LauncherSourceRegistry.Configurable.Count, preferences.Sources.Count );
            ProtocolRequestValidator.Validate( new ProtocolRequest { RequestId = 1, Operation = ProtocolOperations.PreferencesUpdate, Preferences = preferences } );
            var source = preferences.Sources[^1].SourceId;
            await catalog.PatchSourcePreferencesAsync( new( )
            {
                RegistryRevision = preferences.RegistryRevision,
                PreferenceRevision = preferences.PreferenceRevision,
                Changes = [new( ) { SourceId = source, EnabledInSearch = false, Position = 1 }]
            }, CancellationToken.None );
            var patched = await catalog.GetPreferencesAsync( CancellationToken.None );
            Equal( source, patched.Sources[0].SourceId );
            True( !patched.Sources[0].EnabledInSearch );
            Equal( preferences.Sources.Count, patched.Sources.Count );
            await using var excess = await ExtensionArchive.StageAsync( WritePackageFixture( root, PackageFixture( ) ), registry.ArtifactRoot, CancellationToken.None );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await registry.InstallAsync( excess, "local", registry.Document.Revision, true, [], false, CancellationToken.None ) );
            Equal( 128L, registry.Document.Revision );
            Equal( 128, registry.Document.Installations.Length );
            Throws<InvalidOperationException>( ( ) => QueryParser.Parse( "ext0000000000: UUID", registry: registry.Snapshot ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ExtensionActivationFailurePreservesCurrentAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), "commanddeck-activation-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            using var registry = await ExtensionPackageRegistry.OpenAsync( catalog, Path.Combine( root, "artifacts" ), CancellationToken.None );
            catalog.Contributions = registry;
            await using var first = await ExtensionArchive.StageAsync( WritePackageFixture( root, PackageFixture( ) ), registry.ArtifactRoot, CancellationToken.None );
            var original = await registry.InstallAsync( first, "local", 0, true, [], false, CancellationToken.None );
            var updateArchive = WritePackageFixture( root, PackageFixture( "1.1.0" ) );
            List<string> revocationOrder = [];
            registry.RegisterRevocation( ExtensionRevocationStage.CommandSessions, ( _, _ ) =>
            {
                revocationOrder.Add( "commands" );
                throw new IOException( "Session shutdown failed after materialization." );
            } );
            registry.RegisterRevocation( ExtensionRevocationStage.Search, ( _, _ ) =>
            {
                revocationOrder.Add( "search" );
                return ValueTask.CompletedTask;
            } );
            await using ( var update = await ExtensionArchive.StageAsync( updateArchive, registry.ArtifactRoot, CancellationToken.None ) )
            {
                await ThrowsAsync<IOException>( async ( ) => await registry.InstallAsync( update, "local", 1, true, [], false, CancellationToken.None ) );
                Equal( "search,commands", string.Join( ',', revocationOrder ) );
                True( !Directory.Exists( Path.Combine( registry.ArtifactRoot, original.Key, "versions", update.Digest ) ) );
            }
            using var reopened = await ExtensionPackageRegistry.OpenAsync( catalog, registry.ArtifactRoot, CancellationToken.None );
            catalog.Contributions = reopened;
            Equal( 1L, reopened.Document.Revision );
            Equal( original.Digest, reopened.Find( original.Key ).Digest );
            True( Directory.Exists( reopened.ArtifactDirectory( original ) ) );
            await using var retry = await ExtensionArchive.StageAsync( updateArchive, reopened.ArtifactRoot, CancellationToken.None );
            await reopened.InstallAsync( retry, "local", 1, true, [], false, CancellationToken.None );
            Equal( 2L, reopened.Document.Revision );
            Equal( "1.1.0", reopened.Find( original.Key ).Manifest.Version );
            await using var differentBytes = await ExtensionArchive.StageAsync( WritePackageFixture( root, PackageFixture( "1.1.0" ), "extra.txt" ), reopened.ArtifactRoot, CancellationToken.None );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await reopened.InstallAsync( differentBytes, "local", 2, true, [], false, CancellationToken.None ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await reopened.InstallAsync( differentBytes, "another-origin", 2, true, [], false, CancellationToken.None ) );
            Equal( 2L, reopened.Document.Revision );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ExtensionArtifactCleanupPreservesRetainedVersionsAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), "commanddeck-artifact-cleanup-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            var artifacts = Path.Combine( root, "artifacts" );
            string? discardedDigest = null;
            string key;
            using ( var registry = await ExtensionPackageRegistry.OpenAsync( catalog, artifacts, CancellationToken.None ) )
            {
                catalog.Contributions = registry;
                for ( var index = 0; index < 9; index++ )
                {
                    await using var staged = await ExtensionArchive.StageAsync(
                        WritePackageFixture( root, PackageFixture( $"1.{index}.0" ) ),
                        artifacts,
                        CancellationToken.None );
                    if ( index == 0 )
                    {
                        discardedDigest = staged.Digest;
                    }

                    await registry.InstallAsync( staged, "local", registry.Document.Revision, true, [], false, CancellationToken.None );
                }

                var installation = registry.Find( "org.example.utilities" );
                key = installation.Key;
                Equal( 8, installation.Versions.Length );
                Equal( 8, Directory.GetDirectories( Path.Combine( artifacts, key, "versions" ) ).Length );
                True( !Directory.Exists( Path.Combine( artifacts, key, "versions", discardedDigest! ) ) );
                True( installation.Versions.All(
                    version => Directory.Exists( Path.Combine( artifacts, key, "versions", version.Digest ) ) ) );

                var rollback = installation.Versions[^1];
                await registry.RollbackAsync( key, rollback.Digest, registry.Document.Revision, CancellationToken.None );
                Equal( rollback.Digest, registry.Find( key ).Digest );
            }

            var orphanDigest = new string( 'a', 64 );
            var orphanVersion = Path.Combine( artifacts, key, "versions", orphanDigest );
            Directory.CreateDirectory( orphanVersion );
            var orphanPackage = Path.Combine( artifacts, new string( 'b', 64 ) );
            Directory.CreateDirectory( orphanPackage );
            var abandonedStaging = Path.Combine( artifacts, ".staging-" + new string( 'c', 32 ) );
            Directory.CreateDirectory( abandonedStaging );

            using var reopened = await ExtensionPackageRegistry.OpenAsync( catalog, artifacts, CancellationToken.None );

            catalog.Contributions = reopened;
            True( !Directory.Exists( orphanVersion ) );
            True( !Directory.Exists( orphanPackage ) );
            True( !Directory.Exists( abandonedStaging ) );
            True( reopened.Find( key ).Versions.All(
                version => Directory.Exists( Path.Combine( artifacts, key, "versions", version.Digest ) ) ) );

            var versions = Path.Combine( artifacts, key, "versions" );
            var retainedVersions = Path.Combine( artifacts, key, "retained-versions" );
            var externalVersions = Path.Combine( root, "external-versions" );
            var externalDigest = Path.Combine( externalVersions, new string( 'd', 64 ) );
            var externalSentinel = Path.Combine( externalDigest, "sentinel" );
            Directory.CreateDirectory( externalDigest );
            await File.WriteAllTextAsync( externalSentinel, "outside the artifact root" );
            Directory.Move( versions, retainedVersions );
            Directory.CreateSymbolicLink( versions, externalVersions );
            try
            {
                using var linkSafe = await ExtensionPackageRegistry.OpenAsync( catalog, artifacts, CancellationToken.None );
                catalog.Contributions = linkSafe;
                True( File.Exists( externalSentinel ) );
            }
            finally
            {
                Directory.Delete( versions );
                Directory.Move( retainedVersions, versions );
            }

            var externalRoot = Path.Combine( root, "external-root" );
            var externalRootDigest = Path.Combine( externalRoot, new string( 'e', 64 ) );
            var externalRootSentinel = Path.Combine( externalRootDigest, "sentinel" );
            var linkedArtifactRoot = Path.Combine( root, "linked-artifacts" );
            Directory.CreateDirectory( externalRootDigest );
            await File.WriteAllTextAsync( externalRootSentinel, "outside the artifact root" );
            Directory.CreateSymbolicLink( linkedArtifactRoot, externalRoot );

            using var rootLinkSafe = await ExtensionPackageRegistry.OpenAsync( catalog, linkedArtifactRoot, CancellationToken.None );

            catalog.Contributions = rootLinkSafe;
            True( File.Exists( externalRootSentinel ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
