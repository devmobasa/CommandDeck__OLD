namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunCliAgentRegistrationsAsync( )
    {
        Run(
            "CLI agent profiles build closed argv and parse sign-in and model output",
            CliAgentProfilesBuildClosedArgvAndParseOutputs );
        Run(
            "Claude models expose reasoning efforts and forward CLI arguments",
            ClaudeModelsExposeEffortsAndForwardCliArguments );
        Run(
            "CLI agent sandbox binds only the owned home and the install tree",
            CliAgentSandboxBindsOnlyTheOwnedHomeAndInstallTree );
        Run(
            "CLI agent stream reader maps Messages and Cursor frames without duplicates",
            CliAgentStreamReaderMapsMessagesAndCursorFrames );
        await RunAsync(
            "CLI agent backend owns sign-in models and sandboxed turns",
            CliAgentBackendOwnsSignInModelsAndSandboxedTurnsAsync );
        await RunAsync(
            "CLI agent revocation preserves a newer turn's scratch and prompt file",
            CliAgentRevocationPreservesNewerTurnScratchAsync );
        await RunAsync(
            "Grok device-code sign-in harvests the URL and code and completes on exit",
            CliAgentGrokDeviceCodeLoginHarvestsUrlAndCodeAsync );
        await RunAsync(
            "CLI agent backend signs in and answers through the daemon socket",
            CliAgentBackendSignsInAndAnswersThroughTheDaemonAsync );
    }
}
