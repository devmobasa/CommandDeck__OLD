using System.Text;

namespace CommandDeck;

public sealed class ActionInput
{
    public required string Name { get; init; }
    public string? DefaultValue { get; init; }
}

public static class ActionInputValues
{
    public static void Validate( IReadOnlyDictionary<string, string>? values )
    {
        if ( values is null )
        {
            return;
        }
        if ( values.Count > 16 || values.Any( static pair => pair.Key.Length is 0 or > 64 ||
                    pair.Value is null || pair.Value.Length > 8192 || pair.Value.Contains( '\0' ) ) ||
                    values.Sum( static pair => Encoding.UTF8.GetByteCount( pair.Value ) ) > 64 * 1024 )
        {
            throw new InvalidOperationException( "Action inputs exceed the allowed size (16 fields, 8192 characters each, 64 KiB total)." );
        }
    }

    public static CommandAction Bind( CommandAction action, IReadOnlyDictionary<string, string>? values )
    {
        ArgumentNullException.ThrowIfNull( action );
        Validate( values );
        if ( values is not null && values.Keys.Any( name => !action.Inputs.Any( input => input.Name == name ) ) )
        {
            throw new InvalidOperationException( "An input is not declared by the selected action." );
        }
        Dictionary<string, string> arguments = new( action.Arguments, StringComparer.Ordinal );
        foreach ( var input in action.Inputs )
        {
            var value = values?.GetValueOrDefault( input.Name ) ?? input.DefaultValue;
            if ( string.IsNullOrEmpty( value ) )
            {
                throw new InvalidOperationException( "Enter a value for " + input.Name + "." );
            }
            arguments["input:" + input.Name] = value;
        }
        Validate( action.Inputs.ToDictionary( static input => input.Name,
            input => arguments["input:" + input.Name], StringComparer.Ordinal ) );
        return new CommandAction
        {
            Id = action.Id,
            Title = action.Title,
            Kind = action.Kind,
            Arguments = arguments,
            Inputs = action.Inputs,
            Hidden = action.Hidden,
            RequiresConfirmation = action.RequiresConfirmation,
            CloseAfterAction = action.CloseAfterAction,
            IsDestructive = action.IsDestructive
        };
    }
}
