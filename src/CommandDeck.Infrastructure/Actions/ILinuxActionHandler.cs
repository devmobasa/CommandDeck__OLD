namespace CommandDeck;

// One feature's actions. Each handler declares the action kinds it owns, and
// LinuxActionExecutor routes every kind to the one handler that declares it.
internal interface ILinuxActionHandler
{
    IReadOnlyList<string> Kinds
    {
        get;
    }

    ValueTask<ActionExecutionResult> ExecuteAsync(
        SearchResult result,
        CommandAction action,
        string query,
        CancellationToken cancellationToken );
}

internal static class LinuxActionArguments
{
    public static string Require( CommandAction action, string name )
    {
        if ( !action.Arguments.TryGetValue( name, out var value ) ||
            string.IsNullOrWhiteSpace( value ) )
        {
            throw new InvalidOperationException(
                $"Action '{action.Id}' is missing argument '{name}'." );
        }

        return value;
    }

    // A handler only receives the kinds it declares; anything else fails closed.
    public static ActionExecutionResult UnknownKind( CommandAction action ) =>
        ActionExecutionResult.Failure( $"Unknown action kind '{action.Kind}'." );
}
