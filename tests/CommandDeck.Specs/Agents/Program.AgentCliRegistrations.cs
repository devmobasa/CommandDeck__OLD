namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunAgentCliRegistrationsAsync( )
    {
        await RunAsync(
            "agent CLI reports status probes the sandbox and refuses unconfirmed authority",
            AgentCliReportsStatusAndRefusesUnconfirmedAuthorityAsync );
        await RunAsync(
            "agent CLI status resolves the Codex binary and names the fix when it is missing",
            AgentCliStatusResolvesTheCodexBinaryAsync );
        await RunAsync(
            "agent CLI uses daemon-owned conversation state across invocations",
            AgentCliUsesDaemonStateAcrossInvocationsAsync );
    }
}
