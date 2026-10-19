namespace CommandDeck;

// A snippet that reads clipboard history carries a marker so its row is treated as
// private clipboard data: never on Home, in suggestions, or given to the assistant.
public sealed record SnippetActionPayload( string Template, bool ReadsClipboardHistory = false )
{
    private const string TemplateKey = "template";
    private const string ClipboardHistoryKey = "clipboardHistory";

    public Dictionary<string, string> ToArguments( )
    {
        Dictionary<string, string> arguments = new( StringComparer.Ordinal ) { [TemplateKey] = Template };
        if ( ReadsClipboardHistory )
        {
            arguments[ClipboardHistoryKey] = "true";
        }

        return arguments;
    }

    public static SnippetActionPayload Read( IReadOnlyDictionary<string, string> arguments )
    {
        var template = arguments.GetValueOrDefault( TemplateKey );
        if ( string.IsNullOrWhiteSpace( template ) )
        {
            throw new InvalidOperationException( "Action requires 'template'." );
        }

        return new SnippetActionPayload( template, arguments.GetValueOrDefault( ClipboardHistoryKey ) == "true" );
    }

    public static bool IsHistoryReader( CommandAction action ) =>
        action.Kind is ActionKinds.SnippetCopy or ActionKinds.SnippetPreview &&
        action.Arguments.GetValueOrDefault( ClipboardHistoryKey ) == "true";
}
