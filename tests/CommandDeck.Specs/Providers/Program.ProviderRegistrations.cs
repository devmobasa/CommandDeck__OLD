namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunProviderRegistrationsAsync( )
    {
        await RunAsync( "process runner enforces finite RSS when the kernel wrapper is unavailable", ProcessRunnerRetainsResidentMemoryFallbackAsync );
        await RunAsync( "clipboard capture sleeps while disabled paused or capturing", ClipboardCaptureSleepsWhileIdleAsync );
        await RunAsync( "process runner applies both kernel address space limits", ProcessRunnerUsesKernelMemoryLimitsAsync );
        await RunAsync( "process runner sleeps with kernel or unlimited memory limits", ProcessRunnerSleepsWithKernelOrUnlimitedMemoryAsync );
        await RunAsync( "clipboard capture revokes sessions on policy changes", ClipboardCaptureRevokesOnPolicyChangeAsync );
        await RunAsync( "clipboard capture wakes promptly while disabled or paused", ClipboardCaptureWaitsForPolicyChangesWhileOffAsync );
        await RunAsync( "clipboard capture wakes when migration review is acknowledged", ClipboardCaptureStartsWhenMigrationReviewIsAcknowledgedAsync );
        await RunAsync( "clipboard capture retries stopped helpers with backoff", ClipboardCaptureRetriesStoppedHelperWithBackoffAsync );
        await RunAsync( "clipboard retention schedules the next expiry and bounds clock rechecks", ClipboardRetentionPrunesAtNextExpiryAsync );
        await RunAsync( "clipboard open preparation mints capabilities without effects", ClipboardOpenPreparationMintsCapabilitiesWithoutEffectsAsync );
        await RunAsync( "clipboard action worker resolves handlers in a bounded child", ClipboardActionWorkerResolvesInBoundedChildAsync );
        Run( "clipboard desktop Exec expands one literal target strictly", ClipboardDesktopExecExpandsStrictly );
        Run( "clipboard open handlers resolve exact XDG associations", ClipboardOpenHandlersResolveExactAssociations );
        await RunAsync( "clipboard rich browser filters kinds and presents inert metadata", ClipboardRichBrowserFiltersKindsAndPresentsInertMetadataAsync );
        await RunAsync( "clipboard rich metadata survives maximum URI JSON escaping", ClipboardRichMetadataEscapingSurvivesReopenAsync );
        await RunAsync( "clipboard rich image captures preserve half-quota pin headroom", ClipboardRichImagePinBudgetAsync );
        await RunAsync( "clipboard rich Clear retains projection charges and rejects late publication", ClipboardRichClearDuringProjectionAsync );
        await RunAsync( "clipboard rich admission publishes atomic selections and preserves recopy identity", ClipboardRichAdmissionPublishesAtomicSelectionsAsync );
        await RunAsync( "clipboard rich admission persists inert projections and lexical references", ClipboardRichAdmissionPersistsInertMetadataAsync );
        await RunAsync( "clipboard rich admission rejects revoked and conflicting selections", ClipboardRichAdmissionRejectsRevocationAndAliasConflictAsync );
        await RunAsync( "clipboard stream classification avoids full-selection RAM buffers", ClipboardStreamClassificationBoundsMemoryAsync );
        await RunAsync( "clipboard stream classification rejects damaged aliases and cancellation", ClipboardStreamClassificationRejectsDamageAndCancellationAsync );
        Run( "clipboard projection upgrade preserves v2 identities payloads and policy", ClipboardProjectionUpgradePreservesV2History );
        Run( "clipboard projection corruption is rejected before startup clear", ClipboardProjectionCorruptionPrecedesStartupClear );
        Run( "clipboard classification preserves precedence and unambiguous destinations", ClipboardClassificationPreservesPrecedenceAndTargets );
        Run( "clipboard classification rejects aliases moves and invalid primaries before fallback", ClipboardClassificationRejectsConflictsBeforeFallback );
        Run( "clipboard classification separates inert projection and reported source", ClipboardClassificationKeepsProjectionAndSourceSeparate );
        await RunAsync( "clipboard projections verify held payloads before isolated decoding", ClipboardProjectionUsesVerifiedHeldPayloadsAsync );
        await RunAsync( "clipboard projections share one HTML and image worker", ClipboardProjectionSharesWorkerCapacityAsync );
        await RunAsync( "clipboard worker runner reaps on external cancellation", ClipboardWorkerRunnerReapsOnCancellationAsync );
        await RunAsync( "process runner deadline covers blocked stdin and reaps the child", ProcessRunnerDeadlineCoversBlockedInputAsync );
        await RunAsync( "process runner cancellation reaps a child blocked on stdin", ProcessRunnerCancellationCleansBlockedInputAsync );
        Run( "clipboard HTML projects inert text without attributes or hidden subtrees", ClipboardHtmlProjectsInertText );
        Run( "clipboard HTML bounds projection and rejects encoding and structure", ClipboardHtmlBoundsAndRejectsInvalidInputs );
        await RunAsync( "clipboard HTML worker holds isolated input and capacity until reaped", ClipboardHtmlWorkerBoundsAndCancellationAsync );
        Run( "clipboard browser reports live capture status with policy precedence", ClipboardBrowserReportsCaptureStatus );
        await RunAsync( "clipboard navigation is query-free and survives bookmarked catalog rebuilds", ClipboardNavigationSurvivesBookmarksAsync );
        await RunAsync( "clipboard native capture handles inventory receive storage and watcher lifecycle", ClipboardNativeInventoryAsync );
        await RunAsync( "clipboard native control stays live with one worker", ClipboardNativeControlWithOneWorkerAsync );
        await RunAsync( "clipboard native admission reserves before receiving and preserves aliases", ClipboardNativeAdmissionPreservesAliasesAsync );
        await RunAsync( "clipboard native admission rejects invalid payloads and revoked epochs", ClipboardNativeAdmissionRejectsInvalidAsync );
        await RunAsync( "clipboard transport cancels under data backpressure", ClipboardTransportCancelsUnderDataBackpressureAsync );
        await RunAsync( "clipboard transport kills an unresponsive helper", ClipboardTransportKillsAnUnresponsiveHelperAsync );
        await RunAsync( "clipboard transport dies with its parent", ClipboardTransportDiesWithParentAsync );
        await RunAsync( "clipboard copy binds entry owner and generation", ClipboardCopyBindsEntryOwnerAndGenerationAsync );
        await RunAsync( "clipboard copy rejects payload damage after browser open", ClipboardCopyRejectsPayloadDamageAfterBrowserOpenAsync );
        await RunAsync( "clipboard sequence restart copies later entries in order", ClipboardSequenceRestartCopiesLaterEntriesInOrderAsync );
        await RunAsync( "clipboard sequence starts at browser and search entries", ClipboardSequenceStartsAtBrowserAndSearchEntriesAsync );
        await RunAsync( "clipboard sequence commands are private rows", ClipboardSequenceCommandsArePrivateRowsAsync );
        await RunAsync( "clipboard copy process cancels and reaps without changing the real clipboard", ClipboardCopyProcessCancelsAndReapsAsync );
        await RunAsync( "clipboard copy cancellation retains busy slot until completion", ClipboardCopyCancellationRetainsBusySlotUntilCompletionAsync );
        Run( "clipboard digests preserve names bytes and order independence", ClipboardDigestsPreserveNamesBytesAndOrderIndependence );
        Run( "clipboard digests reject unverified lengths and ownership markers", ClipboardDigestsRejectUnverifiedLengthsAndMarkers );
        Run( "clipboard payload store preserves raw bytes and revokes deleted entries", ClipboardPayloadStorePreservesBytesAndRevokesDeletedEntries );
        Run( "clipboard deleted text leaves no remnant in the database file", ClipboardDeletedTextLeavesNoDatabaseRemnant );
        Run( "clipboard schema 4 drops the legacy full-text index", ClipboardSearchIndexMigrationDropsTheLegacyIndex );
        await RunAsync( "clipboard migration recovers killed processes without losing source history", ClipboardMigrationRecoversKilledProcessAsync );
        Run( "clipboard payload migration low space preserves source", ClipboardPayloadMigrationLowSpacePreservesSource );
        Run( "clipboard payload migration retries damaged staged progress", ClipboardPayloadMigrationRetriesDamagedStagedProgress );
        Run( "clipboard payload store recovers missing and damaged files", ClipboardPayloadStoreRecoversMissingAndDamagedFiles );
        await RunAsync( "clipboard payload recovery charges conflicting staging files", ClipboardPayloadRecoveryChargesConflictingStagingFilesAsync );
        await RunAsync( "clipboard payload recovery charges orphans and revokes damaged live files", ClipboardPayloadRecoveryChargesOrphansAndRevokesDamagedLiveAsync );
        await RunAsync( "clipboard payload recovery fails without partial entry removal", ClipboardPayloadRecoveryFailsWithoutPartialRemovalAsync );
        await RunAsync( "clipboard payload ledger recovers killed writers without replay", ClipboardPayloadLedgerRecoversKilledWriterAsync );
        await RunAsync( "clipboard payload ledger publishes entry and charges atomically", ClipboardPayloadLedgerPublishesAtomicallyAsync );
        await RunAsync( "clipboard payload ledger bounds pending work and retains cleanup charges", ClipboardPayloadLedgerBoundsAndRetainsCleanupChargesAsync );
        Run( "clipboard payload directory publishes and honors I/O leases", ClipboardPayloadDirectoryPublishesAndHonorsLeases );
        Run( "clipboard payload directory retains authority across path replacement", ClipboardPayloadDirectoryRetainsAuthorityAcrossPathReplacement );
        await RunAsync( "clipboard payload directory rejects suspicious nodes", ClipboardPayloadDirectoryRejectsSuspiciousNodesAsync );
        Run( "clipboard current schema rejects corruption before startup deletion", ClipboardCurrentSchemaRejectsCorruptionBeforeStartupDeletion );
        Run( "clipboard capture rejects invalid Unicode without replacement", ClipboardCaptureRejectsInvalidUnicodeWithoutReplacement );
        Run( "clipboard file URIs preserve local lexical references", ClipboardFileUrisPreserveLocalLexicalReferences );
        Run( "clipboard URI lists preserve ordered unique destinations", ClipboardUriListsPreserveOrderedUniqueDestinations );
        Run( "clipboard URI lists reject incomplete and mixed representations", ClipboardUriListsRejectIncompleteAndMixedRepresentations );
        Run( "clipboard GNOME pairing and browser source are strict", ClipboardGnomePairingAndBrowserSourceAreStrict );
        await RunAsync( "clipboard search telemetry rejects private and unclassified requests", ClipboardSearchTelemetryRejectsPrivateAndUnclassifiedAsync );
        Run( "clipboard inventory planner bounds and orders supported aliases", ClipboardOfferPlannerBoundsAndOrdersInventory );
        Run( "clipboard inventory planner rejects hints and full-allowlist alias overflow", ClipboardOfferPlannerRejectsHintsAndAliasOverflow );
        await RunAsync( "clipboard GNOME header stops before body and rejects moves", ClipboardGnomeHeaderStopsBeforeBodyAsync );
        Run( "clipboard Wayland metadata preserves ABI and pinned XML", ClipboardWaylandMetadataPreservesAbiAndPinnedXml );
        await RunAsync( "clipboard helper header matches golden bytes and rejects lengths before reading", ClipboardHelperHeaderIsBoundedAndStableAsync );
        Run( "clipboard helper inventory requires complete sequential bounded names", ClipboardHelperInventoryRequiresCompleteSequentialNames );
        await RunAsync( "clipboard helper streams transport-only selections with admitted bounds", ClipboardHelperStreamsWithoutWholeSelectionBuffersAsync );
        await RunAsync( "clipboard helper queues preserve independent control backpressure", ClipboardHelperQueuesBackpressureIndependentlyAsync );
        Run( "clipboard browser bounds encoded summary metadata", ClipboardBrowserBoundsEncodedSummaryMetadata );
        Run( "clipboard settings previews are atomic and revision-bound", ClipboardSettingsPreviewsAreAtomicAndRevisionBound );
        Run( "clipboard settings tokens are owner-bound expiring and one-use", ClipboardSettingsPreviewTokensAreOwnerBoundAndOneUse );
        Run( "clipboard startup clear waits for migration review and runs once", ClipboardStartupClearWaitsForMigrationReviewAndRunsOnce );
        Run( "clipboard URL preserves exact literals and lexical keys", ClipboardUrlPreservesLiteralsAndLexicalKeys );
        Run( "clipboard URL rejects repair controls and ambiguous authorities", ClipboardUrlRejectsRepairControlsAndAmbiguousAuthorities );
        await RunAsync( "clipboard protocol binds live connections and rejects stale authority", ClipboardProtocolBindsLiveConnectionsAsync );
        Run( "clipboard protocol rejects malformed authority and bounded fields", ClipboardProtocolRejectsMalformedAuthority );
        Run( "clipboard browser snapshots survive recopy and skip tombstones", ClipboardBrowserSnapshotsSurviveRecopyAndSkipTombstones );
        Run( "clipboard browser rejects stale and cross-owner actions", ClipboardBrowserRejectsStaleAndCrossOwnerActions );
        Run( "clipboard browser bounds owners generations and lifetime", ClipboardBrowserBoundsOwnersGenerationsAndLifetime );
        Run( "clipboard preview preserves source line endings and Unicode element ranges", ClipboardPreviewPreservesSourceAndUnicode );
        Run( "clipboard preview bounds escaped text match counts and selected ranges", ClipboardPreviewBoundsEscapedSlicesAndCounts );
        Run( "clipboard preview refuses indivisible oversized elements and merges overlaps", ClipboardPreviewRejectsIndivisibleOversizeText );
        Run( "clipboard migration preserves legacy pins content and expired entries until review", ClipboardMigrationPreservesLegacyHistory );
        Run( "clipboard migration rejects excessive unknown and truncated stores", ClipboardMigrationRejectsUnknownAndExcessiveData );
        Run( "clipboard policy persists consent pause and initial configuration defaults", ClipboardPolicyPauseAndConsentPersist );
        await RunAsync( "clipboard mixed aliased and typed queries stay out of persistent history", ClipboardQueriesNeverPersistAsync );
        await RunAsync( "clipboard legacy saved searches are redacted quarantined and omitted from export", ClipboardSavedSearchesAreQuarantinedAsync );
        await RunAsync( "clipboard noninteractive search never starts its provider", ClipboardNoninteractiveSearchIsPrivateAsync );
        await RunAsync( "clipboard runtime denies assistant access to interactive cache", ClipboardRuntimeDeniesAssistantCacheAsync );
        await RunAsync( "clipboard AI attachments are rejected while explicit text remains allowed", ClipboardAiAttachmentsAreRejectedAsync );
        Run( "clipboard handoff recovers capacity without replay", ClipboardHandoffRecovery );
        Run( "clipboard handoff retains capacity until worker reaping", ClipboardHandoffRequiresReaping );
        Run( "clipboard handoff rejects corruption without resetting slots", ClipboardHandoffRejectsCorruption );
        await RunAsync( "folder browsing pages and retains client-owned file selections", FolderBrowsingAndSelectionAsync );
        await RunAsync( "clipboard fallback rejects unverifiable capture before starting a payload read", ClipboardFallbackRejectsUnverifiableCaptureAsync );
        await RunAsync( "clipboard history requires opt-in and enforces retention", ClipboardHistoryIsOptInAndBoundedAsync );
        await RunAsync( "script commands keep inputs literal and bound execution", ScriptCommandsRespectInputsAndBoundsAsync );
        await RunAsync( "snippets bind named inputs and expand placeholders once", SnippetInputsAndExpansionAsync );
        await RunAsync( "snippet clipboard reads enforce a 64 KiB capture budget", SnippetClipboardReadIsBoundedAsync );
        Run( "snippet syntax keeps malformed placeholders literal", SnippetSyntaxKeepsMalformedPlaceholdersLiteral );
        await RunAsync( "snippet placeholder errors reject configuration with a diagnostic", SnippetPlaceholderErrorsRejectConfigurationAsync );
        Run( "snippet modifiers transform each value in order", SnippetModifiersTransformEachValueInOrder );
        Run( "snippet dates use the clock formats locales and offsets", SnippetDatesUseTheClockFormatsLocalesAndOffsets );
        Run( "snippet uuids are fresh version 4 values", SnippetUuidsAreFreshVersionFourValues );
        await RunAsync( "snippet input defaults reach preview and copy", SnippetInputDefaultsReachPreviewAndCopyAsync );
        await RunAsync( "snippet clipboard history offsets are private and need consent", SnippetClipboardHistoryOffsetsArePrivateAndNeedConsentAsync );
        await RunAsync( "project checkouts have distinct paths and usable actions", ProjectCheckoutsAsync );
        await RunAsync( "omarchy menu state marks rows from one bounded batch", OmarchyMenuStateMarksRowsFromOneBatchAsync );
        Run( "omarchy menu state isolates each condition in the batch", OmarchyMenuStateScriptIsolatesEachCondition );
        await RunAsync( "omarchy menu indexes submenu rows that open in the launcher", OmarchyMenuIndexesSubmenuRowsAsync );
        await RunAsync( "omarchy menu browse lists one submenu in Omarchy order with catalog identities", OmarchyMenuBrowseListsChildrenInOmarchyOrderAsync );
        await RunAsync( "omarchy menu browse marks live state without the typed text", OmarchyMenuBrowseMarksLiveStateAsync );
        Run( "omarchy menu scope is validated at the protocol boundary", OmarchyMenuScopeIsValidated );
        await RunAsync( "omarchy menu browse action is launcher owned", OmarchyMenuBrowseActionIsLauncherOwnedAsync );
        await RunAsync( "omarchy menu source reparses only changed files", OmarchyMenuSourceReparsesChangedFilesAsync );
        await RunAsync(
            "systemd provider discovers services and timers without reading logs",
            SystemdProviderDiscoversUnitsAsync );
        await RunAsync( "systemd controls require confirmation and revalidate exact user units", SystemdControlsAsync );
        await RunAsync( "systemd editing uses the preferred editor and rejects missing unit files", SystemdEditingAsync );
        await RunAsync(
            "systemd provider stays silent on failure or malformed output",
            SystemdProviderStaysSilentOnFailureAsync );
        await RunAsync(
            "calculator delegates units to qalc and keeps arithmetic local",
            CalculatorProviderDelegatesUnitsToQalcAndKeepsArithmeticLocalAsync );
        await RunAsync(
            "calculator normalizes bare temperature shorthand to qalc canonical units",
            CalculatorProviderNormalizesTemperatureShorthandAsync );
        await RunAsync(
            "calculator falls back when qalc is absent, errors, or disabled",
            CalculatorProviderFallsBackWhenQalcErrorsAndHonorsNeverAsync );
        await RunAsync(
            "calculator refreshes currency rates in the background at most once per 15 minutes",
            CalculatorProviderRefreshesCurrencyRatesAtMostOncePerCooldownAsync );
        await RunAsync(
            "live qalc evaluates units when installed",
            CalculatorLiveQalcSmokeAsync );
        Run( "calculator configuration rejects unknown qalc modes",
            CalculatorConfigurationRejectsUnknownQalcModes );
        await RunAsync(
            "timezone provider returns the current time through the tz filter",
            TimezoneProviderReturnsCurrentTimeThroughFilterAsync );
        await RunAsync(
            "timezone provider converts a wall-clock time across zones",
            TimezoneProviderConvertsAcrossZonesAsync );
        await RunAsync(
            "timezone provider honors daylight saving on both sides of the boundary",
            TimezoneProviderHonorsDaylightSavingAsync );
        await RunAsync(
            "timezone provider ignores unknown cities and stays off bare Home",
            TimezoneProviderIgnoresUnknownAndStaysOffHomeAsync );
        Run( "web-search configuration rejects malformed, reserved, and duplicate engines",
            WebSearchConfigurationRejectsInvalidEngines );
        await RunAsync(
            "web-search emits one result per global engine for a bare query",
            WebSearchProviderEmitsGlobalEnginesForBareQueryAsync );
        await RunAsync(
            "web-search routes an alias prefix to a single engine with encoding",
            WebSearchProviderRoutesAliasToSingleEngineAsync );
        await RunAsync(
            "web-search stays off bare Home and wires its base filter",
            WebSearchProviderStaysOffHomeAndWiresFilterAsync );
        Run( "typed URL detection accepts web addresses and rejects files versions emails and paths",
            TypedUrlDetectionAcceptsAddressesAndRejectsLookalikes );
        await RunAsync(
            "typed URL row opens and copies the address from the web-search source",
            TypedUrlRowOpensAndCopiesTheAddressAsync );
        await RunAsync(
            "typed URL ranks first in Top Matches but never above an exact alias",
            TypedUrlRanksFirstButNeverAboveAnExactAliasAsync );
        await RunAsync(
            "typed URL follows the web-search source switch and explicit filters",
            TypedUrlFollowsTheWebSourceAndExplicitFiltersAsync );
        await RunAsync(
            "typed URL invocation records usage under a fixed id without the address",
            TypedUrlInvocationStoresNoAddressAsync );
        await RunAsync(
            "emoji provider matches by name and keyword and copies the glyph",
            EmojiProviderMatchesByNameAndKeywordAndCopiesGlyphAsync );
        await RunAsync(
            "desktop controls match plain search and hide commands whose tool is missing",
            DesktopControlsMatchPlainSearchAndHideMissingToolsAsync );
        await RunAsync(
            "desktop controls run literal argv and report tool failures",
            DesktopControlsRunLiteralArgvThroughRunArgvAsync );
        await RunAsync(
            "color picker starts hyprpicker after the launcher closes and reports a missing tool",
            ColorPickerStartsAfterTheLauncherAndReportsAMissingToolAsync );
        await RunAsync(
            "process list shows only the user's own live processes",
            ProcessListShowsOnlyTheUsersOwnLiveProcessesAsync );
        await RunAsync(
            "process signals require confirmation and recheck PID start time and owner",
            ProcessSignalsRequireConfirmationAndRecheckIdentityAsync );
        await RunAsync(
            "process terminate reaches only the verified child through a pidfd",
            ProcessTerminateReachesARealChildThroughAPidfdAsync );
    }
}
