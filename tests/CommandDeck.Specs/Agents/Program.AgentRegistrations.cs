namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunAgentRegistrationsAsync( )
    {
        Run(
            "agent contracts enforce trust levels preference bounds and package names",
            AgentContractsEnforceTrustAndPreferenceBounds );
        Run(
            "agent runtime home configuration and sandbox arguments stay closed",
            AgentRuntimeHomeAndSandboxArgumentsStayClosed );
        Run(
            "sandbox binds the bundled code-mode host when present",
            SandboxBindsTheCodeModeHostWhenPresent );
        await RunAsync(
            "agent unified routing uses only the selected backend",
            AgentUnifiedRoutingUsesOnlyTheSelectedBackendAsync );
        await RunAsync(
            "Codex version range admits the 0.x line marked unreviewed but rejects a major bump",
            CodexVersionRangeAdmitsTheZeroLineButRejectsAMajorBumpAsync );
        await RunAsync(
            "Codex discovery skips launcher shims and reports direct-path options",
            CodexDiscoverySkipsLauncherShimsAsync );
        await RunAsync(
            "unrestricted launch names the fix when the Codex socket link is unreachable",
            UnrestrictedLaunchNamesTheFixForAnUnreachableSocketLinkAsync );
        Run(
            "unknown prefixes and assistant prompts are not search errors",
            UnknownPrefixesAndPromptsAreNotSearchErrors );
        Run(
            "developer instructions steer file work to the CommandDeck tools",
            DeveloperInstructionsSteerFileWorkToCommandDeckTools );
        Run(
            "agent credential store owns a private file and runtime homes wipe cleanly",
            AgentCredentialStoreOwnsAPrivateFile );
        await RunAsync(
            "agent context broker enforces trust levels grants and protected paths",
            AgentContextBrokerEnforcesLevelsAndProtectedPathsAsync );
        await RunAsync(
            "agent context broker rejects intermediate symlink and parent replacement escapes",
            AgentContextBrokerRejectsIntermediateSymlinkEscapesAsync );
        await RunAsync(
            "agent context protects configured credential directories through the runtime",
            AgentContextProtectsConfiguredCredentialDirectoriesAsync );
        await RunAsync(
            "agent context hides configured credentials from parent listings and searches",
            AgentContextHidesConfiguredCredentialsAsync );
        await RunAsync(
            "agent text searches share the per-turn UTF-8 context budget with file reads",
            AgentTextSearchesShareTheContextBudgetAsync );
        await RunAsync(
            "agent command tool plans literal argv with bound identity",
            AgentCommandToolPlansLiteralArgvAsync );
        Run(
            "command risk classifier auto-approves only direct read-only tools",
            ClassifyCommandRiskAllowsOnlyDirectReadOnlyCommands );
        await RunAsync(
            "agent trust service binds challenges phrases and leases",
            AgentTrustServiceBindsChallengesAndLeasesAsync );
        await RunAsync(
            "agent settings save cannot restore trust a downgrade revoked",
            AgentSettingsSaveCannotRestoreRevokedTrustAsync );
        await RunAsync(
            "agent lease enforcement keeps a trust raise that finished during its read",
            AgentLeaseEnforcementKeepsAFreshRaiseAsync );
        await RunAsync(
            "agent customization reset revokes assistant authority",
            AgentCustomizationResetRevokesAssistantAuthorityAsync );
        await RunAsync(
            "agent authority reset waits for and supersedes an in-flight write",
            AgentAuthorityResetWaitsForAnInFlightWriteAsync );
        Run(
            "only the agent authority owner holds the preference store or writes its tables",
            AgentAuthorityIsTheOnlyPreferenceStoreHolder );
        await RunAsync(
            "agent turn prepared across a downgrade never starts",
            AgentTurnPreparedAcrossADowngradeNeverStartsAsync );
        await RunAsync(
            "agent prewarm prepared across a downgrade stays quiet",
            AgentPrewarmPreparedAcrossADowngradeStaysQuietAsync );
        await RunAsync(
            "agent settings save captured before a reset is rejected",
            AgentSettingsSaveRejectedAfterResetAsync );
        await RunAsync(
            "agent settings save captured before a disable is rejected",
            AgentSettingsSaveRejectedAfterDisableAsync );
        await RunAsync(
            "agent stale settings save reports conflict over the socket",
            AgentStaleSettingsSaveReportsConflictOverTheSocketAsync );
        await RunAsync(
            "agent turn admitted during a save's revocation survives it",
            AgentTurnAdmittedDuringARevocationSurvivesItAsync );
        await RunAsync(
            "agent turn from before a saved change does not survive it",
            AgentNoTurnFromBeforeASavedChangeSurvivesItAsync );
        await RunAsync(
            "agent authority rejects a save validated before a reset or disable",
            AgentAuthorityRejectsAStaleSaveAsync );
        await RunAsync(
            "agent losing preparer cleans up only its own process generation",
            AgentLosingPreparerCleansUpOnlyItsGenerationAsync );
        await RunAsync(
            "Codex backend admits processes only at the current epoch",
            CodexBackendAdmitsProcessesOnlyAtTheCurrentEpochAsync );
        await RunAsync(
            "Codex turn launching during a downgrade is stopped and fails retryably",
            CodexTurnLaunchingDuringADowngradeIsStoppedAsync );
        await RunAsync(
            "Codex prewarm launching during a downgrade is stopped and stays quiet",
            CodexPrewarmLaunchingDuringADowngradeStaysQuietAsync );
        await RunAsync(
            "agent preferences and grants persist without secrets or exports",
            AgentPreferencesAndGrantsPersistWithoutSecretsAsync );
        await RunAsync(
            "JSON-RPC connection correlates requests and fails closed",
            JsonRpcConnectionCorrelatesRequestsAndFailsClosedAsync );
        await RunAsync(
            "JSON-RPC connection answers unexpected handler failures generically",
            JsonRpcConnectionAnswersUnexpectedHandlerFailuresAsync );
        await RunAsync(
            "JSON-RPC connection bounds concurrent server requests",
            JsonRpcConnectionBoundsConcurrentServerRequestsAsync );
        await RunAsync(
            "JSON-RPC connection stops reading requests while their replies are unsent",
            JsonRpcConnectionStopsReadingWhileRepliesAreUnsentAsync );
        await RunAsync(
            "JSON-RPC connection disposal ends a wait for unsent replies",
            JsonRpcConnectionDisposalEndsAWaitForUnsentRepliesAsync );
        await RunAsync(
            "JSON-RPC connection disposal waits for in-flight handlers",
            JsonRpcConnectionDisposeWaitsForInFlightHandlersAsync );
        await RunAsync(
            "Codex client handshake thread and turn follow the closed contract",
            CodexClientHandshakeThreadAndTurnFollowTheClosedContractAsync );
        await RunAsync(
            "Codex session maps failures interrupts and declines unrelated requests",
            CodexSessionMapsFailuresInterruptsAndDeclinesAsync );
        await RunAsync(
            "Codex backend owns login credential copy-back and runtime lifecycle",
            CodexBackendOwnsLoginAndRuntimeLifecycleAsync );
        await RunAsync(
            "Codex logout retires credentials despite rejected RPC late login and cancellation",
            CodexLogoutRetiresCredentialsAcrossFailuresAsync );
        await RunAsync(
            "Codex logout retires a process with an unanswered account read",
            CodexLogoutDoesNotWaitForAccountReadAsync );
        await RunAsync(
            "Codex startup account read has a deadline and retires the failed runtime",
            CodexStartupAccountReadHasDeadlineAsync );
        await RunAsync(
            "agent socket streams daemon-owned turns and parks questions across clients",
            AgentSocketStreamsDaemonOwnedTurnsAndParksQuestionsAsync );
        await RunAsync(
            "agent socket trust challenges and master disable reset authority",
            AgentSocketTrustChallengesAndDisableResetAuthorityAsync );
        await RunAsync(
            "package commit rechecks the assistant's trust and enablement",
            PackageCommitRechecksAssistantAuthorityAsync );
        await RunAsync( "agent authority expires admissions on observed external permission changes", AgentAuthorityObservesExternalRevocationAsync );
        await RunAsync( "agent turn stream follows external authority refresh", AgentTurnStreamFollowsExternalAuthorityRefreshAsync );
        await RunAsync(
            "agent tool failures finish the activity as failed and the turn continues",
            AgentToolUnexpectedFailuresFinishTheActivityAsync );
        await RunAsync(
            "agent login page opens through the cached action",
            AgentLoginPageOpensThroughCachedActionAsync );
        await RunAsync(
            "agent replaces a faulted session before the next turn",
            AgentReplacesAFaultedSessionBeforeTheNextTurnAsync );
        await RunAsync(
            "agent starts the next turn as soon as the previous one is done",
            AgentStartsTheNextTurnAsSoonAsTheLastIsDoneAsync );
        await RunAsync(
            "agent protected and command decisions stay bound to the requesting client",
            AgentSensitiveDecisionsStayBoundToTheRequestingClientAsync );
        await RunAsync(
            "agent turn outlives the client that started it",
            AgentTurnOutlivesTheClientThatStartedItAsync );
        await RunAsync(
            "agent clear fences non-cooperative old conversation generations",
            AgentClearFencesNonCooperativeOldGenerationsAsync );
        await RunAsync(
            "agent stream bounds count UTF-8 bytes and drop slow subscribers",
            AgentStreamingBoundsCountUtf8BytesAndDropSlowSubscribersAsync );
        await RunAsync(
            "agent prewarm starts the session without running a turn and no-ops when off",
            AgentPrewarmStartsTheSessionWithoutRunningATurnAsync );
        await RunAsync(
            "agent sweep keeps the process warm past the conversation TTL",
            AgentSweepKeepsTheProcessWarmPastTheConversationTtlAsync );
        await RunAsync(
            "agent sweep does not stop a session that is still preparing",
            AgentSweepDoesNotStopASessionThatIsStillPreparingAsync );
        await RunAsync(
            "agent sweep ends an unrestricted turn when its lease expires",
            AgentSweepEndsAnUnrestrictedTurnWhenItsLeaseExpiresAsync );
    }
}
