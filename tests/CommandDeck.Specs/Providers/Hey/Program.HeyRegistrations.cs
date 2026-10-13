namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunHeyRegistrationsAsync( )
    {
        Run( "hey executable prefers a native binary over the mise wrapper", HeyExecutablePrefersNativeBinaries );
        await RunAsync( "hey client resolves the executable at most once every few seconds", HeyClientResolvesTheExecutableBrieflyAsync );
        await RunAsync( "hey client runs nothing without the executable", HeyClientRunsNothingWithoutExecutableAsync );
        await RunAsync( "hey client reads sign-in status with a fixed argv and a clean environment", HeyClientReadsStatusWithCleanEnvironmentAsync );
        await RunAsync( "hey client passes refinements as single arguments and text after --", HeyClientPassesSearchArgumentsSafelyAsync );
        await RunAsync( "hey client reads search matches into sanitized threads", HeyClientParsesSearchThreadsAsync );
        await RunAsync( "hey client lists only known boxes and skips bundle rows", HeyClientListsBoxesAsync );
        await RunAsync( "hey client maps envelopes, timeouts, and exits to typed failures", HeyClientReportsTypedFailuresAsync );
        Run( "hey client shows mail text inert and bounded", HeyClientCleansMailText );
        await RunAsync( "hey client runs the real process from the root with only forwarded variables", HeyClientRunsRealProcessFromRootAsync );
        Run( "private sources cover clipboard history and HEY mail", PrivateSourcesCoverClipboardAndMail );
        await RunAsync( "hey noninteractive search never starts its provider", HeyNoninteractiveSearchNeverRunsAsync );
        await RunAsync( "hey mail is refused as an AI attachment", HeyAiAttachmentsAreRejectedAsync );
        await RunAsync( "hey provider runs no process unless hey: is asked for and enabled", HeyProviderRunsOnlyWhenAskedAsync );
        await RunAsync( "hey provider keeps HEY's order in private uncustomizable rows", HeyProviderProjectsThreadsInHeyOrderAsync );
        await RunAsync( "hey provider sends refinements in HEY's own terms", HeyProviderSendsRefinementsAsync );
        await RunAsync( "hey provider explains every state with a next step", HeyProviderExplainsEveryStateAsync );
        await RunAsync( "hey provider caches only successful listings briefly", HeyProviderCachesOnlySuccessBrieflyAsync );
        await RunAsync( "hey provider pauses for typing before starting hey", HeyProviderPausesForTypingAsync );
        await RunAsync( "hey provider shares one pending read between identical searches", HeyProviderSharesPendingReadsAsync );
        await RunAsync( "hey provider starts nothing for a one-character search", HeyProviderNeedsTwoCharactersAsync );
        await RunAsync( "hey coordinator keeps HEY order past the local deadline and marks the query private", HeyCoordinatorKeepsHeyOrderAndPrivacyAsync );
        Run( "hey searches cannot be saved and mail: is the same private filter", HeySearchesCannotBeSaved );
        await RunAsync( "hey open launches the HEY web app and refuses forged links", HeyOpenLaunchesWebAppAndRefusesForgedLinksAsync );
        await RunAsync( "hey sign-in and install run in the presented terminal", HeySignInAndInstallUsePresentedTerminalAsync );
        await RunAsync( "hey source is searchable but never appears on Home", HeySourceNeverAppearsOnHomeAsync );
        await RunAsync( "hey open target persists and falls back to the web app", HeyOpenTargetPersistsAsync );
        await RunAsync( "hey terminal target reuses a running TUI or opens one on the thread", HeyTerminalTargetReusesRunningTuiAsync );
        await RunAsync( "hey empty query offers boxes and HEY pages without running hey", HeyEmptyQueryOffersBoxesAndPagesAsync );
        await RunAsync( "hey box listing filters locally and caches briefly without a typing pause", HeyBoxListingFiltersLocallyAsync );
        await RunAsync( "hey passing failures offer Search again with the same query", HeyFailuresOfferSearchAgainAsync );
        await RunAsync( "hey pages open on the web or in the HEY terminal app", HeyPagesOpenOnWebOrTuiAsync );
        await RunAsync( "hey fallback trails ranked results without taking their places", HeyFallbackTrailsRankedResultsAsync );
        await RunAsync( "hey fallback keeps the web fallback first in the runtime", HeyFallbackKeepsTheWebFallbackFirstAsync );
        await RunAsync( "hey client reads whole threads cleaned and bounded", HeyClientReadsWholeThreadsAsync );
        await RunAsync( "hey read thread shows the text in the output dialog", HeyReadThreadShowsTheTextAsync );
        await RunAsync( "hey triage actions follow the thread state with hidden undos", HeyTriageActionsFollowThreadStateAsync );
        await RunAsync( "hey triage runs one bounded change and maps failures", HeyTriageRunsOneBoundedChangeAsync );
        await RunAsync( "hey triage reports what changed and refuses invalid changes", HeyTriageReportsWhatChangedAsync );
        await RunAsync( "hey triage refreshes the listing it changed", HeyTriageRefreshesTheListingItChangedAsync );
        await RunAsync( "hey completes refinement values while they are typed", HeyCompletesRefinementValuesAsync );
        await RunAsync( "hey client reads the error envelope hey writes to stderr", HeyClientReadsErrorsFromStandardErrorAsync );
        await RunAsync( "hey client reports error output it cannot read as a failure, not a long answer", HeyClientReportsOverflowingErrorsAsync );
    }
}
