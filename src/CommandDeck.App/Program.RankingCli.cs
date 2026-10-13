using System.Text;
using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunRankingAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count > 0 ? positionals[0].ToLowerInvariant( ) : RankingCliActions.Inspect;
        switch ( action )
        {
            case RankingCliActions.Inspect:
            case RankingCliActions.Export:
                var limit = action == RankingCliActions.Export
                    ? 256
                    : ParseInteger( arguments.GetOption( CliOptionNames.Limit ), 50, 1, 256 );
                var snapshot = await runtime.GetRankingLearningAsync(
                    limit,
                    cancellationToken ).ConfigureAwait( false );
                if ( action == RankingCliActions.Export || arguments.HasFlag( CliOptionNames.Json ) )
                {
                    var json = JsonSerializer.Serialize(
                        snapshot,
                        CommandDeckJsonContext.Default.RankingLearningSnapshot );
                    if ( Encoding.UTF8.GetByteCount( json ) > ProtocolLimits.MaximumCustomizationExportBytes )
                    {
                        throw new InvalidOperationException(
                            "Ranking export exceeds the 256 KiB UTF-8 output limit." );
                    }
                    Console.WriteLine( json );
                    return 0;
                }

                Console.WriteLine( $"Adaptive ranking: {(snapshot.Enabled ? "enabled" : "disabled")}" );
                Console.WriteLine( $"Retention: {snapshot.RetentionDays} days" );
                Console.WriteLine( $"Stored events: {snapshot.TotalEvents}" );
                foreach ( var rankingEvent in snapshot.Events )
                {
                    var query = rankingEvent.Query is null
                        ? "query not retained"
                        : $"query {PreferencesTextFormatter.EscapeTerminalText( rankingEvent.Query )}";
                    Console.WriteLine(
                        $"  {PreferencesTextFormatter.EscapeTerminalText( rankingEvent.StableId )} · " +
                        $"{PreferencesTextFormatter.EscapeTerminalText( rankingEvent.ActionId )} · {query}" );
                }
                return 0;

            case RankingCliActions.Reset:
                if ( !arguments.HasFlag( CliOptionNames.Yes ) )
                {
                    Console.Error.WriteLine( $"ranking {RankingCliActions.Reset} requires {CliOptionNames.Yes}." );
                    return 2;
                }
                await runtime.ResetRankingLearningAsync( cancellationToken ).ConfigureAwait( false );
                Console.WriteLine( "Adaptive ranking history reset." );
                return 0;

            default:
                Console.Error.WriteLine(
                    $"Use 'ranking {RankingCliActions.Inspect}', 'ranking {RankingCliActions.Export}', " +
                    $"or 'ranking {RankingCliActions.Reset} {CliOptionNames.Yes}'." );
                return 2;
        }
    }
}
