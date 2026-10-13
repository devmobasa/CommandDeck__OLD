namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunHerdrRegistrationsAsync( )
    {
        await RunAsync( "herdr project actions create and reuse spaces through cached folder and project results", HerdrProjectActionsUseCachedFoldersAsync );
        await RunAsync( "herdr project reuse matches exact directories and prefers the focused space", HerdrProjectReuseMatchesDirectoriesAsync );
        await RunAsync( "herdr project failures never retry creation or focus", HerdrProjectFailuresNeverRetryAsync );
        await RunAsync( "herdr projects reject incomplete inventories and mismatched creation acknowledgements", HerdrProjectRejectsIncompleteInventoryAsync );
        await RunAsync( "herdr projects reject overlapping opens and recover after cancellation", HerdrProjectRejectsOverlappingOpensAsync );
        await RunAsync( "herdr projects report partial failure when the terminal cannot be raised", HerdrProjectReportsWindowFailureAsync );
        await RunAsync( "herdr projects reject snapshots with missing or duplicate panes", HerdrProjectValidatesPaneCountsAsync );
        await RunAsync( "herdr project gate covers terminal activation across executors", HerdrProjectGateCoversActivationAsync );
        await RunAsync( "herdr mixed focus actions share a gate through activation and cleanup", HerdrMixedFocusActionsShareGateAsync );
        await RunAsync( "herdr attention and exact space scopes preserve the query algebra", HerdrAttentionAndScopeAsync );
        await RunAsync( "herdr space details bound the tab preview", HerdrSpacePreviewIsBoundedAsync );
        await RunAsync( "herdr tab edits bind cached targets and do not train focus history", HerdrTabEditsUseCachedTargetsAsync );
        await RunAsync( "herdr tab edits reject unconfirmed changes without retries", HerdrTabEditsRejectUnconfirmedChangesAsync );
        await RunAsync( "herdr client runs nothing without the executable", HerdrClientRunsNothingWithoutExecutableAsync );
        await RunAsync( "herdr client reads status with a fixed default-session argv", HerdrClientReadsStatusAsync );
        await RunAsync( "herdr client admits only a compatible running server", HerdrClientAdmitsOnlyCompatibleServersAsync );
        await RunAsync( "herdr client treats process and parse failures as typed outcomes", HerdrClientReportsTypedFailuresAsync );
        await RunAsync( "herdr client lists both inventories with bounded limits and a clean environment", HerdrClientListsWithBoundsAsync );
        await RunAsync( "herdr client focuses with a literal id and verifies the acknowledgement", HerdrClientFocusesAndVerifiesAsync );
        await RunAsync( "herdr client maps focus errors and never retries a timeout", HerdrClientMapsFocusErrorsAsync );
        await RunAsync( "herdr provider runs no process when gated out", HerdrProviderIsGatedAsync );
        await RunAsync( "herdr provider runs both lists concurrently for a text query", HerdrProviderListsConcurrentlyAsync );
        await RunAsync( "herdr provider projects spaces and tabs with parent context", HerdrProviderProjectsInventoryAsync );
        await RunAsync( "herdr provider filters by kind and matches tabs through their space", HerdrProviderFiltersAndRanksAsync );
        await RunAsync( "herdr provider browses in focused-first order and honors bounds", HerdrProviderBrowsesDeterministicallyAsync );
        await RunAsync( "herdr provider stays silent on partial or failed inventory", HerdrProviderStaysSilentOnFailureAsync );
        await RunAsync( "herdr coordinator gates the source and adds an opt-in Home section", HerdrCoordinatorGatesAndHomeAsync );
        await RunAsync( "herdr coordinator allows exclusions within explicit browsing", HerdrCoordinatorBrowsesWithExclusionsAsync );
        await RunAsync( "herdr coordinator preserves browse order despite usage history", HerdrCoordinatorPreservesBrowseOrderWithUsageAsync );
        await RunAsync( "herdr coordinator intersects text clauses across tab and parent labels", HerdrCoordinatorIntersectsTextClausesAsync );
        await RunAsync( "herdr coordinator respects clause fields and exclusions", HerdrCoordinatorIntersectsFieldClausesAsync );
        await RunAsync( "herdr coordinator keeps phrase clauses contiguous", HerdrCoordinatorPreservesPhraseClausesAsync );
        await RunAsync( "herdr coordinator normalizes phrase clauses without skipping words", HerdrCoordinatorNormalizesPhraseClausesAsync );
        await RunAsync( "herdr focus action validates the cached result and sends exact argv", HerdrActionExecutesFocusAsync );
        await RunAsync( "herdr focus action raises the foreground Herdr window", HerdrActionRaisesForegroundWindowAsync );
        await RunAsync( "herdr focus action rejects zero-exit Lua warnings and clears the title", HerdrActionRejectsDispatchWarningsAsync );
        await RunAsync( "herdr window activation uses the bounded raw title contract", HerdrWindowActivationUsesRawTitleContractAsync );
        await RunAsync( "herdr window activation fails closed on a malformed title response", HerdrWindowActivationRejectsMalformedTitleResponseAsync );
        await RunAsync( "herdr window activation rejects an oversized title response", HerdrWindowActivationRejectsOversizedTitleResponseAsync );
        await RunAsync( "herdr window activation restores the title after Hyprland failure", HerdrWindowActivationRestoresTitleAfterHyprlandFailureAsync );
        await RunAsync( "herdr window activation clears the title after marking times out", HerdrWindowActivationClearsAfterMarkTimeoutAsync );
        await RunAsync( "herdr window activation clears the title after marking is cancelled", HerdrWindowActivationClearsAfterMarkCancellationAsync );
        await RunAsync( "herdr window activation is inert outside Hyprland", HerdrWindowActivationIsInertOutsideHyprlandAsync );
        Run( "herdr focus risk is immediate only for a matching Herdr result", HerdrFocusRiskClassification );
        await RunAsync( "herdr source preference merges into stored preferences", HerdrPreferenceMergesAsync );
    }
}
