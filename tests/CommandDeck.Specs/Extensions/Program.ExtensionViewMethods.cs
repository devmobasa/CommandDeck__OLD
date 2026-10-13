using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionViewsRoundTrip( )
    {
        ExtensionView[] views =
        [
            new ListView
            {
                Id = "uuids", Title = "UUIDs", Items = [new ListItem
                {
                    Id = "uuid-one", Title = "Identifier", Actions = [ViewAction.CopyText( "copy", "Copy identifier", "identifier" )]
                }]
            },
            new DetailView { Id = "details", Title = "Details", Body = "Plain text with Unicode: λ" },
            new FormView
            {
                Id = "task", Title = "New task", Fields = [FormField.Text( "title", "Title", required: true, maxLength: 200 ),
                    FormField.Choice( "priority", "Priority", [new Choice( "normal", "Normal" )] ), FormField.Checkbox( "done", "Done" )],
                Submit = ViewAction.Callback( "save", "Save" )
            }
        ];
        string[] golden =
        [
            """{"kind":"list","search":{"mode":"local","placeholder":"Search\u2026"},"items":[{"id":"uuid-one","title":"Identifier","actions":[{"id":"copy","kind":"copy-text","title":"Copy identifier","text":"identifier"}]}],"emptyMessage":"No results","id":"uuids","title":"UUIDs","actions":[],"loading":false}""",
            """{"kind":"detail","body":"Plain text with Unicode: \u03BB","metadata":[],"id":"details","title":"Details","actions":[],"loading":false}""",
            """{"kind":"form","fields":[{"id":"title","label":"Title","kind":"text","required":true,"maxLength":200,"choices":[]},{"id":"priority","label":"Priority","kind":"choice","required":false,"maxLength":8192,"choices":[{"value":"normal","label":"Normal"}]},{"id":"done","label":"Done","kind":"checkbox","required":false,"maxLength":8192,"choices":[]}],"submit":{"id":"save","kind":"callback","title":"Save"},"fieldErrors":{},"validatedFormRevision":0,"id":"task","title":"New task","actions":[],"loading":false}"""
        ];
        foreach ( var (view, expected) in views.Zip( golden ) )
        {
            ViewValidation.Validate( view );
            var json = JsonSerializer.Serialize( view, ExtensionViewJsonContext.Default.ExtensionView );
            Equal( expected, json );
            var restored = JsonSerializer.Deserialize( json, ExtensionViewJsonContext.Default.ExtensionView )!;
            ViewValidation.Validate( restored );
            Equal( view.GetType( ), restored.GetType( ) );
            Equal( view.Id, restored.Id );
            Equal( json, JsonSerializer.Serialize( restored, ExtensionViewJsonContext.Default.ExtensionView ) );
        }
        // Preserve non-text maxLength from preview.5 without reopening setters in the author API.
        const string legacy = """{"kind":"form","fields":[{"id":"choice","label":"Choice","kind":"choice","required":false,"maxLength":3,"choices":[{"value":"one","label":"One"}],"defaultValue":"one"}],"submit":{"id":"save","kind":"callback","title":"Save"},"fieldErrors":{},"validatedFormRevision":0,"id":"legacy","title":"Legacy","actions":[],"loading":false}""";
        var legacyForm = ( FormView ) JsonSerializer.Deserialize( legacy, ExtensionViewJsonContext.Default.ExtensionView )!;
        ViewValidation.Validate( legacyForm );
        Equal( legacy, JsonSerializer.Serialize( ( ExtensionView ) legacyForm, ExtensionViewJsonContext.Default.ExtensionView ) );
        Equal( 3, legacyForm.Fields[0].MaxLength );
        Throws<InvalidDataException>( ( ) => ViewValidation.ValidateValue( legacyForm.Fields[0], JsonElement.Parse( "\"long\"" ) ) );

        var @event = new CommandEventRequest( 1, "invocation", "task", 2, new FormSubmitted( "save", 3,
            new Dictionary<string, JsonElement> { ["title"] = JsonElement.Parse( "\"draft\"" ) } ) );
        var roundTrip = JsonSerializer.Deserialize( JsonSerializer.Serialize( @event, ExtensionViewJsonContext.Default.CommandEventRequest ),
            ExtensionViewJsonContext.Default.CommandEventRequest )!;
        Equal( "draft", (( FormSubmitted ) roundTrip.Event).Values["title"].GetString( ) );
    }

    private static void ExtensionViewsRejectHostileContent( )
    {
        ListView valid = new( )
        {
            Id = "list",
            Title = "List",
            Items = [new ListItem { Id = "one", Title = "One" }]
        };
        Throws<InvalidDataException>( ( ) => ViewValidation.Validate( valid with { Items = [valid.Items[0], valid.Items[0]] } ) );
        Throws<InvalidDataException>( ( ) => ViewValidation.Validate( valid with { Title = new string( 'λ', 513 ) } ) );
        Throws<InvalidDataException>( ( ) => ViewValidation.Validate( valid with { Items = [valid.Items[0] with { Asset = "https://example.com/query" }] } ) );
        Throws<InvalidDataException>( ( ) => ViewValidation.Validate( valid with { Items = default } ) );
        Throws<InvalidDataException>( ( ) => ViewValidation.Validate( valid with { Items = [null!] } ) );
        Throws<JsonException>( ( ) => JsonSerializer.Deserialize( """{"kind":"list","id":"list","title":"List","actions":[{"id":"evil","kind":"run-argv","title":"Run"}]}""", ExtensionViewJsonContext.Default.ExtensionView ) );
        Throws<JsonException>( ( ) => JsonSerializer.Deserialize( """{"kind":"list","id":"list","title":"List","actions":[{"id":"copy","kind":"copy-text","title":"Copy","text":"value","url":"https://example.com"}]}""", ExtensionViewJsonContext.Default.ExtensionView ) );
        Throws<InvalidDataException>( ( ) => ViewValidation.Validate( valid with { Actions = [ViewAction.OpenUrl( "open", "Open", "file:///etc/passwd" )] } ) );
        Throws<InvalidDataException>( ( ) => ViewValidation.Validate( valid with { Actions = [ViewAction.OpenUrl( "open", "Open", "https://user:secret@example.com" )] } ) );
        Throws<JsonException>( ( ) => JsonSerializer.Deserialize( """{"kind":"form","id":"form","title":"Form","submit":{"id":"submit","kind":"copy-text","title":"Submit","text":"value"}}""", ExtensionViewJsonContext.Default.ExtensionView ) );
        Throws<InvalidDataException>( ( ) => ViewValidation.ValidateValue( FormField.Text( "name", "Name", required: true ), JsonElement.Parse( "\"\"" ) ) );
        Throws<InvalidDataException>( ( ) => ViewValidation.ValidateValue( FormField.Checkbox( "flag", "Flag" ), JsonElement.Parse( "\"true\"" ) ) );
        Throws<JsonException>( ( ) => JsonSerializer.Deserialize( "{\"kind\":\"qml\",\"id\":\"x\",\"title\":\"x\"}", ExtensionViewJsonContext.Default.ExtensionView ) );
        Throws<JsonException>( ( ) => JsonSerializer.Deserialize( "{\"kind\":\"detail\",\"id\":\"x\",\"title\":\"x\",\"script\":\"eval()\"}", ExtensionViewJsonContext.Default.ExtensionView ) );
    }
}
