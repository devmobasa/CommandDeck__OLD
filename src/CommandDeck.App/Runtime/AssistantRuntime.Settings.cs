namespace CommandDeck;

internal sealed partial class AssistantRuntime
{

    public async ValueTask<AgentBackendInventory> GetAgentBackendsAsync( CancellationToken cancellationToken )
    {
        var preferences = await RefreshAgentPreferenceCacheAsync( cancellationToken ).ConfigureAwait( false );
        List<AgentBackendDescriptor> backends = [.. _backends!.All.Select( static backend => backend.Describe( ) )];
        var effectiveAi = _aiSettings.Current;
        if ( effectiveAi.Enabled )
        {
            foreach ( var provider in effectiveAi.Registry.GetInventory( ).Providers )
            {
                backends.Add( DescribeAnswerOnly( provider ) );
            }
        }
        return new AgentBackendInventory
        {
            Backends = backends,
            SelectedBackendId = preferences.BackendId ?? (preferences.Enabled ? AgentBackendIds.Codex : null),
            Enabled = preferences.Enabled
        };
    }

    public async ValueTask<AgentStatus> GetAgentStatusAsync( CancellationToken cancellationToken )
    {
        var preferences = await RefreshAgentPreferenceCacheAsync( cancellationToken ).ConfigureAwait( false );
        var selectedBackend = SelectedAgentBackend( );
        var login = selectedBackend is null
            ? new AgentLoginState { State = AgentAuthenticationStates.NotApplicable, BackendId = preferences.BackendId }
            : await selectedBackend.GetLoginStateAsync( cancellationToken ).ConfigureAwait( false );
        var helper = await ReadHelperStatusAsync( cancellationToken ).ConfigureAwait( false );
        List<string> warnings = [];
        var sandbox = File.Exists( _agentOptions.BubblewrapPath );
        var transient = File.Exists( _agentOptions.SystemdRunPath );
        if ( !sandbox )
        {
            warnings.Add( "Bubblewrap is not installed; sandboxed levels cannot start." );
        }
        if ( !transient )
        {
            warnings.Add( "systemd-run is unavailable; unrestricted mode cannot start." );
        }
        if ( _codex?.BinaryReviewed == false )
        {
            warnings.Add( $"Codex {_codex.BinaryVersion} is outside the reviewed App Server version; it runs because agent.maximumCodex*Version admits it." );
        }
        if ( selectedBackend?.Id == AgentBackendIds.Codex &&
            CodexBinaryLocator.LauncherWarning( _agentOptions ) is { } launcherWarning )
        {
            warnings.Add( launcherWarning );
        }

        return new AgentStatus
        {
            Enabled = preferences.Enabled,
            Backend = selectedBackend?.Describe( ) ?? DescribeAnswerOnlyBackend( preferences.BackendId ),
            Trust = AgentTrustService.Describe( preferences ),
            Login = login,
            Preferences = preferences,
            SandboxAvailable = sandbox,
            TransientUnitAvailable = transient,
            BinaryPath = _codex?.BinaryPath,
            BinaryVersion = _codex?.BinaryVersion,
            BinaryReviewed = _codex?.BinaryReviewed,
            Helper = helper,
            Conversation = _agent!.Summary( ),
            PendingRequestCount = _agent.PendingRequestCount,
            Warnings = warnings
        };
    }

    private async ValueTask<AgentHelperStatus> ReadHelperStatusAsync( CancellationToken cancellationToken )
    {
        if ( _helper is null )
        {
            return new AgentHelperStatus { Installed = false, Message = "The package helper is not installed." };
        }
        try
        {
            return await _helper.GetStatusAsync( cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is InvalidOperationException or IOException )
        {
            return new AgentHelperStatus { Installed = false, Message = "The package helper is not reachable." };
        }
    }

    public async ValueTask<AgentModelInventory> DiscoverAgentModelsAsync(
        string? backendId,
        bool includeHidden,
        CancellationToken cancellationToken )
    {
        var preferences = await RefreshAgentPreferenceCacheAsync( cancellationToken ).ConfigureAwait( false );
        if ( !preferences.Enabled )
        {
            return new AgentModelInventory
            {
                BackendId = backendId ?? AgentBackendIds.Codex,
                Error = new AiError { Category = AiErrorCategories.Disabled, Summary = "The assistant is disabled." }
            };
        }
        var backend = _backends!.Resolve( backendId ?? preferences.BackendId );
        if ( backend is null )
        {
            return new AgentModelInventory
            {
                BackendId = backendId ?? preferences.BackendId ?? AgentBackendIds.Codex,
                Error = new AiError { Category = AiErrorCategories.Unsupported, Summary = "Use the AI provider model refresh for answer-only backends." }
            };
        }
        return await backend.DiscoverModelsAsync( includeHidden || preferences.ShowHiddenModels, cancellationToken )
            .ConfigureAwait( false );
    }

    public ValueTask<AgentPreferences> GetAgentPreferencesAsync( CancellationToken cancellationToken ) =>
        RefreshAgentPreferenceCacheAsync( cancellationToken );

    public async ValueTask<AgentPreferences> SetAgentPreferencesAsync(
        AgentPreferences incoming,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( incoming );
        AgentContractValidator.ValidatePreferences( incoming );
        // The epoch is captured with the snapshot this save validates against and
        // carried to the write unchanged: an authority change in between makes the
        // owner reject the save instead of writing settings over it.
        var snapshot = await Agent.LoadSnapshotAsync( cancellationToken ).ConfigureAwait( false );
        var current = snapshot.Preferences;
        if ( incoming.TrustLevel != current.TrustLevel || incoming.TrustDuration != current.TrustDuration ||
            incoming.TrustLeaseExpiresUnixMilliseconds != current.TrustLeaseExpiresUnixMilliseconds )
        {
            throw new InvalidOperationException( "Use the trust challenge to change the trust level." );
        }
        if ( incoming.BackendId is not null && _backends?.Contains( incoming.BackendId ) != true &&
            !_aiSettings.Current.Options.Providers.Any( provider => provider.Enabled && string.Equals( provider.Id, incoming.BackendId, StringComparison.OrdinalIgnoreCase ) ) )
        {
            throw new InvalidOperationException( "The selected backend is not configured." );
        }
        ValidateLoopbackAcknowledgement( incoming.LoopbackPreflightProviderId );
        AgentAuthoritySnapshot saved;
        if ( current.Enabled && !incoming.Enabled )
        {
            // The owner clears every grant and all authority in the same step as the write.
            saved = await _authority!.DisableAsync( new AgentPreferences
            {
                Enabled = false,
                BackendId = incoming.BackendId,
                ModelId = incoming.ModelId,
                ReasoningEffort = incoming.ReasoningEffort,
                ShowHiddenModels = incoming.ShowHiddenModels,
                ConversationTtlMinutes = incoming.ConversationTtlMinutes,
                TurnTimeoutMinutes = incoming.TurnTimeoutMinutes
            }, cancellationToken ).ConfigureAwait( false );
        }
        else
        {
            // Trust level, duration, and lease are not settings: the owner copies
            // them from the record it reads at write time.
            AgentPreferences settings = new( )
            {
                Enabled = incoming.Enabled,
                BackendId = incoming.BackendId,
                ModelId = incoming.ModelId,
                ReasoningEffort = incoming.ReasoningEffort,
                ContextPolicy = incoming.ContextPolicy,
                ShowHiddenModels = incoming.ShowHiddenModels,
                WebSearchEnabled = incoming.WebSearchEnabled,
                UseCodexConfiguration = incoming.UseCodexConfiguration,
                AurEnabled = false,
                ConversationTtlMinutes = incoming.ConversationTtlMinutes,
                TurnTimeoutMinutes = incoming.TurnTimeoutMinutes,
                LoopbackPreflightProviderId = incoming.LoopbackPreflightProviderId
            };
            saved = await _authority!.UpdateSettingsAsync( settings, snapshot.SessionEpoch, cancellationToken ).ConfigureAwait( false );
        }
        // Write first, revoke second: a change that ends sessions advanced the
        // epoch in the same step as the write, so a turn admitted after it runs
        // with the saved settings and only older work is ended here.
        await Agent.RevokeStaleAsync( CancellationToken.None ).ConfigureAwait( false );
        // Report the record as it stands now: a change that landed after this
        // write, such as a downgrade during the revocation, is part of it.
        return _authority.Current;
    }

    // Customization reset ends the assistant as disable does: the owner resets
    // the record and every grant, then work admitted before it is ended.
    public async ValueTask ResetAuthorityAsync( CancellationToken cancellationToken )
    {
        await _authority!.ResetAsync( cancellationToken ).ConfigureAwait( false );
        await Agent.RevokeStaleAsync( CancellationToken.None ).ConfigureAwait( false );
    }

    private void ValidateLoopbackAcknowledgement( string? providerId )
    {
        if ( providerId is null )
        {
            return;
        }
        var provider = _aiSettings.Current.Options.Providers.FirstOrDefault( value =>
            value.Enabled && string.Equals( value.Id, providerId, StringComparison.OrdinalIgnoreCase ) );
        if ( provider is null || !Uri.TryCreate( provider.BaseUrl, UriKind.Absolute, out var uri ) ||
            !AiEndpointPolicy.IsLiteralLoopback( uri ) )
        {
            throw new InvalidOperationException( "The loopback preflight acknowledgement requires a literal loopback provider." );
        }
    }

    public async ValueTask<AgentLoginState> StartAgentLoginAsync(
        string method,
        AiCredentialOptions? credential,
        CancellationToken cancellationToken,
        string? backendId = null )
    {
        var preferences = await RefreshAgentPreferenceCacheAsync( cancellationToken ).ConfigureAwait( false );
        if ( !preferences.Enabled )
        {
            throw new InvalidOperationException( "Enable the assistant before signing in." );
        }
        if ( method == AgentLoginMethods.ApiKey && (credential is null || credential.Kind == AiCredentialKinds.None) )
        {
            credential = _agentOptions.ApiKeyCredential;
        }
        var login = await ResolveAgentBackend( backendId ).StartLoginAsync( method, credential, cancellationToken ).ConfigureAwait( false );
        return login;
    }

    public ValueTask<AgentLoginState> GetAgentLoginStateAsync( string? backendId, CancellationToken cancellationToken ) =>
        ResolveAgentBackend( backendId ).GetLoginStateAsync( cancellationToken );

    public ValueTask<AgentLoginState> CancelAgentLoginAsync( CancellationToken cancellationToken, string? backendId = null ) =>
        ResolveAgentBackend( backendId ).CancelLoginAsync( cancellationToken );

    public async ValueTask<AgentLoginState> CompleteAgentLoginAsync( string code, CancellationToken cancellationToken, string? backendId = null )
    {
        if ( ResolveAgentBackend( backendId ) is not IAgentCodeLoginBackend codeLogin )
        {
            throw new AiOperationException(
                AiErrorCategories.Unsupported,
                safeSummary: "The selected backend does not finish sign-in with a pasted code." );
        }
        var login = await codeLogin.CompleteLoginAsync( code, cancellationToken ).ConfigureAwait( false );
        return login;
    }

    public async ValueTask<AgentLoginState> AgentLogoutAsync( CancellationToken cancellationToken, string? backendId = null )
    {
        var backend = ResolveAgentBackend( backendId );
        // Sign-out clears only a conversation bound to that backend; other
        // backends keep their sessions and credentials.
        if ( string.Equals( _agent?.Summary( )?.BackendId, backend.Id, StringComparison.Ordinal ) )
        {
            await Agent.ClearAsync( cancellationToken ).ConfigureAwait( false );
        }
        return await backend.LogoutAsync( cancellationToken ).ConfigureAwait( false );
    }

    private AgentBackendDescriptor? DescribeAnswerOnlyBackend( string? providerId )
    {
        if ( providerId is null )
        {
            return null;
        }
        var effectiveAi = _aiSettings.Current;
        var provider = effectiveAi.Enabled
            ? effectiveAi.Registry.GetInventory( ).Providers.FirstOrDefault( value =>
                string.Equals( value.Id, providerId, StringComparison.OrdinalIgnoreCase ) )
            : null;
        return provider is null ? null : DescribeAnswerOnly( provider );
    }

    private static AgentBackendDescriptor DescribeAnswerOnly( AiProviderDescriptor provider ) => new( )
    {
        Id = provider.Id,
        DisplayName = provider.DisplayName,
        Kind = AgentBackendKinds.AnswerOnly,
        ProcessLocality = AiLocality.Local,
        InferenceLocality = provider.Locality,
        Available = provider.Enabled,
        HealthState = provider.HealthState,
        AuthenticationState = AgentAuthenticationStates.NotApplicable,
        Capabilities = new AgentBackendCapabilities
        {
            Conversation = false,
            Tools = false,
            Reasoning = false,
            ModelDiscovery = provider.Capabilities.Discovery,
            PreflightRequired = true,
            Usage = provider.Capabilities.Usage,
            Login = false,
            ContractVersion = provider.Capabilities.AdapterContractVersion
        },
        Explanation = "Answer-only. Each question is a fresh request reviewed in the preflight.",
        ConfiguredModel = provider.ConfiguredModel
    };

    public void PresentLoginResult( AgentLoginState login, string owner ) => RegisterLoginResult( login, owner );

    private void RegisterLoginResult( AgentLoginState login, string owner )
    {
        if ( login.State != AgentAuthenticationStates.Pending || login.VerificationUrl is null )
        {
            return;
        }
        List<CommandAction> actions =
        [
            new CommandAction
            {
                Id = AgentLoginActionIds.OpenUrl,
                Title = "Open Login Page",
                Kind = ActionKinds.OpenUrl,
                Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["urlTemplate"] = login.VerificationUrl }
            },
            new CommandAction
            {
                Id = AgentLoginActionIds.CopyUrl,
                Title = "Copy URL",
                Kind = ActionKinds.CopyText,
                Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = login.VerificationUrl }
            }
        ];
        if ( login.UserCode is not null )
        {
            actions.Add( new CommandAction
            {
                Id = AgentLoginActionIds.CopyCode,
                Title = "Copy Code",
                Kind = ActionKinds.CopyText,
                Arguments = new Dictionary<string, string>( StringComparer.Ordinal ) { ["text"] = login.UserCode }
            } );
        }
        var result = _cacheSyntheticResult( new SearchResult
        {
            Id = "agent:login",
            Kind = ResultKinds.Assistant,
            Title = AgentBackendIds.DisplayName( login.BackendId ?? AgentBackendIds.Codex ) + " login",
            SourceId = SourceIds.Ai,
            ProviderId = ProviderIds.Agent,
            Actions = actions
        }, owner );
        login.InvocationReference = result.InvocationReference;
    }
}
