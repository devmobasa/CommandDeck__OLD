namespace CommandDeck;

public sealed record SnippetActionPayload( string Template )
{
    public Dictionary<string, string> ToArguments( ) => new( StringComparer.Ordinal ) { ["template"] = Template };

    public static SnippetActionPayload Read( IReadOnlyDictionary<string, string> arguments )
    {
        var template = arguments.GetValueOrDefault( "template" );
        if ( string.IsNullOrWhiteSpace( template ) )
        {
            throw new InvalidOperationException( "Action requires 'template'." );
        }

        return new SnippetActionPayload( template );
    }
}
