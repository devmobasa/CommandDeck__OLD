namespace CommandDeck;

public sealed record UrlActionPayload( string UrlTemplate, string? Alias = null )
{
    public Dictionary<string, string> ToArguments( ) => new( StringComparer.Ordinal )
    {
        ["urlTemplate"] = UrlTemplate,
        ["alias"] = Alias ?? string.Empty
    };

    public static UrlActionPayload Read( IReadOnlyDictionary<string, string> arguments )
    {
        var template = arguments.GetValueOrDefault( "urlTemplate" );
        if ( string.IsNullOrWhiteSpace( template ) )
        {
            throw new InvalidOperationException( "Action requires 'urlTemplate'." );
        }

        return new UrlActionPayload( template, arguments.GetValueOrDefault( "alias" ) );
    }

    public string Expand( string query )
    {
        if ( !string.IsNullOrWhiteSpace( Alias ) && query.StartsWith( Alias + " ", StringComparison.OrdinalIgnoreCase ) )
        {
            query = query[(Alias.Length + 1)..];
        }

        return UrlTemplate.Replace( "{query}", Uri.EscapeDataString( query ), StringComparison.OrdinalIgnoreCase );
    }
}
