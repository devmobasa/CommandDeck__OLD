namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunPackageHelperRegistrationsAsync( )
    {
        await RunAsync(
            "package helper rejects unsupported operations before Polkit",
            HelperRejectsUnsupportedOperationsAsync );
        await RunAsync(
            "package helper rejects invalid and excessive package names before Polkit",
            HelperRejectsInvalidPackageNamesAsync );
        await RunAsync(
            "package helper rejects a changed package database",
            HelperRejectsAChangedDatabaseAsync );
        await RunAsync(
            "package helper rejects a changed transaction",
            HelperRejectsAChangedTransactionAsync );
        await RunAsync(
            "package helper revalidates database and transaction after Polkit",
            HelperRevalidatesAfterPolkitAsync );
        await RunAsync(
            "package helper revalidates queued transactions before Pacman",
            HelperRevalidatesQueuedTransactionAsync );
        await RunAsync(
            "package helper refuses to commit while the database is locked",
            HelperRefusesToCommitWhileTheDatabaseIsLockedAsync );
        await RunAsync(
            "package helper denies unauthorized package changes",
            HelperDeniesUnauthorizedPackageChangesAsync );
        await RunAsync(
            "package helper runs one package operation at a time",
            HelperRunsOnePackageOperationAtATimeAsync );
        await RunAsync(
            "package helper installs through literal argv and reports every state",
            HelperInstallsThroughLiteralArgvAsync );
        await RunAsync(
            "package helper removes through literal -Rns argv",
            HelperRemovesThroughLiteralArgvAsync );
        await RunAsync(
            "package helper fails when post-verification disagrees",
            HelperFailsWhenPostVerificationDisagreesAsync );
        await RunAsync(
            "package helper post-verification requires approved versions",
            HelperPostVerificationRequiresApprovedVersionsAsync );
        await RunAsync(
            "package helper cancels only queued operations",
            HelperCancelsOnlyQueuedOperationsAsync );
        await RunAsync(
            "package helper never runs an operation whose cancellation it accepted",
            HelperNeverRunsAnAcceptedCancellationAsync );
        await RunAsync(
            "package helper fails a removal when post-verification is inconclusive",
            HelperFailsRemovalWhenVerificationIsInconclusiveAsync );
        Run(
            "package database identity matches the shared definition",
            PackageDatabaseIdentityMatchesTheSharedDefinition );
        Run(
            "pacman transaction digest is stable and operation bound",
            PacmanTransactionDigestIsStable );
        Run(
            "package helper client maps missing helper failures",
            PackageHelperClientMapsMissingHelperFailures );
    }
}
