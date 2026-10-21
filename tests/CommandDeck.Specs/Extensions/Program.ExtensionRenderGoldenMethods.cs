using System.Text.Json;
using System.Text.Json.Nodes;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionRenderedViewsMatchGolden( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-render-golden-" ).FullName;
        try
        {
            Directory.CreateDirectory( Path.Combine( root, "assets" ) );
            File.Copy( Path.Combine( FindRepositoryRoot( ), "samples/extensions/QuickTools/assets/uuid.png" ), Path.Combine( root, "assets/icon.png" ) );
            ExtensionAssets assets = new( root );
            ExtensionViewAuthority authority = new( "session", 1, 1, assets.Resolve );
            authority.BeginInvocation( InvocationOrigin.Open );
            List<string> actual = [];
            ListView list = new( )
            {
                Id = "list",
                Title = "List",
                PreviousPage = "previous-token",
                NextPage = "next-token",
                Actions = [ViewAction.OpenUrl( "open", "Open", "https://example.com" )],
                Items = [new ListItem { Id = "one", Title = "One", Subtitle = "Subtitle", Asset = "assets/icon.png",
                    Actions = [ViewAction.CopyText("copy", "Copy", "hidden payload"), ViewAction.Callback("callback", "Callback")] }]
            };
            Add( authority.Replace( new( "session", 1, list.Id, 0, 1, list ) ) );
            DetailView detail = new( ) { Id = "detail", Title = "Detail", Body = "Body", Asset = "assets/icon.png", Metadata = [new( "Label", "Value" )] };
            Add( authority.Push( new( "session", 1, detail.Id, 0, 1, detail ) ) );
            FormView form = new( )
            {
                Id = "form",
                Title = "Form",
                Fields = [FormField.Text("text", "Text", true, 100), FormField.Multiline("multi", "Multiline"),
                    FormField.Choice("choice", "Choice", [new("one", "One")]), FormField.Checkbox("check", "Checkbox")],
                Submit = ViewAction.Callback( "submit", "Submit" ),
                FieldErrors = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty.Add( "text", "Required" ),
                ValidatedFormRevision = 4
            };
            Add( authority.Push( new( "session", 1, form.Id, 0, 1, form ) ) );
            for ( var depth = 4; depth <= 8; depth++ )
            {
                var child = detail with { Id = "detail-" + depth, Asset = null };
                var offered = authority.Push( new( "session", 1, child.Id, 0, 1, child ) );
                authority.Present( depth, offered.Revision );
                if ( depth == 8 )
                {
                    actual.Add( Normalize( offered.Content ) );
                }
            }
            File.Delete( Path.Combine( root, "assets/icon.png" ) );
            Add( authority.Pop( ) );
            for ( var depth = 6; depth >= 2; depth-- )
            {
                var restored = authority.Pop( );
                authority.Present( restored.Revision, restored.Revision );
                if ( depth == 2 )
                {
                    actual.Add( Normalize( restored.Content ) );
                }
            }
            Add( authority.Pop( ) );
            var fixture = Path.Combine( FindRepositoryRoot( ), "tests/CommandDeck.Specs/fixtures/extension-render-golden.jsonl" );
            Equal( string.Join( '\n', File.ReadAllLines( fixture ) ), string.Join( '\n', actual ) );

            void Add( PresentedExtensionView offered )
            {
                authority.Present( offered.Revision, offered.Revision );
                actual.Add( Normalize( offered.Content ) );
            }
        }
        finally { Directory.Delete( root, true ); }

        static string Normalize( JsonElement content )
        {
            var node = JsonNode.Parse( content.GetRawText( ) )!;
            var count = 0;
            Walk( node );
            return node.ToJsonString( );
            void Walk( JsonNode value )
            {
                if ( value is JsonObject obj )
                {
                    foreach ( var key in obj.Select( pair => pair.Key ).ToArray( ) )
                    {
                        if ( key is "handle" or "backHandle" or "previousPage" or "nextPage" )
                        {
                            obj[key] = "handle-" + ++count;
                        }
                        else if ( obj[key] is { } child )
                        {
                            Walk( child );
                        }
                    }
                }
                else if ( value is JsonArray array )
                {
                    foreach ( var child in array )
                    {
                        if ( child is not null )
                        {
                            Walk( child );
                        }
                    }
                }
            }
        }
    }
}
