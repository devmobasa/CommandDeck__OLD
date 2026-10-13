namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunPackageCatalogRegistrationsAsync( )
    {
        await RunAsync(
            "pacman catalog parses search output into bounded records",
            PacmanCatalogParsesSearchOutputAsync );
        await RunAsync(
            "pacman catalog clamps the search limit and reports truncation",
            PacmanCatalogBoundsSearchResultsAsync );
        await RunAsync(
            "pacman catalog rejects option-like and unsupported package queries",
            PacmanCatalogRejectsUnsafeQueriesAsync );
        await RunAsync(
            "pacman catalog reports an empty search when pacman finds no matches",
            PacmanCatalogReportsNoSearchMatchesAsync );
        await RunAsync(
            "pacman catalog leaves package sizes empty when expac is unavailable",
            PacmanCatalogSurvivesMissingExpacAsync );
        Run(
            "pacman catalog database identity tracks sync database changes",
            PacmanCatalogDatabaseIdentityTracksChanges );
        await RunAsync(
            "pacman catalog previews an install with sizes warnings and a stable digest",
            PacmanCatalogPreviewsAnInstallAsync );
        await RunAsync(
            "pacman catalog refuses an install whose targets are missing or installed",
            PacmanCatalogRefusesUnknownInstallTargetsAsync );
        await RunAsync(
            "pacman catalog previews a remove cascade with the nosave warning",
            PacmanCatalogPreviewsARemoveCascadeAsync );
        await RunAsync(
            "pacman catalog reports dependency failures for a blocked remove",
            PacmanCatalogReportsBlockedRemovalsAsync );
        await RunAsync(
            "pacman catalog rejects unsafe package name sets before running commands",
            PacmanCatalogRejectsUnsafePreviewRequestsAsync );
        await RunAsync(
            "pacman catalog refuses to commit while the database lock is held",
            PacmanCatalogRefusesCommitWhileLockedAsync );
    }
}
