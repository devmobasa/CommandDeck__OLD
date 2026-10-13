namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{

    public AiProviderInventory GetAiProviders( )
    {
        var effective = EffectiveAiSettings;
        if ( !effective.Enabled )
        {
            return new AiProviderInventory( );
        }
        return effective.Registry.GetInventory( );
    }

    public ValueTask<AiModelInventory> DiscoverAiModelsAsync(
        string providerId,
        CancellationToken cancellationToken )
    {
        var effective = EffectiveAiSettings;
        if ( !effective.Enabled )
        {
            return ValueTask.FromResult( new AiModelInventory
            {
                ProviderId = providerId,
                Error = new AiError
                {
                    Category = AiErrorCategories.Disabled,
                    Summary = "AI support is disabled."
                }
            } );
        }
        return effective.Registry.DiscoverModelsAsync( providerId, cancellationToken );
    }

    public async ValueTask<AiSelection> GetAiSelectionAsync(
        CancellationToken cancellationToken )
    {
        var stored = await _aiPreferences.GetAiSelectionAsync(
            cancellationToken ).ConfigureAwait( false );
        var effective = EffectiveAiSettings;
        if ( !effective.Enabled )
        {
            return new AiSelection
            {
                ProviderId = stored.ProviderId,
                ModelId = stored.ModelId,
                State = AiSelectionStates.Disabled,
                Explanation = "AI support is disabled."
            };
        }
        if ( stored.ProviderId is null )
        {
            return stored;
        }
        return effective.Registry.ReconcileSelection( stored );
    }

    public async ValueTask<AiSelection> SetAiSelectionAsync(
        string? providerId,
        string? modelId,
        CancellationToken cancellationToken )
    {
        if ( providerId is not null && !EffectiveAiOptions.Providers.Any( value =>
            value.Enabled && string.Equals( value.Id, providerId, StringComparison.OrdinalIgnoreCase ) ) )
        {
            throw new InvalidOperationException( "The selected AI provider is unavailable." );
        }
        _ = await _aiPreflights.MutateAuthorityAsync(
            token => _aiPreferences.SetAiSelectionAsync(
                providerId, modelId, token ),
            cancellationToken ).ConfigureAwait( false );
        return await GetAiSelectionAsync( cancellationToken ).ConfigureAwait( false );
    }

    public ValueTask<IReadOnlyList<AiTemplate>> GetAiTemplatesAsync(
        CancellationToken cancellationToken ) =>
        _aiPreferences.GetAiTemplatesAsync( cancellationToken );

    public async ValueTask<IReadOnlyList<AiTemplate>> SaveAiTemplateAsync(
        AiTemplate promptTemplate,
        CancellationToken cancellationToken )
    {
        return await _aiPreflights.MutateAuthorityAsync(
            token => _aiPreferences.SaveAiTemplateAsync(
                promptTemplate, token ),
            cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<IReadOnlyList<AiTemplate>> DeleteAiTemplateAsync(
        string templateId,
        CancellationToken cancellationToken )
    {
        return await _aiPreflights.MutateAuthorityAsync(
            token => _aiPreferences.DeleteAiTemplateAsync( templateId, token ),
            cancellationToken ).ConfigureAwait( false );
    }

    public ValueTask<AiPreflight> CreateAiPreflightAsync(
        string clientOwnerId,
        AiDraft draft,
        CancellationToken cancellationToken )
    {
        if ( !EffectiveAiSettings.Enabled )
        {
            return ValueTask.FromException<AiPreflight>(
                new InvalidOperationException( "AI support is disabled." ) );
        }
        return _aiPreflights.CreateAsync( clientOwnerId, draft, cancellationToken );
    }

    public ValueTask StreamAiAsync(
        string clientOwnerId,
        string preflightId,
        string digest,
        long generation,
        Func<AiChunk, ValueTask> onChunk,
        Func<AiDone, ValueTask> onDone,
        CancellationToken cancellationToken ) =>
        _aiGenerations.RunAsync(
            clientOwnerId, preflightId, digest, generation, onChunk, onDone, cancellationToken );

    public ValueTask<ActionExecutionResult> ExecuteAiOutputAsync(
        string clientOwnerId,
        long generation,
        string action,
        string? targetPath,
        string? confirmationToken,
        CancellationToken cancellationToken ) => _aiOutputs.ExecuteAsync(
            clientOwnerId,
            generation,
            action,
            targetPath,
            confirmationToken,
            cancellationToken );

    /// <summary>
    /// Purges the client's direct-AI state for ai.clear: unconsumed preflights,
    /// its generation, completed output, and overwrite challenges. The file
    /// selection and trust challenges belong to the connection, not to an answer.
    /// </summary>
    public void ClearDirectAiClient( string clientOwnerId )
    {
        _aiPreflights.ClearClient( clientOwnerId );
        _aiGenerations.ClearClient( clientOwnerId );
        _aiOutputs.ClearClient( clientOwnerId );
    }
}
