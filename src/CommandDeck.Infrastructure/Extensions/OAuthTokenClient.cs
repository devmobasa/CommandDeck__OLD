using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

internal sealed class OAuthTokenClient( ExtensionConnectionSetup setup )
{
    public async ValueTask<JsonDocument> RequestAsync( Dictionary<string, string> values, CancellationToken token )
    {
        var result = await PostAsync( setup.OAuth!.TokenEndpoint, values, token ).ConfigureAwait( false );
        if ( result.StatusCode != 200 )
        {
            throw new InvalidOperationException( "Login expired or token exchange failed." );
        }

        return JsonDocument.Parse( Convert.FromBase64String( result.Body ?? "" ), new JsonDocumentOptions { MaxDepth = 8 } );
    }

    public async ValueTask<BrokerResult> PostAsync( string endpoint, Dictionary<string, string> values, CancellationToken token )
    {
        var uri = new Uri( endpoint );
        using var content = new FormUrlEncodedContent( values );
        var body = await content.ReadAsByteArrayAsync( token ).ConfigureAwait( false );
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource( token );
        deadline.CancelAfter( TimeSpan.FromSeconds( 15 ) );
        using var client = ExtensionHttpTransport.Create( ExtensionHttpTransport.Origin( setup.OAuth!.Issuer, setup.PrivateNetwork ), setup.PrivateNetwork );
        return await ExtensionHttpTransport.SendAsync( client, uri, new( "POST", uri.AbsolutePath, Convert.ToBase64String( body ), "application/x-www-form-urlencoded" ), null, deadline.Token ).ConfigureAwait( false );
    }
}
