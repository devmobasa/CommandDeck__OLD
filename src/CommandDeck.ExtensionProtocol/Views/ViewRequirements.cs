namespace CommandDeck.Extensions.Views;

using CommandDeck.ExtensionProtocol;

/// <summary>Features required to publish a view, shared by author and host validation.</summary>
public static class ViewRequirements
{
    public static IReadOnlySet<string> For( ExtensionView view, bool push )
    {
        ArgumentNullException.ThrowIfNull( view );
        HashSet<string> features = new( StringComparer.Ordinal )
        {
            view switch
            {
                ListView => ExtensionFeatures.ListView,
                DetailView => ExtensionFeatures.DetailView,
                FormView => ExtensionFeatures.FormView,
                _ => throw new InvalidDataException( "Unsupported extension view." )
            }
        };
        if ( push )
        {
            features.Add( ExtensionFeatures.ViewStack );
        }
        IEnumerable<ViewAction> actions = view.Actions;
        if ( view is ListView list )
        {
            // Recognize the reserved mode so it fails at the negotiated feature gate.
#pragma warning disable CDEXT0001
            if ( list.Search.Mode == SearchMode.Remote )
#pragma warning restore CDEXT0001
            {
                features.Add( ExtensionFeatures.RemoteSearch );
            }
            if ( list.PreviousPage is not null || list.NextPage is not null )
            {
                features.Add( ExtensionFeatures.Pagination );
            }
            if ( list.Items.Any( item => item.Asset is not null ) )
            {
                features.Add( ExtensionFeatures.PngAssets );
            }
            actions = actions.Concat( list.Items.SelectMany( item => item.Actions ) );
        }
        if ( view is DetailView { Asset: not null } )
        {
            features.Add( ExtensionFeatures.PngAssets );
        }
        if ( view is FormView form )
        {
            actions = actions.Append( form.Submit );
        }
        foreach ( var action in actions )
        {
            features.Add( action.Kind switch
            {
                ExtensionActionKinds.Callback => ExtensionFeatures.Commands,
                ExtensionActionKinds.CopyText => ExtensionFeatures.CopyTextAction,
                ExtensionActionKinds.OpenUrl => ExtensionFeatures.OpenUrlAction,
                _ => throw new InvalidDataException( "Unsupported extension action." )
            } );
        }
        return features;
    }

    public static string? FirstMissing( ExtensionView view, bool push, IReadOnlyCollection<string> negotiated ) =>
        For( view, push ).Order( StringComparer.Ordinal ).FirstOrDefault( feature => !negotiated.Contains( feature, StringComparer.Ordinal ) );
}
