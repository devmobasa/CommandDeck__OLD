using System.Security.Cryptography;
using System.Text;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

public sealed partial class ExtensionPackageRegistry : IContributionRegistry, IDisposable
{
    private readonly SqliteCatalog _catalog;
    private readonly ExtensionArtifactStore _artifacts;
    private readonly SemaphoreSlim _gate = new( 1, 1 );
    private ExtensionRegistryDocument _document = new( );
    private ContributionRegistrySnapshot _snapshot = ContributionRegistrySnapshot.BuiltIns;

    private ExtensionPackageRegistry( SqliteCatalog catalog, ExtensionArtifactStore artifacts )
    {
        _catalog = catalog;
        _artifacts = artifacts;
    }

    public string ArtifactRoot => _artifacts.ArtifactRoot;
    public ExtensionDataStore DataStore { get; private set; } = null!;
    public ContributionRegistrySnapshot Snapshot => Volatile.Read( ref _snapshot );
    public ExtensionRegistryDocument Document => Volatile.Read( ref _document );
    public string? Error { get; private set; }
    private readonly SortedDictionary<ExtensionRevocationStage, Func<string?, CancellationToken, ValueTask>> _revocations = [];

    public void RegisterRevocation( ExtensionRevocationStage stage, Func<string?, CancellationToken, ValueTask> revoke )
    {
        ArgumentNullException.ThrowIfNull( revoke );
        lock ( _revocations )
        {
            _revocations.Add( stage, revoke );
        }
    }

    public static ValueTask<ExtensionPackageRegistry> OpenAsync( SqliteCatalog catalog, string artifactRoot, CancellationToken cancellationToken ) =>
        OpenAsync( catalog, new ExtensionArtifactStore( artifactRoot ), cancellationToken );

    public static async ValueTask<ExtensionPackageRegistry> OpenAsync( SqliteCatalog catalog, ExtensionArtifactStore artifacts, CancellationToken cancellationToken )
    {
        ExtensionPackageRegistry registry = new( catalog, artifacts );
        try
        {
            registry._document = await catalog.ReadExtensionRegistryAsync( cancellationToken ).ConfigureAwait( false );
            ValidateDocument( registry._document );
            await registry.MigrateStorageAsync( cancellationToken ).ConfigureAwait( false );
            registry.DataStore = await ExtensionDataStore.OpenAsync( catalog, cancellationToken ).ConfigureAwait( false );
            registry._snapshot = ContributionSnapshotBuilder.Build( registry._document );
            registry._artifacts.Recover( registry.Document );
        }
        catch ( Exception exception ) when ( exception is InvalidDataException or System.Text.Json.JsonException or IOException )
        {
            registry.Error = "The extension registry is invalid (" + exception.Message +
                             "). Extensions are unavailable; built-in commands remain available.";
            registry._document = new ExtensionRegistryDocument( );
            registry.DataStore = ExtensionDataStore.Empty( catalog );

            // Settings saves are checked against the stored registry revision, so
            // advertise it with the built-in sources. The empty document keeps its
            // own revision, so no registry commit can overwrite the stored one.
            var storedRevision = await catalog.ReadExtensionRegistryRevisionAsync( cancellationToken ).ConfigureAwait( false );
            registry._snapshot = new ContributionRegistrySnapshot( storedRevision, [], [], [] );
        }
        return registry;
    }

    public ExtensionInstallation Find( string keyOrPackageId )
    {
        var result = Document.Installations.FirstOrDefault( installation => installation.Key == keyOrPackageId || installation.Manifest.Id == keyOrPackageId );
        if ( result is null )
        {
            throw new InvalidOperationException( "The extension is not installed." );
        }

        return result;
    }

    public string ArtifactDirectory( ExtensionInstallation installation ) =>
        _artifacts.DirectoryFor( installation );

    public static string InstallationKey( string origin, string packageId ) =>
        Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( origin + "\0" + packageId ) ) ).ToLowerInvariant( );

    public async ValueTask<ExtensionInstallation> InstallAsync(
        StagedExtension staged, string origin, long expectedRevision, bool enable, string[]? approvedGrants,
        bool reviewedPermissionChanges, CancellationToken cancellationToken )
    {
        if ( origin is not { Length: > 0 and <= 2048 } || origin.Any( char.IsControl ) )
        {
            throw new InvalidDataException( "The extension origin is invalid." );
        }
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        string? materializedKey = null;
        try
        {
            EnsureRevision( expectedRevision );
            var manifest = staged.Manifest;
            ManifestValidation.Validate( manifest );
            _ = ExtensionRuntimeSupport.Require( manifest );
            var key = InstallationKey( origin, manifest.Id );
            var previous = Document.Installations.FirstOrDefault( installation => installation.Manifest.Id == manifest.Id );
            ValidateReplacement( previous, key, staged );
            var requests = manifest.Capabilities.Keys;
            if ( !reviewedPermissionChanges && requests.Except( previous?.Manifest.Capabilities.Keys.AsEnumerable( ) ?? [], StringComparer.Ordinal ).Any( ) )
            {
                throw new InvalidOperationException( "Review the added extension capabilities before activating this package." );
            }
            var grants = approvedGrants ?? previous?.Grants.Intersect( requests, StringComparer.Ordinal ).ToArray( ) ?? [];
            ValidateGrants( manifest, grants );
            var versions = new[] { new ExtensionVersion( manifest.Version, staged.Digest ) }
                .Concat( previous?.Versions ?? [] ).DistinctBy( version => version.Digest ).Take( 8 ).ToArray( );
            ExtensionInstallation installation = new( )
            {
                Key = key,
                Origin = origin,
                Digest = staged.Digest,
                Manifest = manifest,
                Enabled = enable || previous is { Enabled: true, Removed: false },
                GrantRevision = (previous?.GrantRevision ?? 0) + 1,
                Grants = grants,
                Versions = versions,
                Settings = previous?.Settings ?? new( StringComparer.Ordinal )
            };
            materializedKey = key;
            _ = _artifacts.Materialize( staged, key );
            await CommitAsync( Document with
            {
                Installations = Document.Installations.Where( current => current.Key != key ).Append( installation ).ToArray( )
            }, key, cancellationToken ).ConfigureAwait( false );
            return installation;
        }
        finally
        {
            if ( materializedKey is not null )
            {
                _artifacts.Cleanup( Document, materializedKey );
            }

            _gate.Release( );
        }
    }

    public ValueTask SetEnabledAsync( string? key, bool enabled, long expectedRevision, CancellationToken cancellationToken )
    {
        if ( key is null )
        {
            return MutateDocumentAsync( expectedRevision, null, document => document with { Enabled = enabled }, cancellationToken );
        }

        return MutateAsync( key, expectedRevision, installation =>
        {
            if ( installation.Removed )
            {
                throw new InvalidOperationException( "Reinstall the removed extension before enabling it." );
            }

            return installation with { Enabled = enabled };
        }, cancellationToken );
    }

    private void ValidateReplacement( ExtensionInstallation? previous, string key, StagedExtension staged )
    {
        if ( previous is not null && previous.Key != key )
        {
            throw new InvalidOperationException( "This package ID belongs to another origin. Remove its retained identity before replacing the origin." );
        }
        if ( previous is null && Document.Installations.Length >= ManifestValidation.MaximumPackages )
        {
            throw new InvalidOperationException( "The extension inventory limit was reached." );
        }
        if ( previous?.Versions.Any( version => version.Version == staged.Manifest.Version && version.Digest != staged.Digest ) == true )
        {
            throw new InvalidOperationException( "This version was previously installed with different bytes. Publish a new version." );
        }
    }

    public async ValueTask RemoveAsync( string key, bool eraseSettings, long expectedRevision, CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            EnsureRevision( expectedRevision );
            var installation = Find( key );
            await ReplaceAsync( installation with
            {
                Removed = true,
                Enabled = false,
                GlobalSearchEnabled = false,
                Grants = [],
                GrantRevision = installation.GrantRevision + 1,
                Settings = eraseSettings ? new( StringComparer.Ordinal ) : installation.Settings
            }, eraseSettings ? installation.Key : null, cancellationToken ).ConfigureAwait( false );
            _artifacts.Remove( installation.Key );
        }
        finally
        {
            _gate.Release( );
        }
    }

    public async ValueTask RollbackAsync( string key, string digest, long expectedRevision, CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            EnsureRevision( expectedRevision );
            var installation = Find( key );
            if ( installation.Removed || !installation.Versions.Any( version => version.Digest == digest ) )
            {
                throw new InvalidOperationException( "The requested version is not retained." );
            }
            var directory = Path.Combine( ArtifactRoot, installation.Key, "versions", digest );
            var manifest = ManifestValidation.Parse( await File.ReadAllBytesAsync( Path.Combine( directory, "commanddeck.extension.json" ), cancellationToken ).ConfigureAwait( false ) );
            _ = ExtensionRuntimeSupport.Require( manifest );
            await ReplaceAsync( installation with
            {
                Digest = digest,
                Manifest = manifest,
                GrantRevision = installation.GrantRevision + 1,
                Grants = installation.Grants.Intersect( manifest.Capabilities.Keys, StringComparer.Ordinal ).ToArray( ),
                GlobalSearchEnabled = false
            }, cancellationToken ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    public ValueTask SetGrantsAsync( string key, string[] grants, long expectedRevision, CancellationToken cancellationToken ) =>
        MutateAsync( key, expectedRevision, installation =>
        {
            ValidateGrants( installation.Manifest, grants );
            return installation with { Grants = grants };
        }, cancellationToken );

    private ValueTask ReplaceAsync( ExtensionInstallation installation, CancellationToken cancellationToken ) =>
        ReplaceAsync( installation, null, cancellationToken );

    private ValueTask ReplaceAsync( ExtensionInstallation installation, string? eraseStorageKey, CancellationToken cancellationToken ) =>
        CommitAsync( Document with
        {
            Installations = Document.Installations.Select( current => current.Key == installation.Key ? installation : current ).ToArray( )
        }, installation.Key, eraseStorageKey, cancellationToken );

    private ValueTask CommitAsync( ExtensionRegistryDocument document, string? key, CancellationToken cancellationToken ) =>
        CommitAsync( document, key, null, cancellationToken );

    private async ValueTask CommitAsync( ExtensionRegistryDocument document, string? key, string? eraseStorageKey, CancellationToken cancellationToken )
    {
        using var executions = await LockExecutionsAsync( key, cancellationToken ).ConfigureAwait( false );
        if ( eraseStorageKey is not null )
        {
            _ = checked(DataStore.Read( eraseStorageKey ).Revision + 1);
        }

        document = document with
        {
            Revision = checked(Document.Revision + 1)
        };
        var snapshot = ContributionSnapshotBuilder.Build( document );
        Func<string?, CancellationToken, ValueTask>[] revocations;
        lock ( _revocations )
        {
            revocations = [.. _revocations.Values];
        }
        foreach ( var revoke in revocations )
        {
            await revoke( key, cancellationToken ).ConfigureAwait( false );
        }
        await _catalog.CommitExtensionRegistryAsync( document, snapshot, Document.Revision, eraseStorageKey, null, cancellationToken ).ConfigureAwait( false );
        if ( eraseStorageKey is not null )
        {
            DataStore.PublishErase( eraseStorageKey );
        }
        Volatile.Write( ref _document, document );
        Volatile.Write( ref _snapshot, snapshot );
    }

    private void EnsureRevision( long expectedRevision )
    {
        if ( Error is not null )
        {
            throw new InvalidOperationException( Error );
        }
        if ( Document.Revision != expectedRevision )
        {
            throw new InvalidOperationException( "The extension registry changed. Refresh and try again." );
        }
    }

    private static void ValidateDocument( ExtensionRegistryDocument document )
    {
        if ( document.Version != 1 || document.Revision < 0 || document.Installations is null || document.Installations.Length > ManifestValidation.MaximumPackages )
        {
            throw new InvalidDataException( "The extension registry is invalid." );
        }
        HashSet<string> ids = new( StringComparer.Ordinal );
        foreach ( var installation in document.Installations )
        {
            ValidateInstallation( installation );
            if ( !ids.Add( installation.Manifest.Id ) )
            {
                throw new InvalidDataException( "The extension package identity is duplicated." );
            }
        }
    }

    private static void ValidateInstallation( ExtensionInstallation installation )
    {
        if ( installation is null || installation.Manifest is null || installation.Origin is null || installation.Key is null || installation.Digest is null )
        {
            throw new InvalidDataException( "The extension installation is invalid." );
        }
        ManifestValidation.Validate( installation.Manifest );
        if ( installation.Key != InstallationKey( installation.Origin, installation.Manifest.Id ) ||
             installation.Digest.Length != 64 || installation.Digest.Any( c => !char.IsAsciiHexDigit( c ) ) || installation.Versions is null || installation.Versions.Length > 8 ||
             installation.Settings is null || installation.Settings.Count > 16 || installation.GrantRevision < 1 )
        {
            throw new InvalidDataException( "The extension installation is invalid." );
        }
        ValidateGrants( installation.Manifest, installation.Grants );
        if ( installation.StorageRevision < 0 )
        {
            throw new InvalidDataException( "The storage revision is invalid." );
        }
        ExtensionDataStore.Validate( installation.Storage );
        foreach ( var version in installation.Versions )
        {
            if ( version is null || version.Digest is not { Length: 64 } || version.Digest.Any( c => !char.IsAsciiHexDigit( c ) ) )
            {
                throw new InvalidDataException( "The retained extension version is invalid." );
            }
            ManifestValidation.ValidateVersion( version.Version );
        }
    }

    private static void ValidateGrants( ExtensionManifest manifest, string[]? grants )
    {
        if ( grants is null || grants.Length > 16 || grants.Distinct( StringComparer.Ordinal ).Count( ) != grants.Length ||
             grants.Any( grant => grant is null || !manifest.Capabilities.ContainsKey( grant ) ) )
        {
            throw new InvalidDataException( "The extension grants contain an undeclared capability." );
        }
    }

    /// <summary>Orders a bounded host effect with this installation's revocation. Never await child RPC, network I/O, credential resolution or browser launches here.</summary>
    public async ValueTask<T> WithExecutionAsync<T>( string key, Func<ExtensionInstallation, ValueTask<T>> operation, CancellationToken cancellationToken )
    {
        var canonicalKey = Find( key ).Key;
        using var execution = await LockExecutionsAsync( canonicalKey, cancellationToken ).ConfigureAwait( false );
        var installation = Find( canonicalKey );
        if ( Error is not null || !Document.Enabled || !installation.Enabled || installation.Removed )
        {
            throw new InvalidOperationException( "The extension is disabled or unavailable." );
        }
        return await operation( installation ).ConfigureAwait( false );
    }

    public void Dispose( )
    {
        DataStore.Dispose( );
        foreach ( var gate in _executionGates.Values )
        {
            gate.Dispose( );
        }
        _gate.Dispose( );
    }
}
