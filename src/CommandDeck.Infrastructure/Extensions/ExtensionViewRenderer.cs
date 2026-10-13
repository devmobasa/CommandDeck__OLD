using CommandDeck.Extensions.Views;

namespace CommandDeck;

internal sealed class ExtensionViewRenderer( Func<string, string>? resolveAsset,
    Func<ExtensionActionBinding, string?, string> bind )
{
    private int _budget = 128 * 1024;

    public RenderedExtensionView Build( ExtensionView view, int depth, RenderedExtensionView? retained )
    {
        var model = new RenderedExtensionView
        {
            Kind = Kind( view ),
            Id = view.Id,
            Title = view.Title,
            Actions = view.Actions.Select( action => Action( action ) ).ToArray( ),
            Loading = view.Loading,
            Error = view.Error,
            Depth = depth,
            BackHandle = retained is null && depth > 1 ? bind( new BackBinding( ), null ) : null,
            RestoredBackHandle = retained is not null && depth > 1 ? bind( new BackBinding( ), null ) : null
        };
        return view switch
        {
            ListView list => List( model, list, retained ),
            DetailView detail => model with
            {
                Asset = resolveAsset is null ? detail.Asset : null,
                Body = detail.Body,
                Metadata = detail.Metadata,
                ImageSource = Image( detail.Asset, retained?.ImageSource, retained is not null )
            },
            FormView form => model with
            {
                Fields = form.Fields,
                Submit = Action( form.Submit, binding: new SubmitBinding( form.Submit.Id ) ),
                FieldErrors = form.FieldErrors,
                ValidatedFormRevision = form.ValidatedFormRevision
            },
            _ => throw new InvalidDataException( "Invalid view." )
        };
    }

    private RenderedExtensionView List( RenderedExtensionView model, ListView list, RenderedExtensionView? retained ) => model with
    {
        Search = list.Search,
        EmptyMessage = list.EmptyMessage,
        PreviousPage = list.PreviousPage is null ? null : bind( new PageBinding( list.PreviousPage ), null ),
        NextPage = list.NextPage is null ? null : bind( new PageBinding( list.NextPage ), null ),
        Items = list.Items.Select( ( item, index ) => new RenderedExtensionItem( item.Id, item.Title, item.Subtitle,
            resolveAsset is null ? item.Asset : null, item.Actions.Select( action => Action( action, item.Id ) ).ToArray( ),
            Image( item.Asset, retained?.Items?[index].ImageSource, retained is not null ) ) ).ToArray( )
    };

    private RenderedExtensionAction Action( ViewAction action, string? itemId = null, ExtensionActionBinding? binding = null ) =>
        new( action.Id, action.Kind, action.Title, bind( binding ?? (action is CallbackAction ? new CallbackBinding( action.Id ) : new EffectBinding( action )), itemId ) );

    private string? Image( string? asset, string? prior, bool restoring )
    {
        if ( asset is null || resolveAsset is null )
        {
            return null;
        }
        var image = restoring ? prior : resolveAsset( asset );
        _budget -= image?.Length ?? 0;
        if ( _budget < 0 )
        {
            throw new InvalidDataException( "The view's assets exceed 128 KiB." );
        }
        return image;
    }

    private static string Kind( ExtensionView view ) => view switch
    {
        ListView => "list",
        DetailView => "detail",
        FormView => "form",
        _ => throw new InvalidDataException( "Invalid view." )
    };
}
