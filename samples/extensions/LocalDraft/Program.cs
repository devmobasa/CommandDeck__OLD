using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;
using CommandDeck.ExtensionSample;

await ExtensionApp.RunAsync( args, new DraftCommand( ) );

namespace CommandDeck.ExtensionSample
{
public sealed class DraftCommand : ExtensionCommandBase
{
    private long _storageRevision;
    private FormView? _form;
    public override string Id => "edit";
    public override async ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
    {
        var settings = await context.Data.ReadSettingsAsync( cancellationToken );
        var stored = await context.Data.ReadAsync( cancellationToken );
        _storageRevision = stored.Revision;
        var draft = stored.Values.TryGetValue( "draft", out var value ) ? value.Deserialize( DraftJsonContext.Default.Draft ) : null;
        draft ??= new Draft( settings.Values["defaultTitle"].GetString( )!, "", "note", false );
        _form = new FormView
        {
            Id = "draft",
            Title = "Local draft",
            Submit = ViewAction.Callback( "save", "Save draft" ),
            Fields = [
                FormField.Text( "title", "Title", true, 120 ).WithDefault(draft.Title),
                FormField.Multiline( "body", "Draft", 8192 ).WithDefault(draft.Body),
                FormField.Choice( "category", "Category", [new( "note", "Note" ), new( "task", "Task" )] ) with { DefaultValue = JsonSerializer.SerializeToElement( draft.Category, DraftJsonContext.Default.String ) },
                FormField.Checkbox( "done", "Completed" ).WithDefault(draft.Done)
            ]
        };
        await context.Views.ReplaceAsync( _form, cancellationToken );
    }

    protected override async ValueTask OnSubmitAsync( CommandContext context, FormSubmitted submission, CancellationToken cancellationToken )
    {
        if ( submission is not FormSubmitted { ActionId: "save" } submitted || _form is null )
        {
            return;
        }
        var draft = new Draft( submitted.Values["title"].GetString( )!, submitted.Values["body"].GetString( )!,
                    submitted.Values["category"].GetString( )!, submitted.Values["done"].GetBoolean( ) );
        if ( draft.Title.Trim( ).Equals( "untitled", StringComparison.OrdinalIgnoreCase ) )
        {
            await context.Views.ReplaceAsync( _form with
            {
                FieldErrors = _form.FieldErrors.SetItem( "title", "Choose a title other than Untitled." ),
                ValidatedFormRevision = submitted.FormRevision
            }, cancellationToken );
            return;
        }
        try
        {
            var saved = await context.Data.WriteAsync( "draft", JsonSerializer.SerializeToElement( draft, DraftJsonContext.Default.Draft ), _storageRevision, cancellationToken );
            _storageRevision = saved.Revision;
            _form = _form with
            {
                Title = "Draft saved",
                FieldErrors = _form.FieldErrors.Clear( ),
                ValidatedFormRevision = submitted.FormRevision,
                Error = null
            };
        }
        catch ( ExtensionRpcException exception )
        {
            _form = _form with
            {
                Error = exception.IsConflict ? "The draft changed elsewhere. Close and reopen before saving again." : "The draft could not be saved. Check permissions and reopen before trying again."
            };
        }
        await context.Views.ReplaceAsync( _form, cancellationToken );
    }
}

sealed record Draft( string Title, string Body, string Category, bool Done );
[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase )]
[JsonSerializable( typeof( Draft ) )]
[JsonSerializable( typeof( string ) )]
sealed partial class DraftJsonContext : JsonSerializerContext;

}
