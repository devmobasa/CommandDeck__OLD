namespace CommandDeck;

public static class ProtocolLimits
{
    public const int CurrentVersion = 3;
    public const int MaximumRequestBytes = 256 * 1024;
    public const int MaximumResponseBytes = 2 * 1024 * 1024;
    public const int MaximumCustomizationExportBytes = 256 * 1024;
    public const int MaximumCustomizationSummaries = 256;
    public const int MaximumQueryCharacters = 4 * 1024;
    public const int MaximumPromptCharacters = 64 * 1024;
    public const int MaximumIdentifierCharacters = 512;
    public const int MaximumConcurrentRequestsPerClient = 4;
    public const int MaximumPreviewBytes = 8 * 1024;
    public const int MaximumPreviewLines = 200;
}

public static partial class ProtocolRequestValidator
{
    public static void Validate( ProtocolRequest request, ContributionRegistrySnapshot? registry = null, bool deferFilterResolution = false )
    {
        ArgumentNullException.ThrowIfNull( request );
        if ( request.Version != ProtocolLimits.CurrentVersion )
        {
            throw new InvalidOperationException(
                $"Unsupported protocol version {request.Version}. Expected version " +
                $"{ProtocolLimits.CurrentVersion}." );
        }

        if ( request.RequestId <= 0 )
        {
            throw new InvalidOperationException( "requestId must be a positive integer." );
        }

        if ( request.Offset < 0 )
        {
            throw new InvalidOperationException( "offset must not be negative." );
        }

        if ( request.Limit is < 1 or > 256 )
        {
            throw new InvalidOperationException( "limit must be between 1 and 256." );
        }

        if ( string.Equals( request.Operation, ProtocolOperations.Cancel, StringComparison.Ordinal ) &&
            request.TargetRequestId <= 0 )
        {
            throw new InvalidOperationException( "cancel requires a positive targetRequestId." );
        }

        ValidateEnabledFlag( request );

        CheckLength( request.Operation, nameof( request.Operation ), 32 );
        CheckLength( request.Query, nameof( request.Query ), ProtocolLimits.MaximumQueryCharacters );
        CheckLength( request.ResultId, nameof( request.ResultId ), ProtocolLimits.MaximumIdentifierCharacters );
        CheckLength( request.TargetResultId, nameof( request.TargetResultId ), ProtocolLimits.MaximumIdentifierCharacters );
        CheckLength( request.InvocationReference, nameof( request.InvocationReference ), ProtocolLimits.MaximumIdentifierCharacters );
        CheckLength( request.ActionId, nameof( request.ActionId ), ProtocolLimits.MaximumIdentifierCharacters );
        CheckLength( request.ProviderId, nameof( request.ProviderId ), ProtocolLimits.MaximumIdentifierCharacters );
        ActionInputValues.Validate( request.InputValues );
        ValidateBrowserFields( request );
        ValidateAiFields( request );
        ValidateAgentFields( request );
        ValidateQueryExpression( request, registry, deferFilterResolution );
        ValidateClipboardFields( request );
        ValidateExtensionFields( request );
        ValidateExtensionSession( request );
        CheckLength( request.Scope, nameof( request.Scope ), 32 );
        CheckLength( request.Path, nameof( request.Path ), 4 * 1024 );
        CheckLength( request.RootId, nameof( request.RootId ), ProtocolLimits.MaximumIdentifierCharacters );
        CheckLength( request.Alias, nameof( request.Alias ), 32 );
        CheckLength(
            request.TargetFilterId,
            nameof( request.TargetFilterId ),
            ProtocolLimits.MaximumIdentifierCharacters );
        CheckLength(
            request.CustomizationId,
            nameof( request.CustomizationId ),
            ProtocolLimits.MaximumIdentifierCharacters );
        CheckLength(
            request.ActiveApplication,
            nameof( request.ActiveApplication ),
            ProtocolLimits.MaximumIdentifierCharacters );

        if ( request.Preferences is not null && request.Preferences.Sources is null )
        {
            throw new InvalidOperationException( "preferences.sources must be an array." );
        }

        if ( request.Preferences?.Sources.Count > 160 )
        {
            throw new InvalidOperationException( "preferences contains more than 160 sources." );
        }

        if ( request.Preferences?.Sources.Any( static source =>
                source is null || string.IsNullOrWhiteSpace( source.SourceId ) ) == true )
        {
            throw new InvalidOperationException(
                "preferences.sources must contain valid source objects." );
        }

        if ( request.StableIds is null )
        {
            throw new InvalidOperationException( "stableIds must be an array." );
        }

        if ( request.StableIds.Count > CustomizationLimits.MaximumReorderItems ||
            request.StableIds.Any( static value =>
                value is null || value.Length > ProtocolLimits.MaximumIdentifierCharacters ) )
        {
            throw new InvalidOperationException( "favorite reorder exceeds protocol limits." );
        }
        CheckLength(
            request.ActiveWorkspace,
            nameof( request.ActiveWorkspace ),
            ProtocolLimits.MaximumIdentifierCharacters );
        CheckLength( request.CurrentDirectory, nameof( request.CurrentDirectory ), 4 * 1024 );
        CheckLength( request.SelectedProject, nameof( request.SelectedProject ), 4 * 1024 );
        CheckLength( request.QuicklinkId, "quicklinkId", 64 );
        if ( request.Quicklink is not null )
        {
            QuicklinkValidator.Validate( request.Quicklink );
        }
        ValidateSavedSearch( request, registry, deferFilterResolution );
    }

    private static void ValidateAiFields( ProtocolRequest request )
    {
        CheckLength( request.ModelId, nameof( request.ModelId ), AiLimits.MaximumModelIdBytes );
        CheckLength( request.PreflightId, nameof( request.PreflightId ), 96 );
        CheckLength( request.PayloadDigest, nameof( request.PayloadDigest ), 64 );
        CheckLength( request.SessionId, nameof( request.SessionId ), 64 );
        CheckLength( request.OutputAction, nameof( request.OutputAction ), 16 );
        CheckLength( request.ConfirmationToken, nameof( request.ConfirmationToken ), 96 );
        CheckLength( request.AiTemplateId, nameof( request.AiTemplateId ), 64 );
        if ( request.AiDraft is not null )
        {
            AiContractValidator.ValidateDraft( request.AiDraft );
        }
        if ( request.AiProvider is not null )
        {
            ConfigurationValidator.ValidateAiProvider( request.AiProvider );
        }
        if ( request.ExpectedRevision is < 0 || request.DraftRevision < 0 )
        {
            throw new InvalidOperationException( "expectedRevision and draftRevision must not be negative." );
        }
        ValidateAiOperation( request );
        ValidateAiManagementOperation( request );
    }

    private static void ValidateAiManagementOperation( ProtocolRequest request )
    {
        switch ( request.Operation )
        {
            case ProtocolOperations.AiSettingsUpdate when request.Enabled is null:
                throw new InvalidOperationException( "ai.settings.update requires enabled." );
            case ProtocolOperations.AiProvidersSave when request.AiProvider is null:
                throw new InvalidOperationException( "ai.providers.save requires aiProvider." );
            case ProtocolOperations.AiProvidersDelete when
                string.IsNullOrWhiteSpace( request.ProviderId ) || !request.Confirmed:
                throw new InvalidOperationException(
                    "ai.providers.delete requires providerId and confirmed=true." );
            case ProtocolOperations.AiProvidersDelete:
                AiContractValidator.ValidateIdentifier( request.ProviderId, 64, "providerId" );
                break;
            case ProtocolOperations.AiProvidersTest when request.AiProvider is null:
                throw new InvalidOperationException( "ai.providers.test requires aiProvider." );
        }
    }

    private static void ValidateAiOperation( ProtocolRequest request )
    {
        switch ( request.Operation )
        {
            case ProtocolOperations.AiModelsGet:
                AiContractValidator.ValidateIdentifier( request.ProviderId, 64, "providerId" );
                if ( !request.Refresh )
                {
                    throw new InvalidOperationException( "ai.models.get requires refresh=true." );
                }
                break;
            case ProtocolOperations.AiSelectionUpdate:
                if ( request.ProviderId is null != (request.ModelId is null) )
                {
                    throw new InvalidOperationException(
                        "AI selection requires both providerId and modelId, or both null." );
                }
                if ( request.ProviderId is not null )
                {
                    AiContractValidator.ValidateIdentifier( request.ProviderId, 64, "providerId" );
                    AiContractValidator.ValidateUtf8(
                        request.ModelId, AiLimits.MaximumModelIdBytes, "modelId", false,
                        allowLineBreaks: false );
                }
                break;
            case ProtocolOperations.AiPreflight:
                if ( request.AiDraft is null )
                {
                    throw new InvalidOperationException( "ai.preflight requires aiDraft." );
                }
                break;
            case ProtocolOperations.AiStart:
                ValidateAiStart( request );
                break;
            case ProtocolOperations.AiTemplatesDelete when
                string.IsNullOrWhiteSpace( request.AiTemplateId ) || !request.Confirmed:
                throw new InvalidOperationException(
                    "ai.templates.delete requires templateId and confirmed=true." );
            case ProtocolOperations.AiTemplatesSave when request.AiTemplate is null:
                throw new InvalidOperationException( "ai.templates.save requires aiTemplate." );
            case ProtocolOperations.AiOutput when request.Generation <= 0 ||
                request.OutputAction is not "copy" and not "save" and not "open":
                throw new InvalidOperationException(
                    "ai.output requires generation and action copy, save, or open." );
        }
    }

    private static void ValidateAiStart( ProtocolRequest request )
    {
        if ( string.IsNullOrWhiteSpace( request.PreflightId ) ||
            request.PayloadDigest?.Length != 64 || request.Generation <= 0 ||
            string.IsNullOrWhiteSpace( request.SessionId ) )
        {
            throw new InvalidOperationException(
                "ai.start requires preflightId, SHA-256 payloadDigest, sessionId, and generation." );
        }
        AiContractValidator.ValidateIdentifier( request.PreflightId, 96, "preflightId" );
        if ( request.PayloadDigest.Any( static character => !char.IsAsciiHexDigit( character ) ) )
        {
            throw new InvalidOperationException( "payloadDigest must be a SHA-256 hex digest." );
        }
        if ( request.SessionId.Length is < 32 or > 64 ||
            request.SessionId.Any( static character => character > 0x7f ||
                !(char.IsAsciiLetterOrDigit( character ) || character is '.' or '_' or '-') ) )
        {
            throw new InvalidOperationException(
                "sessionId must use 32-64 safe ASCII characters." );
        }
    }

    private static void ValidateAgentFields( ProtocolRequest request )
    {
        CheckLength( request.BackendId, nameof( request.BackendId ), 64 );
        CheckLength( request.ReasoningEffort, nameof( request.ReasoningEffort ), 32 );
        CheckLength( request.LoginMethod, nameof( request.LoginMethod ), 32 );
        CheckLength( request.LoginCode, nameof( request.LoginCode ), AiProviderSettingsLimits.MaximumLoginCodeCharacters );
        CheckLength( request.ConversationId, nameof( request.ConversationId ), 96 );
        CheckLength( request.PendingRequestId, nameof( request.PendingRequestId ), 96 );
        CheckLength( request.Decision, nameof( request.Decision ), 32 );
        CheckLength( request.ChallengeId, nameof( request.ChallengeId ), 96 );
        CheckLength( request.Phrase, nameof( request.Phrase ), 64 );
        CheckLength( request.TrustDuration, nameof( request.TrustDuration ), 32 );
        CheckLength( request.GrantId, nameof( request.GrantId ), 96 );
        CheckLength( request.GrantKind, nameof( request.GrantKind ), 32 );
        CheckLength( request.GrantScope, nameof( request.GrantScope ), 4 * 1024 );
        CheckLength( request.PackageOperation, nameof( request.PackageOperation ), 16 );
        CheckLength( request.PreviewId, nameof( request.PreviewId ), 96 );
        CheckLength( request.OperationId, nameof( request.OperationId ), 96 );
        if ( request.PackageNames is null )
        {
            throw new InvalidOperationException( "packageNames must be an array." );
        }
        if ( request.PackageNames.Count > AgentLimits.MaximumPackageNames )
        {
            throw new InvalidOperationException( "packageNames exceeds the supported count." );
        }
        if ( request.Answers is not null )
        {
            if ( request.Answers.Count > AgentLimits.MaximumQuestionOptions )
            {
                throw new InvalidOperationException( "answers exceeds the supported count." );
            }
            foreach ( (var key, var value) in request.Answers )
            {
                CheckLength( key, "answers key", 96 );
                CheckLength( value, "answers value", AgentLimits.MaximumAnswerCharacters );
            }
        }
        if ( request.AgentPreferences is not null )
        {
            AgentContractValidator.ValidatePreferences( request.AgentPreferences );
        }
        ValidateAgentOperation( request );
    }

    private static void ValidateAgentOperation( ProtocolRequest request )
    {
        switch ( request.Operation )
        {
            case ProtocolOperations.AgentTurnStart:
                AgentContractValidator.ValidatePromptText( request.Query );
                break;
            case ProtocolOperations.AgentTurnInput when
                string.IsNullOrWhiteSpace( request.PendingRequestId ):
                throw new InvalidOperationException( "agent.turn.input requires pendingRequestId." );
            case ProtocolOperations.AgentContextResolve when
                string.IsNullOrWhiteSpace( request.PendingRequestId ) ||
                string.IsNullOrWhiteSpace( request.Decision ):
                throw new InvalidOperationException(
                    "agent.context.resolve requires pendingRequestId and decision." );
            case ProtocolOperations.AgentPreferencesUpdate when request.AgentPreferences is null:
                throw new InvalidOperationException( "agent.preferences.update requires agentPreferences." );
            case ProtocolOperations.AgentLoginStart when
                request.LoginMethod is not (AgentLoginMethods.DeviceCode or AgentLoginMethods.ApiKey or
                    AgentLoginMethods.Browser):
                throw new InvalidOperationException(
                    "agent.login.start requires loginMethod device-code, api-key, or browser." );
            case ProtocolOperations.AgentLoginComplete when
                string.IsNullOrWhiteSpace( request.LoginCode ) ||
                request.LoginCode.Length > AiProviderSettingsLimits.MaximumLoginCodeCharacters ||
                request.LoginCode.Any( char.IsControl ):
                throw new InvalidOperationException( "agent.login.complete requires a loginCode." );
            case ProtocolOperations.AgentOutput when request.Generation <= 0 ||
                request.OutputAction is not "copy" and not "save" and not "open":
                throw new InvalidOperationException(
                    "agent.output requires generation and action copy, save, or open." );
        }
        ValidateAgentAuthorityOperation( request );
        ValidatePackageOperation( request );
    }

    private static void ValidateAgentAuthorityOperation( ProtocolRequest request )
    {
        switch ( request.Operation )
        {
            case ProtocolOperations.AgentTrustChallenge when
                request.TrustLevel is null || !AgentTrustLevels.IsValid( request.TrustLevel.Value ):
                throw new InvalidOperationException( "agent.trust.challenge requires a valid trustLevel." );
            case ProtocolOperations.AgentTrustUpdate when
                string.IsNullOrWhiteSpace( request.ChallengeId ) || request.TrustLevel is null:
                throw new InvalidOperationException( "agent.trust.update requires challengeId and trustLevel." );
            case ProtocolOperations.AgentGrantsAdd when
                string.IsNullOrWhiteSpace( request.GrantKind ) || string.IsNullOrWhiteSpace( request.GrantScope ):
                throw new InvalidOperationException( "agent.grants.add requires grantKind and grantScope." );
            case ProtocolOperations.AgentGrantsRemove when string.IsNullOrWhiteSpace( request.GrantId ):
                throw new InvalidOperationException( "agent.grants.remove requires grantId." );
            case ProtocolOperations.AgentGrantsClear when !request.Confirmed:
                throw new InvalidOperationException( "agent.grants.clear requires confirmed: true." );
        }
    }

    private static void ValidatePackageOperation( ProtocolRequest request )
    {
        switch ( request.Operation )
        {
            case ProtocolOperations.PackageSearch when string.IsNullOrWhiteSpace( request.Query ):
                throw new InvalidOperationException( "package.search requires query." );
            case ProtocolOperations.PackagePreview when
                request.PackageOperation is not (PackageOperations.Install or PackageOperations.Remove) ||
                request.PackageNames.Count == 0:
                throw new InvalidOperationException(
                    "package.preview requires packageOperation and packageNames." );
            case ProtocolOperations.PackageCommit when
                string.IsNullOrWhiteSpace( request.PreviewId ) || !request.Confirmed:
                throw new InvalidOperationException( "package.commit requires previewId and confirmed: true." );
            case ProtocolOperations.PackageCancel when string.IsNullOrWhiteSpace( request.OperationId ):
                throw new InvalidOperationException( "package.cancel requires operationId." );
        }
    }

    private static bool OversizedExtensionHandle( ExtensionSessionInput input ) => input.ConfirmationId?.Length > 64 || input.SearchLease?.Length > 64;

    private static void ValidateExtensionSession( ProtocolRequest request )
    {
        if ( ProtocolRoutes.IsIn( request.Operation, ProtocolFamily.ExtensionSession ) )
        {
            var input = request.ExtensionInput;
            if ( input is null )
            {
                throw new InvalidOperationException( "Missing extension session input." );
            }

            if ( input.Features is null || input.Features.Length > 32 || input.Features.Any( feature => feature is null || feature.Length > 64 ) ||
                 input.Sequence < 0 || input.Revision < 0 || input.Handle?.Length > 64 || input.ItemId?.Length > 128 || OversizedExtensionHandle( input ) )
            {
                throw new InvalidOperationException( "Invalid extension session input." );
            }
            ValidateExtensionFormInput( input );
            if ( request.Operation == ProtocolOperations.ExtensionOpen && string.IsNullOrWhiteSpace( request.ResultId ) )
            {
                throw new InvalidOperationException( "Select an extension command." );
            }
            if ( request.Operation != ProtocolOperations.ExtensionOpen && string.IsNullOrWhiteSpace( request.SessionId ) )
            {
                throw new InvalidOperationException( "Missing extension session identity." );
            }
        }
    }

    private static void ValidateExtensionFormInput( ExtensionSessionInput input )
    {
        if ( input.FormRevision < 0 || input.Values is { Count: > 32 } || input.Values?.Keys.Any( key => key.Length > 64 ) == true )
        {
            throw new InvalidOperationException( "Invalid extension form input." );
        }
    }

    private static void ValidateExtensionFields( ProtocolRequest request )
    {
        request.Extension?.Validate( );
        if ( request.SourcePatch is { } patch )
        {
            if ( request.Preferences is not null || patch.RegistryRevision < 0 || patch.PreferenceRevision < 0 ||
                 patch.Changes is not { Count: > 0 and <= 32 } || patch.Changes.Any( change => change is null || string.IsNullOrWhiteSpace( change.SourceId ) || change.SourceId.Length > 128 ) )
            {
                throw new InvalidOperationException( "Source patches require 1-32 bounded changes and no full preferences." );
            }
        }
    }

    // favorite.set and hidden.set take an explicit Boolean; an omitted flag is
    // rejected rather than read as removal.
    private static void ValidateEnabledFlag( ProtocolRequest request )
    {
        var requiresEnabled =
            string.Equals( request.Operation, ProtocolOperations.FavoriteSet, StringComparison.Ordinal ) ||
            string.Equals( request.Operation, ProtocolOperations.HiddenSet, StringComparison.Ordinal );
        if ( requiresEnabled && request.Enabled is null )
        {
            throw new InvalidOperationException( $"{request.Operation} requires enabled." );
        }
    }

    private static void ValidateBrowserFields( ProtocolRequest request )
    {
        CheckLength( request.HerdrWorkspaceId, nameof( request.HerdrWorkspaceId ), 512 );
        if ( request.HerdrWorkspaceId is not null &&
            (request.HerdrWorkspaceId.Length == 0 || request.HerdrWorkspaceId.Any( char.IsControl )) )
        {
            throw new InvalidOperationException( "HerdrWorkspaceId must be a valid Herdr identifier." );
        }

        CheckLength( request.OmarchyMenuId, nameof( request.OmarchyMenuId ), 512 );
        if ( request.OmarchyMenuId is not null &&
            (request.OmarchyMenuId.Trim( ).Length == 0 || request.OmarchyMenuId.Any( char.IsControl )) )
        {
            throw new InvalidOperationException( "OmarchyMenuId must be a valid Omarchy menu identifier." );
        }

        if ( request.OmarchyMenuId is not null && (request.HerdrWorkspaceId is not null || request.BrowseDirectory is not null) )
        {
            throw new InvalidOperationException( "A search can browse only one Omarchy menu, Herdr space or folder." );
        }

        CheckLength( request.BrowseDirectory, nameof( request.BrowseDirectory ), 4096 );
        if ( request.BrowseOffset is < 0 or > 50000 )
        {
            throw new InvalidOperationException( "browseOffset must be between 0 and 50000." );
        }
    }

    private static void CheckLength( string? value, string name, int maximum )
    {
        if ( value?.Length > maximum )
        {
            throw new InvalidOperationException(
                $"{name} exceeds the {maximum}-character protocol limit." );
        }
    }

    private static void ValidateSavedSearch( ProtocolRequest request, ContributionRegistrySnapshot? registry, bool deferFilterResolution )
    {
        CheckLength(
            request.SavedSearchId,
            nameof( request.SavedSearchId ),
            ProtocolLimits.MaximumIdentifierCharacters );
        if ( request.SavedSearch is null )
        {
            return;
        }
        CheckLength( request.SavedSearch.Id, "savedSearch.id", 80 );
        CheckLength( request.SavedSearch.Name, "savedSearch.name", 64 );
        _ = SavedSearchValidator.ValidateName( request.SavedSearch.Name );
        _ = deferFilterResolution ? QueryParser.ValidateShape( request.SavedSearch.Expression ) : SavedSearchValidator.ValidateExpression( request.SavedSearch.Expression, registry );
        _ = SavedSearchValidator.NormalizeShortcut( request.SavedSearch.Shortcut );
    }

    private static void ValidateQueryExpression( ProtocolRequest request, ContributionRegistrySnapshot? registry, bool deferFilterResolution )
    {
        if ( request.Expression is null )
        {
            throw new InvalidOperationException( "expression must be an object." );
        }
        if ( request.Operation is ProtocolOperations.AgentTurnStart or ProtocolOperations.PackageSearch )
        {
            return;
        }
        if ( deferFilterResolution )
        {
            _ = QueryParser.ValidateShape( request.Expression );
            _ = QueryParser.Parse( request.Query );
        }
        else
        {
            _ = QueryParser.Parse( request.Query, request.Expression, registry: registry );
        }
    }
}
