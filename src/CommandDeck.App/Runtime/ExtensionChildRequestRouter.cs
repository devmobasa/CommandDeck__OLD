using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions.Views;

namespace CommandDeck;

internal sealed class ExtensionChildRequestRouter( ExtensionCommandSession session, IExtensionSessionHost host, string[] features )
{
    private static readonly JsonElement EmptyObject = JsonElement.Parse( "{}" );
    private ExtensionAssets? _assets;
    public void SetArtifact( string artifact ) => _assets = new ExtensionAssets( artifact );
    public string ResolveAsset( string asset )
    {
        var assets = _assets;
        if ( assets is null )
        {
            throw new InvalidOperationException( "The extension artifact has not been prepared." );
        }

        return assets.Resolve( asset );
    }

    public async ValueTask<JsonElement> HandleAsync( ExtensionFrame frame, CancellationToken cancellationToken )
    {
        try
        { return await HandleCoreAsync( frame, cancellationToken ).ConfigureAwait( false ); }
        catch ( JsonException ) { throw new ExtensionApplicationException( frame.Method is ExtensionMethods.ViewsReplace or ExtensionMethods.ViewsPush ? ExtensionErrorCodes.InvalidView : ExtensionErrorCodes.InvalidRequest ); }
        catch ( InvalidDataException ) { throw new ExtensionApplicationException( frame.Method is ExtensionMethods.ViewsReplace or ExtensionMethods.ViewsPush ? ExtensionErrorCodes.InvalidView : ExtensionErrorCodes.InvalidRequest ); }
    }

    private async ValueTask<JsonElement> HandleCoreAsync( ExtensionFrame frame, CancellationToken cancellationToken )
    {
        if ( frame.Method == ExtensionMethods.CommandBroker )
        {
            return await HandleBrokerAsync( frame, cancellationToken ).ConfigureAwait( false );
        }
        if ( frame.Method == ExtensionMethods.CommandData )
        {
            var dataRequest = frame.Params?.Deserialize( ExtensionViewJsonContext.Default.CommandDataRequest );
            if ( dataRequest is null )
            {
                throw new InvalidDataException( "Missing data request." );
            }

            if ( !features.Contains( dataRequest.Operation == ExtensionDataOperations.Settings ? ExtensionFeatures.Settings : ExtensionFeatures.Storage, StringComparer.Ordinal ) )
            {
                throw new ExtensionApplicationException( ExtensionErrorCodes.UnsupportedFeature );
            }
            return JsonSerializer.SerializeToElement( await host.DataAsync( session, dataRequest, cancellationToken ).ConfigureAwait( false ), ExtensionViewJsonContext.Default.CommandDataResult );
        }
        if ( frame.Method == ExtensionMethods.CopyText )
        {
            if ( session.Command.Mode != ExtensionCommandModes.NoView || !features.Contains( ExtensionFeatures.CopyText, StringComparer.Ordinal ) )
            {
                throw new ExtensionApplicationException( ExtensionErrorCodes.UnsupportedFeature );
            }
            var request = frame.Params?.Deserialize( ExtensionViewJsonContext.Default.CopyTextRequest );
            if ( request is null )
            {
                throw new InvalidDataException( "Missing effect." );
            }

            await host.CopyAsync( session, request, cancellationToken ).ConfigureAwait( false );
            return EmptyObject;
        }
        if ( session.Command.Mode != ExtensionCommandModes.View )
        {
            throw new InvalidDataException( "No-view commands cannot publish views." );
        }
        var update = frame.Params?.Deserialize( ExtensionViewJsonContext.Default.ViewUpdate );
        if ( update is null )
        {
            throw new InvalidDataException( "Missing view." );
        }

        ValidateRenderer( update.Content, frame.Method! );
        var view = frame.Method == ExtensionMethods.ViewsPush ? session.Authority.Push( update ) : session.Authority.Replace( update );
        session.SetView( view );
        await session.PublishStatusAsync( ExtensionSessionStatuses.View, null, cancellationToken ).ConfigureAwait( false );
        return JsonSerializer.SerializeToElement( new ViewAccepted( view.ViewId, view.Revision ), ExtensionViewJsonContext.Default.ViewAccepted );
    }

    private void ValidateRenderer( ExtensionView content, string method )
    {
        try
        {
            ViewValidation.Validate( content );
        }
        catch ( InvalidDataException )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.InvalidView );
        }
        if ( ViewRequirements.FirstMissing( content, method == ExtensionMethods.ViewsPush, features ) is not null )
        {
            throw new ExtensionApplicationException( ExtensionErrorCodes.UnsupportedFeature );
        }
    }

    private async ValueTask<JsonElement> HandleBrokerAsync( ExtensionFrame frame, CancellationToken token )
    {
        if ( !features.Contains( ExtensionFeatures.Brokers, StringComparer.Ordinal ) )
        {
            throw new InvalidOperationException( "Connection brokers were not negotiated." );
        }
        var request = frame.Params?.Deserialize( BrokerJsonContext.Default.BrokerRequest );
        if ( request is null )
        {
            throw new InvalidDataException( "Missing broker request." );
        }

        var result = await host.BrokerAsync( session, request, token ).ConfigureAwait( false );
        return JsonSerializer.SerializeToElement( result, BrokerJsonContext.Default.BrokerResult );
    }
}
