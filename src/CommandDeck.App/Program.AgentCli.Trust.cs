using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunAgentTrustAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        IReadOnlyList<string> positionals,
        CancellationToken cancellationToken )
    {
        var action = positionals.Count > 1 ? positionals[1].ToLowerInvariant( ) : AgentCliActions.Show;
        if ( action == AgentCliActions.Show )
        {
            var current = await runtime.GetAgentTrustAsync( cancellationToken )
                .ConfigureAwait( false );
            return WriteAgentTrust( current, arguments.HasFlag( CliOptionNames.Json ) );
        }

        if ( action != AgentCliActions.Set || positionals.Count < 3 ||
            !int.TryParse( positionals[2], out var level ) )
        {
            Console.Error.WriteLine( "Use 'agent trust show' or 'agent trust set LEVEL --yes'." );
            return 2;
        }

        if ( !arguments.HasFlag( CliOptionNames.Yes ) )
        {
            Console.Error.WriteLine(
                $"agent trust set {level} requires --yes; level " +
                $"{AgentTrustLevels.Unrestricted} also requires --phrase and --duration." );
            return 2;
        }

        var clientOwnerId = $"cli-{Guid.NewGuid( ):N}";
        var challenge = await runtime.CreateAgentTrustChallengeAsync(
            clientOwnerId, level, cancellationToken ).ConfigureAwait( false );
        Console.Error.WriteLine( Safe( challenge.Prompt ) );
        foreach ( var warning in challenge.Warnings )
        {
            Console.Error.WriteLine( $"Warning: {Safe( warning )}" );
        }

        var applied = await runtime.ApplyAgentTrustAsync(
            clientOwnerId,
            challenge.Id,
            level,
            arguments.GetOption( CliOptionNames.Phrase ),
            arguments.GetOption( CliOptionNames.Duration ),
            cancellationToken ).ConfigureAwait( false );
        return WriteAgentTrust( applied, arguments.HasFlag( CliOptionNames.Json ) );
    }

    private static int WriteAgentTrust( AgentTrustState trust, bool json )
    {
        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize(
                trust, CommandDeckJsonContext.Default.AgentTrustState ) );
            return 0;
        }

        Console.WriteLine( $"Level:   {trust.Level} ({Safe( trust.LevelName )})" );
        Console.WriteLine( $"Default: {trust.DefaultLevel}" );
        Console.WriteLine( $"Lease:   {FormatAgentLease( trust )}" );
        if ( !string.IsNullOrWhiteSpace( trust.Explanation ) )
        {
            Console.WriteLine( $"Detail:  {Safe( trust.Explanation )}" );
        }

        return 0;
    }
}
