namespace CommandDeck;

public static partial class Program
{
    private static bool IsPreferencesResetCommand( CliArguments arguments )
    {
        var values = arguments.PositionalsAfterCommand( );
        return values.Count > 0 && values[0] == "reset";
    }

    private static async Task<int> RunPreferencesResetCliAsync( CliArguments arguments, CancellationToken cancellationToken )
    {
        if ( !arguments.HasFlag( "--yes" ) )
        {
            Console.Error.WriteLine( "preferences reset requires --yes." );
            return 2;
        }

        await using var client = await ConnectPreferenceDaemonAsync( arguments, cancellationToken ).ConfigureAwait( false );
        if ( client is null )
        {
            return await RunWithRuntimeAsync( arguments, cancellationToken ).ConfigureAwait( false );
        }

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 1,
            Operation = ProtocolOperations.CustomizationsReset,
            Confirmed = true
        }, cancellationToken ).ConfigureAwait( false );
        await foreach ( var line in client.ReadRawMessagesAsync( cancellationToken ).ConfigureAwait( false ) )
        {
            var message = ProtocolJson.DeserializeMessage( line );
            if ( message?.Type == ProtocolMessageTypes.Error )
            {
                throw new InvalidOperationException( message.Error );
            }
            if ( message?.Type == ProtocolMessageTypes.Done )
            {
                Console.WriteLine( "Launcher preferences and customizations reset. Added folders removed; configuration folders re-enabled." );
                return 0;
            }
        }
        throw new InvalidOperationException( "The daemon did not complete customization reset." );
    }
}
