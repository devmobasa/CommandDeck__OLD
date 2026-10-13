namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunAgentLoginAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        var backendId = arguments.GetOption( CliOptionNames.Backend );
        var positionals = arguments.PositionalsAfterCommand( );
        if ( positionals.Count > 1 && string.Equals( positionals[1], "complete", StringComparison.OrdinalIgnoreCase ) )
        {
            var code = arguments.GetOption( CliOptionNames.Code );
            if ( string.IsNullOrWhiteSpace( code ) )
            {
                Console.Error.WriteLine( "agent login complete requires --code CODE." );
                return 2;
            }
            var completed = await runtime.CompleteAgentLoginAsync( code, cancellationToken, backendId ).ConfigureAwait( false );
            WriteAgentLogin( completed );
            return completed.State == AgentAuthenticationStates.Authenticated ? 0 : 1;
        }
        var method = arguments.GetOption( CliOptionNames.Method ) ?? DefaultLoginMethod( backendId );
        if ( method is not (AgentLoginMethods.DeviceCode or AgentLoginMethods.ApiKey or AgentLoginMethods.Browser) )
        {
            Console.Error.WriteLine( "agent login --method must be device-code, api-key, or browser." );
            return 2;
        }

        var kind = arguments.GetOption( CliOptionNames.CredentialKind );
        if ( kind is not null and not (AiCredentialKinds.Environment or AiCredentialKinds.Systemd) )
        {
            Console.Error.WriteLine(
                "agent login --credential-kind must be environment or systemd." );
            return 2;
        }

        var credential = kind is null
            ? null
            : new AiCredentialOptions { Kind = kind, Name = arguments.GetOption( CliOptionNames.CredentialName ) };
        var login = await runtime.StartAgentLoginAsync(
            method, credential, cancellationToken, backendId ).ConfigureAwait( false );
        WriteAgentLogin( login );
        if ( login.State != AgentAuthenticationStates.Pending )
        {
            return login.State == AgentAuthenticationStates.Authenticated ? 0 : 1;
        }
        if ( login.RequiresCode )
        {
            // Claude Code hands the browser a code to paste back; read it here so
            // the terminal flow completes without the window.
            Console.Error.Write( "Paste the code shown in the browser: " );
            var code = Console.ReadLine( )?.Trim( );
            if ( string.IsNullOrEmpty( code ) )
            {
                await runtime.CancelAgentLoginAsync( cancellationToken, backendId ).ConfigureAwait( false );
                Console.Error.WriteLine( "No code entered; sign-in cancelled." );
                return 1;
            }
            var completed = await runtime.CompleteAgentLoginAsync( code, cancellationToken, backendId ).ConfigureAwait( false );
            WriteAgentLogin( completed );
            if ( completed.State != AgentAuthenticationStates.Pending )
            {
                return completed.State == AgentAuthenticationStates.Authenticated ? 0 : 1;
            }
        }

        return await PollAgentLoginAsync( runtime, backendId, cancellationToken ).ConfigureAwait( false );
    }

    // Codex keeps its device-code default; the CLI backends declare their own flow.
    private static string DefaultLoginMethod( string? backendId ) => backendId switch
    {
        AgentBackendIds.ClaudeCode or AgentBackendIds.Cursor => AgentLoginMethods.Browser,
        _ => AgentLoginMethods.DeviceCode
    };

    private static void WriteAgentLogin( AgentLoginState login )
    {
        Console.WriteLine( $"State:  {Safe( login.State )}" );
        if ( login.UserCode is not null )
        {
            Console.WriteLine( $"Code:   {Safe( login.UserCode )}" );
        }
        if ( login.VerificationUrl is not null )
        {
            Console.WriteLine( $"Open:   {Safe( login.VerificationUrl )}" );
        }
        if ( login.Message is not null )
        {
            Console.WriteLine( $"Detail: {Safe( login.Message )}" );
        }
    }

    private static async Task<int> PollAgentLoginAsync(
        CommandDeckRuntime runtime,
        string? backendId,
        CancellationToken cancellationToken )
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes( 15 );
        var state = AgentAuthenticationStates.Pending;
        var displayName = AgentBackendIds.DisplayName( backendId ?? AgentBackendIds.Codex );
        try
        {
            while ( DateTimeOffset.UtcNow < deadline &&
                state == AgentAuthenticationStates.Pending )
            {
                await Task.Delay( TimeSpan.FromSeconds( 2 ), cancellationToken ).ConfigureAwait( false );
                var login = await runtime.GetAgentLoginStateAsync( backendId, cancellationToken )
                    .ConfigureAwait( false );
                state = login.State;
                Console.Error.Write( "." );
            }
        }
        catch ( OperationCanceledException )
        {
            Console.Error.WriteLine( );
            await runtime.CancelAgentLoginAsync( CancellationToken.None, backendId ).ConfigureAwait( false );
            throw;
        }

        Console.Error.WriteLine( );
        if ( state == AgentAuthenticationStates.Authenticated )
        {
            Console.WriteLine( $"Signed in to {displayName}." );
            return 0;
        }

        await runtime.CancelAgentLoginAsync( cancellationToken, backendId ).ConfigureAwait( false );
        Console.Error.WriteLine( $"{displayName} sign-in did not complete ({Safe( state )})." );
        return 1;
    }

    private static async Task<int> RunAgentLogoutAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        CancellationToken cancellationToken )
    {
        if ( !arguments.HasFlag( CliOptionNames.Yes ) )
        {
            Console.Error.WriteLine( "agent logout requires --yes." );
            return 2;
        }

        var login = await runtime.AgentLogoutAsync( cancellationToken, arguments.GetOption( CliOptionNames.Backend ) )
            .ConfigureAwait( false );
        Console.WriteLine( Safe( login.Message ?? "Signed out." ) );
        return 0;
    }
}
