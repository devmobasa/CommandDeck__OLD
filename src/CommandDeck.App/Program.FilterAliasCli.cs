using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunFilterAliasesAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var positionals = arguments.PositionalsAfterCommand( );
        var action = positionals.Count == 0 ? "list" : positionals[0].ToLowerInvariant( );
        if ( action == "list" )
        {
            var aliases = await runtime.Preferences.GetFilterAliasesAsync(
                cancellationToken ).ConfigureAwait( false );
            if ( arguments.HasFlag( CliOptionNames.Json ) )
            {
                Console.WriteLine( JsonSerializer.Serialize(
                    aliases.ToList( ),
                    CommandDeckJsonContext.Default.ListFilterAlias ) );
            }
            else if ( aliases.Count == 0 )
            {
                Console.WriteLine( "No user filter aliases." );
            }
            else
            {
                foreach ( var alias in aliases )
                {
                    Console.WriteLine(
                        $"{PreferencesTextFormatter.EscapeTerminalText( alias.Alias )}: -> " +
                        alias.FilterId );
                }
            }
            return 0;
        }

        if ( action != "alias" || positionals.Count < 3 )
        {
            return FilterAliasUsageError( );
        }

        var mutation = positionals[1].ToLowerInvariant( );
        var token = positionals[2];
        var filterId = mutation switch
        {
            "set" when positionals.Count == 4 => positionals[3],
            "remove" when positionals.Count == 3 => null,
            _ => throw new InvalidOperationException(
                "Use 'filters alias set TOKEN FILTER_ID' or 'filters alias remove TOKEN'." )
        };
        var result = await runtime.SetFilterAliasAsync(
            token,
            filterId,
            cancellationToken ).ConfigureAwait( false );
        Console.WriteLine( result.Message );
        return 0;
    }

    private static int FilterAliasUsageError( )
    {
        Console.Error.WriteLine(
            "Use 'filters list', 'filters alias set TOKEN FILTER_ID', " +
            "or 'filters alias remove TOKEN'." );
        return 2;
    }
}
