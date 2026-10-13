namespace CommandDeck;

/// <summary>
/// The immutable effective provider state: which definitions are live, where
/// they came from, and the registry built from them. Replaced as a whole on
/// every acknowledged mutation.
/// </summary>
public sealed class AiEffectiveProviderSettings
{
    public required AiOptions Options { get; init; }
    public required string Source { get; init; }
    public long Revision { get; init; }
    public bool LegacyIgnored { get; init; }
    public AiError? Error { get; init; }
    public required AiProviderSettingsIdentity Identity { get; init; }
    public required AiProviderRegistry Registry { get; init; }

    public bool Enabled => Error is null && Options.Enabled;
}

/// <summary>
/// A validated candidate whose registry has been constructed without credential
/// resolution or network I/O, waiting to be persisted and published.
/// </summary>
public sealed class PreparedAiProviderSettings
{
    internal PreparedAiProviderSettings( AiProviderSettingsDocument document, AiProviderRegistry registry )
    {
        Document = document;
        Registry = registry;
    }

    public AiProviderSettingsDocument Document { get; }

    internal AiProviderRegistry Registry { get; }
}

/// <summary>
/// Owns the effective provider snapshot and the live registry. Consumers read
/// <see cref="Current"/> instead of the startup options, so a management
/// mutation takes effect in the running daemon without a restart.
/// </summary>
public sealed class AiProviderSettingsService : IAsyncDisposable
{
    private readonly object _sync = new( );
    private readonly AiOptions _legacy;
    private readonly IAiHttpTransportFactory _transports;
    private readonly IAiCredentialResolver _credentials;
    private readonly AiOperationDeadlines _deadlines;
    private readonly SemaphoreSlim _discoveryCapacity = new( 2, 2 );
    private AiEffectiveProviderSettings _current;
    private int _disposed;

    public AiProviderSettingsService(
        AiOptions legacyOptions,
        AiProviderSettingsStore store,
        IAiHttpTransportFactory? transportFactory = null,
        IAiCredentialResolver? credentialResolver = null,
        AiOperationDeadlines? deadlines = null )
    {
        if ( legacyOptions is null )
        {
            throw new ArgumentNullException( nameof( legacyOptions ) );
        }

        _legacy = legacyOptions;
        if ( store is null )
        {
            throw new ArgumentNullException( nameof( store ) );
        }

        Store = store;
        _transports = transportFactory ?? new AiHttpTransportFactory( );
        _credentials = credentialResolver ?? new AiCredentialResolver( );
        _deadlines = deadlines ?? new AiOperationDeadlines( );
        _current = Resolve( store.Load( ) );
    }

    public AiProviderSettingsStore Store { get; }

    public AiEffectiveProviderSettings Current => Volatile.Read( ref _current );

    /// <summary>
    /// The document a mutation must be based on. When no managed document exists
    /// the effective legacy set is seeded at revision 0, so the first mutation
    /// carries every existing definition into the managed file.
    /// </summary>
    public AiProviderSettingsDocument CurrentDocument( )
    {
        var current = Current;
        return new AiProviderSettingsDocument
        {
            Revision = current.Revision,
            Enabled = current.Error is null && current.Options.Enabled,
            Providers = [.. current.Options.Providers]
        };
    }

    public PreparedAiProviderSettings Prepare( AiProviderSettingsDocument candidate )
    {
        ArgumentNullException.ThrowIfNull( candidate );
        AiOptions options = new( )
        {
            Enabled = candidate.Enabled,
            Providers = [.. candidate.Providers]
        };
        ConfigurationValidator.ValidateAi( options );
        // Construction resolves no credential and opens no connection; it only
        // builds adapters and transports around validated definitions.
        AiProviderRegistry registry = new( options, _transports, _credentials, _deadlines, _discoveryCapacity );
        return new PreparedAiProviderSettings( candidate, registry );
    }

    public AiEffectiveProviderSettings Publish(
        PreparedAiProviderSettings prepared,
        AiProviderSettingsIdentity identity )
    {
        ArgumentNullException.ThrowIfNull( prepared );
        ArgumentNullException.ThrowIfNull( identity );
        AiEffectiveProviderSettings replaced;
        AiEffectiveProviderSettings published = new( )
        {
            Options = prepared.Registry.Options,
            Source = AiProviderSettingsSources.ManagedDocument,
            Revision = prepared.Document.Revision,
            LegacyIgnored = _legacy.Providers.Count > 0 || _legacy.Enabled,
            Identity = identity,
            Registry = prepared.Registry
        };
        lock ( _sync )
        {
            ObjectDisposedException.ThrowIf( _disposed != 0, this );
            replaced = _current;
            Volatile.Write( ref _current, published );
        }
        // The old registry stays usable for operations that already hold a lease;
        // its transports are disposed when the last lease is released.
        replaced.Registry.Retire( );
        return published;
    }

    public static void Discard( PreparedAiProviderSettings prepared )
    {
        ArgumentNullException.ThrowIfNull( prepared );
        prepared.Registry.Retire( );
    }

    /// <summary>
    /// Explicit connection test for an unsaved draft: one bounded model-discovery
    /// call through a temporary adapter that shares the live discovery capacity.
    /// The draft is never registered as a live provider and nothing is persisted.
    /// </summary>
    public async ValueTask<AiProviderTestResult> TestDraftAsync(
        AiProviderOptions draft,
        long draftRevision,
        CancellationToken cancellationToken )
    {
        ConfigurationValidator.ValidateAiProvider( draft );
        AiProviderOptions enabled = new( )
        {
            Id = draft.Id,
            DisplayName = draft.DisplayName,
            Kind = draft.Kind,
            BaseUrl = draft.BaseUrl,
            Model = draft.Model,
            Credential = draft.Credential,
            AllowRemoteEndpoint = draft.AllowRemoteEndpoint,
            Enabled = true,
            ContextLimit = draft.ContextLimit
        };
        AiProviderRegistry temporary = new(
            new AiOptions { Enabled = true, Providers = [enabled] },
            _transports,
            _credentials,
            _deadlines,
            _discoveryCapacity );
        try
        {
            if ( !temporary.TryGet( draft.Id, out var provider ) || provider is null )
            {
                throw new InvalidOperationException( "The draft provider could not be prepared." );
            }
            var descriptor = provider.Describe( );
            var inventory = await temporary.DiscoverModelsAsync( draft.Id, cancellationToken ).ConfigureAwait( false );
            var outcome = inventory.Error is null
                ? inventory.Models.Count == 0 ? AiProviderTestOutcomes.Empty : AiProviderTestOutcomes.Models
                : inventory.Error.Category == AiErrorCategories.Unsupported
                    ? AiProviderTestOutcomes.Unsupported
                    : AiProviderTestOutcomes.Failed;
            return new AiProviderTestResult
            {
                ProviderId = draft.Id,
                DraftRevision = draftRevision,
                Outcome = outcome,
                Destination = descriptor.Destination,
                Locality = descriptor.Locality,
                CredentialState = descriptor.CredentialState,
                Models = inventory.Models,
                Error = inventory.Error
            };
        }
        finally
        {
            temporary.Retire( );
        }
    }

    public AiProviderSettings Describe( AiSelection? selection )
    {
        var current = Current;
        return new AiProviderSettings
        {
            Enabled = current.Enabled,
            Source = current.Source,
            Revision = current.Revision,
            LegacyIgnored = current.LegacyIgnored,
            Providers = [.. current.Options.Providers],
            Selection = selection,
            Presets = [.. AiProviderPresets.All],
            ReservedIds = [.. AgentBackendIds.All],
            Error = current.Error
        };
    }

    private AiEffectiveProviderSettings Resolve( AiProviderSettingsLoadResult loaded )
    {
        if ( loaded.Error is not null )
        {
            // Managed document present but unusable: direct inference is off and the
            // legacy section stays inactive so a broken file cannot resurrect old endpoints.
            return new AiEffectiveProviderSettings
            {
                Options = new AiOptions { Enabled = false, Providers = [] },
                Source = AiProviderSettingsSources.ManagedDocument,
                Revision = 0,
                LegacyIgnored = true,
                Error = loaded.Error,
                Identity = loaded.Identity,
                Registry = new AiProviderRegistry(
                    new AiOptions { Enabled = false, Providers = [] },
                    _transports, _credentials, _deadlines, _discoveryCapacity )
            };
        }
        if ( loaded.Document is { } document )
        {
            AiOptions managed = new( )
            {
                Enabled = document.Enabled,
                Providers = [.. document.Providers]
            };
            return new AiEffectiveProviderSettings
            {
                Options = managed,
                Source = AiProviderSettingsSources.ManagedDocument,
                Revision = document.Revision,
                LegacyIgnored = _legacy.Providers.Count > 0 || _legacy.Enabled,
                Identity = loaded.Identity,
                Registry = new AiProviderRegistry( managed, _transports, _credentials, _deadlines, _discoveryCapacity )
            };
        }
        return new AiEffectiveProviderSettings
        {
            Options = _legacy,
            Source = AiProviderSettingsSources.LegacyConfiguration,
            Revision = 0,
            Identity = AiProviderSettingsIdentity.Absent,
            Registry = new AiProviderRegistry( _legacy, _transports, _credentials, _deadlines, _discoveryCapacity )
        };
    }

    public async ValueTask DisposeAsync( )
    {
        if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
        {
            return;
        }
        await Current.Registry.DisposeAsync( ).ConfigureAwait( false );
        _discoveryCapacity.Dispose( );
    }
}
