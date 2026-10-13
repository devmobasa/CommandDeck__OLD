namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunAiSettingsRegistrationsAsync( )
    {
        Run(
            "AI provider settings contracts reserve backend IDs and validate management requests",
            AiSettingsContractsReserveBackendIdsAndValidateRequests );
        await RunAsync(
            "explicit backend prefixes route to assistant backends not direct providers",
            ExplicitBackendPrefixesRouteToAssistantBackendsAsync );
        await RunAsync(
            "AI provider settings store writes privately detects conflicts and fails closed",
            AiSettingsStoreWritesPrivatelyDetectsConflictsAndFailsClosedAsync );
        await RunAsync(
            "AI provider settings service resolves precedence shares capacity and retires registries",
            AiSettingsServiceResolvesPrecedenceAndRetiresRegistriesAsync );
        await RunAsync(
            "AI settings runtime applies live mutations invalidates preflights and persists across restart",
            AiSettingsRuntimeAppliesLiveMutationsAndPersistsAsync );
        await RunAsync(
            "AI settings runtime cancels affected generations and keeps unaffected streams",
            AiSettingsRuntimeCancelsAffectedGenerationsAsync );
        await RunAsync(
            "AI settings socket and CLI act through the daemon with categorised errors",
            AiSettingsSocketAndCliUseTheDaemonAsync );
    }
}
