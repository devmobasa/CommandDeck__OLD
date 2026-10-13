using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

await ExtensionApp.RunAsync( args, new Notebook( ) );

sealed class Notebook : IExtensionCommand, IExtensionSearchProvider
{
    public string Id => "note";
    public string SearchId => "notes";
    private long _revision;
    private Note _note = new( "My searchable note", "" );
    public async ValueTask OpenAsync( CommandContext context, CancellationToken token )
    {
        var data = await context.Data.ReadAsync( token );
        _revision = data.Revision;
        if ( data.Values.TryGetValue( "search-note", out var value ) )
        {
            _note = value.Deserialize( NotebookJson.Default.Note )!;
        }
        if ( context.Inputs.ContainsKey( "searchItemId" ) )
        {
            await Detail( context, token );
        }
        else
        {
            await Edit( context, token );
        }
    }
    public async ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken token )
    {
        if ( commandEvent is ActionInvoked { ActionId: "edit" } )
        {
            await Edit( context, token );
            return;
        }
        if ( commandEvent is not FormSubmitted { ActionId: "save" } form )
        {
            return;
        }
        var note = new Note( form.Values["title"].GetString( )!, form.Values["body"].GetString( )! );
        var result = await context.Data.WriteAsync( "search-note", JsonSerializer.SerializeToElement( note, NotebookJson.Default.Note ), _revision, token );
        _revision = result.Revision;
        _note = note;
        await Detail( context, token );
    }
    private ValueTask Edit( CommandContext context, CancellationToken token ) => context.Views.ReplaceAsync( new FormView
    {
        Id = "note",
        Title = "Edit searchable note",
        Submit = ViewAction.Callback( "save", "Save note" ),
        Fields = [FormField.Text( "title", "Title", true, 120 ) with { DefaultValue = JsonSerializer.SerializeToElement( _note.Title, NotebookJson.Default.String ) },
            FormField.Multiline( "body", "Note", 2048 ) with { DefaultValue = JsonSerializer.SerializeToElement( _note.Body, NotebookJson.Default.String ) }]
    }, token );
    private ValueTask Detail( CommandContext context, CancellationToken token ) => context.Views.ReplaceAsync( new DetailView
    {
        Id = "note",
        Title = _note.Title,
        Body = _note.Body,
        Actions = [ViewAction.Callback( "edit", "Edit note" )]
    }, token );
    public ValueTask<ExtensionSearchResponse> SearchAsync( SearchContext context, CancellationToken token )
    {
        token.ThrowIfCancellationRequested( );
        var note = context.Cache?.Deserialize( NotebookJson.Default.Note );
        var match = note is not null && context.Query.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).All( term => (note.Title + " " + note.Body).Contains( term, StringComparison.OrdinalIgnoreCase ) );
        return ValueTask.FromResult( new ExtensionSearchResponse( match ? [new( "note", note!.Title, "Saved notebook entry" )] : [] ) );
    }
}
sealed record Note( string Title, string Body );
[JsonSerializable( typeof( Note ) )]
[JsonSerializable( typeof( string ) )]
partial class NotebookJson : JsonSerializerContext;
