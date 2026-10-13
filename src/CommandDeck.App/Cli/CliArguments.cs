namespace CommandDeck;

public sealed class CliArguments
{
    private readonly string[] _arguments;

    public CliArguments( string[] arguments )
    {
        _arguments = arguments;
    }

    public string Command => _arguments.FirstOrDefault( static arg => !arg.StartsWith( '-' ) ) ?? "help";

    public bool HasFlag( string name ) =>
        _arguments.Any( value => string.Equals( value, name, StringComparison.Ordinal ) );

    public string? GetOption( string name )
    {
        for ( var index = 0; index < _arguments.Length - 1; index++ )
        {
            if ( string.Equals( _arguments[index], name, StringComparison.Ordinal ) )
            {
                return _arguments[index + 1];
            }
        }

        var prefix = name + "=";
        var inline = _arguments.FirstOrDefault( value =>
            value.StartsWith( prefix, StringComparison.Ordinal ) );
        return inline is null ? null : inline[prefix.Length..];
    }

    public IReadOnlyList<string> PositionalsAfterCommand( )
    {
        var commandIndex = Array.FindIndex(
            _arguments,
            value => !value.StartsWith( '-' ) );
        if ( commandIndex < 0 )
        {
            return [];
        }

        List<string> values = [];
        for ( var index = commandIndex + 1; index < _arguments.Length; index++ )
        {
            var value = _arguments[index];
            if ( value.StartsWith( '-' ) )
            {
                if ( !value.Contains( '=', StringComparison.Ordinal ) &&
                    index + 1 < _arguments.Length &&
                    !_arguments[index + 1].StartsWith( '-' ) )
                {
                    index++;
                }

                continue;
            }

            values.Add( value );
        }

        return values;
    }

    public string JoinPositionalsAfterCommand( ) =>
        string.Join( ' ', PositionalsAfterCommand( ) );
}
