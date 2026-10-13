namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{
    private static readonly TimeSpan AiCancellationBound = TimeSpan.FromSeconds( 5 );
    // Serializes management mutations. Lock order is fixed: this mutex first,
    // then the preflight authority gate inside MutateAuthorityAsync. Nothing
    // awaits a generation while holding either.
    private readonly SemaphoreSlim _aiSettingsMutex = new( 1, 1 );

    /// <summary>The effective provider definitions, honouring a live settings mutation.</summary>
    public AiOptions EffectiveAiOptions => _aiSettings.Current.Options;

    public AiEffectiveProviderSettings EffectiveAiSettings => _aiSettings.Current;

    public async ValueTask<AiProviderSettings> GetAiSettingsAsync( CancellationToken cancellationToken ) =>
        _aiSettings.Describe( await GetAiSelectionAsync( cancellationToken ).ConfigureAwait( false ) );

    public async ValueTask<AiProviderSettings> UpdateAiEnabledAsync(
        bool enabled,
        long? expectedRevision,
        CancellationToken cancellationToken )
    {
        await MutateAiSettingsAsync(
            expectedRevision,
            document => new AiProviderSettingsDocument
            {
                Revision = document.Revision,
                Enabled = enabled,
                Providers = document.Providers
            },
            cancellationToken ).ConfigureAwait( false );
        return await GetAiSettingsAsync( cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<AiProviderSaveResult> SaveAiProviderAsync(
        AiProviderOptions provider,
        bool create,
        long? expectedRevision,
        bool selectAfterSave,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( provider );
        ConfigurationValidator.ValidateAiProvider( provider );
        var published = await MutateAiSettingsAsync(
            expectedRevision,
            document =>
            {
                var index = document.Providers.FindIndex( value =>
                    string.Equals( value.Id, provider.Id, StringComparison.OrdinalIgnoreCase ) );
                if ( create && index >= 0 )
                {
                    throw new AiOperationException(
                        AiErrorCategories.Conflict,
                        safeSummary: $"An AI provider with ID '{provider.Id}' already exists." );
                }
                if ( !create && index < 0 )
                {
                    throw new AiOperationException(
                        AiErrorCategories.NotFound,
                        safeSummary: $"AI provider '{provider.Id}' was not found." );
                }
                List<AiProviderOptions> providers = [.. document.Providers];
                if ( index >= 0 )
                {
                    providers[index] = provider;
                }
                else
                {
                    providers.Add( provider );
                }
                return new AiProviderSettingsDocument
                {
                    Revision = document.Revision,
                    Enabled = document.Enabled,
                    Providers = providers
                };
            },
            cancellationToken ).ConfigureAwait( false );

        // Save and use is a reported sequence, not a cross-store transaction: the
        // provider commit above stands even when the selection below fails.
        AiSelection? selection = null;
        AiError? selectionError = null;
        var applied = false;
        if ( selectAfterSave )
        {
            try
            {
                if ( !published.Enabled )
                {
                    throw new AiOperationException(
                        AiErrorCategories.Disabled,
                        safeSummary: "Direct AI is off; enable it before selecting a provider." );
                }
                selection = await SetAiSelectionAsync( provider.Id, provider.Model, cancellationToken )
                    .ConfigureAwait( false );
                applied = true;
            }
            catch ( AiOperationException exception )
            {
                selectionError = new AiError { Category = exception.Category, Summary = exception.SafeSummary };
            }
            catch ( InvalidOperationException exception )
            {
                selectionError = new AiError
                {
                    Category = AiErrorCategories.InvalidConfiguration,
                    Summary = SafeSummary( exception.Message )
                };
            }
        }
        var settings = await GetAiSettingsAsync( cancellationToken ).ConfigureAwait( false );
        return new AiProviderSaveResult
        {
            ProviderId = provider.Id,
            Settings = settings,
            SelectionRequested = selectAfterSave,
            SelectionApplied = applied,
            Selection = selection ?? settings.Selection,
            SelectionError = selectionError
        };
    }

    public async ValueTask<AiProviderSettings> DeleteAiProviderAsync(
        string providerId,
        long? expectedRevision,
        CancellationToken cancellationToken )
    {
        AiContractValidator.ValidateIdentifier( providerId, 64, "providerId" );
        await MutateAiSettingsAsync(
            expectedRevision,
            document =>
            {
                List<AiProviderOptions> providers = [.. document.Providers.Where( value =>
                    !string.Equals( value.Id, providerId, StringComparison.OrdinalIgnoreCase ) )];
                if ( providers.Count == document.Providers.Count )
                {
                    throw new AiOperationException(
                        AiErrorCategories.NotFound,
                        safeSummary: $"AI provider '{providerId}' was not found." );
                }
                return new AiProviderSettingsDocument
                {
                    Revision = document.Revision,
                    Enabled = document.Enabled,
                    Providers = providers
                };
            },
            cancellationToken ).ConfigureAwait( false );
        // A stored selection naming the removed provider is preserved and reported
        // as unavailable; no other destination is chosen on the user's behalf.
        return await GetAiSettingsAsync( cancellationToken ).ConfigureAwait( false );
    }

    public ValueTask<AiProviderTestResult> TestAiProviderDraftAsync(
        AiProviderOptions draft,
        long draftRevision,
        long? expectedRevision,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( draft );
        var current = _aiSettings.Current;
        if ( expectedRevision is not null && expectedRevision.Value != current.Revision )
        {
            throw new AiOperationException(
                AiErrorCategories.Conflict,
                safeSummary: "The AI provider settings changed; reload before testing again." );
        }
        if ( !current.Enabled )
        {
            throw new AiOperationException(
                AiErrorCategories.Disabled,
                safeSummary: "Direct AI is off. Enable it to test a provider; saving still works offline." );
        }
        return _aiSettings.TestDraftAsync( draft, draftRevision, cancellationToken );
    }

    private async ValueTask<AiEffectiveProviderSettings> MutateAiSettingsAsync(
        long? expectedRevision,
        Func<AiProviderSettingsDocument, AiProviderSettingsDocument> transform,
        CancellationToken cancellationToken )
    {
        await _aiSettingsMutex.WaitAsync( cancellationToken ).ConfigureAwait( false );
        PreparedAiProviderSettings? prepared = null;
        try
        {
            var current = _aiSettings.Current;
            if ( current.Error is not null )
            {
                throw new AiOperationException(
                    AiErrorCategories.InvalidConfiguration,
                    safeSummary: "The managed AI provider settings document is invalid; repair it and restart the daemon." );
            }
            if ( expectedRevision is not null && expectedRevision.Value != current.Revision )
            {
                throw new AiOperationException(
                    AiErrorCategories.Conflict,
                    safeSummary: "The AI provider settings changed; reload before saving again." );
            }
            var baseline = _aiSettings.CurrentDocument( );
            var transformed = transform( baseline );
            AiProviderSettingsDocument candidate = new( )
            {
                Revision = current.Revision + 1,
                Enabled = transformed.Enabled,
                Providers = transformed.Providers
            };
            // Validation and adapter construction happen before any lock that a
            // transport start could wait on; neither touches a credential or the network.
            prepared = _aiSettings.Prepare( candidate );
            var candidateRegistry = prepared;
            var published = await _aiPreflights.MutateAuthorityAsync(
                _ =>
                {
                    // Under the authority gate no direct request can start. The store
                    // recompares the file identity captured at load, then the atomic
                    // replacement is the commit point and the new snapshot is published
                    // before the gate releases and invalidates every pending preflight.
                    var identity = _aiSettings.Store.Write( candidate, current.Identity );
                    return ValueTask.FromResult( _aiSettings.Publish( candidateRegistry, identity ) );
                },
                cancellationToken ).ConfigureAwait( false );
            prepared = null;
            var affected = AffectedProviders( current.Options, published.Options );
            if ( affected.Count > 0 )
            {
                // Outside the gate: cancel streams that already started against a
                // changed, disabled, or removed definition and wait a bounded time.
                _ = await _aiGenerations.CancelAsync(
                    affected.Contains, AiCancellationBound, cancellationToken ).ConfigureAwait( false );
            }
            return published;
        }
        finally
        {
            if ( prepared is not null )
            {
                AiProviderSettingsService.Discard( prepared );
            }
            _aiSettingsMutex.Release( );
        }
    }

    /// <summary>
    /// Providers whose active generations must stop: every provider on master
    /// disable, otherwise those removed, disabled, or whose transport, credential,
    /// model, or context definition changed. A display-name-only edit is exempt.
    /// </summary>
    internal static HashSet<string> AffectedProviders( AiOptions before, AiOptions after )
    {
        HashSet<string> affected = new( StringComparer.OrdinalIgnoreCase );
        if ( before.Enabled && !after.Enabled )
        {
            foreach ( var provider in before.Providers )
            {
                affected.Add( provider.Id );
            }
            return affected;
        }
        foreach ( var previous in before.Providers )
        {
            var next = after.Providers.FirstOrDefault( value =>
                string.Equals( value.Id, previous.Id, StringComparison.OrdinalIgnoreCase ) );
            if ( next is null || (previous.Enabled && !next.Enabled) || !SameTransportDefinition( previous, next ) )
            {
                affected.Add( previous.Id );
            }
        }
        return affected;
    }

    private static bool SameTransportDefinition( AiProviderOptions left, AiProviderOptions right ) =>
        string.Equals( left.Kind, right.Kind, StringComparison.Ordinal ) &&
        string.Equals( left.BaseUrl, right.BaseUrl, StringComparison.Ordinal ) &&
        string.Equals( left.Model, right.Model, StringComparison.Ordinal ) &&
        left.AllowRemoteEndpoint == right.AllowRemoteEndpoint &&
        left.ContextLimit == right.ContextLimit &&
        string.Equals( left.Credential.Kind, right.Credential.Kind, StringComparison.Ordinal ) &&
        string.Equals( left.Credential.Name, right.Credential.Name, StringComparison.Ordinal );

    private static string SafeSummary( string message )
    {
        var trimmed = message.Length > 240 ? message[..240] : message;
        return new string( trimmed.Where( static character => !char.IsControl( character ) ).ToArray( ) );
    }
}
