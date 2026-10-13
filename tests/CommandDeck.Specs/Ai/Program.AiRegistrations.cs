namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunAiRegistrationsAsync( )
    {
        Run( "AI protocol contracts reject invalid authority", AiContractsRejectInvalidAuthority );
        Run( "AI configuration is validated before transport", AiConfigurationIsValidatedBeforeTransport );
        await RunAsync(
            "AI credentials resolve only from bounded named sources",
            AiCredentialsResolveOnlyFromBoundedNamedSourcesAsync );
        await RunAsync(
            "AI stream parsers handle arbitrary UTF-8 boundaries",
            AiStreamParsersHandleArbitraryUtf8BoundariesAsync );
        await RunAsync(
            "AI streams enforce an idle deadline",
            AiStreamsEnforceAnIdleDeadlineAsync );
        await RunAsync(
            "AI discovery and generation enforce fixed operation deadlines",
            AiOperationsEnforceFixedDeadlinesAsync );
        await RunAsync(
            "AI providers discover and stream through explicit contracts",
            AiProvidersDiscoverAndStreamThroughExplicitContractsAsync );
        await RunAsync(
            "AI provider errors are bounded and normalized",
            AiProviderErrorsAreBoundedAndNormalizedAsync );
        await RunAsync(
            "AI generation binds preflight stream and output to one client",
            AiGenerationBindsPreflightStreamAndOutputToClientAsync );
        await RunAsync(
            "AI generation cancellation emits exactly one terminal",
            AiGenerationCancellationHasOneTerminalAsync );
        await RunAsync(
            "AI attachments are reauthorized immediately before transmission",
            AiAttachmentsAreReauthorizedBeforeTransmissionAsync );
        await RunAsync(
            "AI file authority is handle-first and rejects replacement symlinks",
            AiFileAuthorityIsHandleFirstAsync );
        await RunAsync(
            "AI preflight attaches only the requesting client's result snapshots",
            AiPreflightAttachesOnlyOwnedResultsAsync );
        await RunAsync(
            "AI preflight capacity is isolated per client",
            AiPreflightCapacityIsPerClientAsync );
        await RunAsync(
            "ai.clear is a per-client completion barrier",
            AiClearIsPerClientBarrierAsync );
        await RunAsync(
            "AI cancellation ownership serializes clear and disposal",
            AiCancellationOwnershipSerializesClearAndDisposalAsync );
        await RunAsync(
            "socket ai.clear acknowledges only after prior AI work is unrecoverable",
            SocketAiClearIsACompletionBarrierAsync );
        await RunAsync(
            "socket AI reads answer while an answer streams on the same connection",
            SocketAiReadsAnswerWhileAnAnswerStreamsAsync );
        await RunAsync(
            "socket ai.clear keeps the connection's file selection and trust challenges",
            SocketAiClearKeepsFileSelectionAndTrustChallengesAsync );
        await RunAsync(
            "AI completed output retention remains globally bounded",
            AiCompletedOutputRetentionIsBoundedAsync );
        await RunAsync(
            "AI output staging is private at file creation",
            AiOutputStagingIsPrivateAtCreationAsync );
        await RunAsync(
            "AI selection templates and preflight are durable and bounded",
            AiSelectionTemplatesAndPreflightAreDurableAndBoundAsync );
    }
}
