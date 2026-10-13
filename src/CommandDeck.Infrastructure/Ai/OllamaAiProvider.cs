using System.Net.Http.Headers;
using System.Text.Json;

namespace CommandDeck;

public sealed class OllamaAiProvider : IAiProvider
{
    private readonly AiProviderOptions _options;
    private readonly HttpClient _client;

    public OllamaAiProvider( AiProviderOptions options, HttpClient client )
    {
        _options = options;
        _client = client;
    }

    public string Id => _options.Id;

    public AiProviderDescriptor Describe( ) => ProviderDescriptor.Create(
        _options,
        new AiProviderCapabilities
        {
            Discovery = true,
            Streaming = true,
            Usage = true,
            AdapterContractVersion = "ollama-v1"
        } );

    public async ValueTask<IReadOnlyList<AiModelDescriptor>> DiscoverModelsAsync(
        CancellationToken cancellationToken )
    {
        using var response = await _client.GetAsync(
            "api/tags", HttpCompletionOption.ResponseHeadersRead, cancellationToken )
            .ConfigureAwait( false );
        AiHttpContent.EnsureSuccess( response );
        var body = await AiHttpContent.ReadBoundedJsonAsync(
            response.Content,
            CommandDeckJsonContext.Default.OllamaTagsResponse,
            AiLimits.MaximumDiscoveryBytes,
            cancellationToken ).ConfigureAwait( false );
        return NormalizeModels( body?.Models.Select( static model => model.Model ?? model.Name ) );
    }

    public AiPreparedProviderRequest PrepareRequest( AiPrompt prompt )
    {
        OllamaChatRequest request = new( )
        {
            Model = prompt.ModelOverride ?? _options.Model,
            Stream = true,
            Messages = AiProviderMessages.Build( prompt )
        };
        return new AiPreparedProviderRequest
        {
            ProviderId = Id,
            ModelId = request.Model,
            Destination = new Uri( _client.BaseAddress!, "api/chat" ).AbsoluteUri,
            Body = JsonSerializer.SerializeToUtf8Bytes(
                request, CommandDeckJsonContext.Default.OllamaChatRequest )
        };
    }

    public async IAsyncEnumerable<AiProviderStreamEvent> StreamAsync(
        AiPreparedProviderRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken )
    {
        EnsurePreparedRequest( request );
        using ByteArrayContent content = new( request.Body );
        content.Headers.ContentType = new MediaTypeHeaderValue( "application/json" )
        {
            CharSet = "utf-8"
        };
        using HttpRequestMessage message = new( HttpMethod.Post, "api/chat" )
        {
            Content = content
        };
        using var response = await _client.SendAsync(
            message, HttpCompletionOption.ResponseHeadersRead, cancellationToken ).ConfigureAwait( false );
        AiHttpContent.EnsureSuccess( response );
        await using AiIdleTimeoutStream stream = new(
            await response.Content.ReadAsStreamAsync( cancellationToken ).ConfigureAwait( false ),
            TimeSpan.FromSeconds( 30 ) );
        await foreach ( var value in OllamaNdjsonReader.ReadAsync(
            stream, request.ModelId, cancellationToken ) )
        {
            if ( value.Terminal && _options.ContextLimit is not null )
            {
                yield return new AiProviderStreamEvent
                {
                    Text = value.Text,
                    Terminal = true,
                    FinishCategory = value.FinishCategory,
                    Usage = new AiUsage
                    {
                        InputTokens = value.Usage?.InputTokens,
                        OutputTokens = value.Usage?.OutputTokens,
                        TotalTokens = value.Usage?.TotalTokens,
                        ContextLimit = _options.ContextLimit
                    }
                };
            }
            else
            {
                yield return value;
            }
        }
    }

    private void EnsurePreparedRequest( AiPreparedProviderRequest request )
    {
        if ( !string.Equals( request.ProviderId, Id, StringComparison.Ordinal ) ||
            request.Body.Length > AiLimits.MaximumProviderPayloadBytes )
        {
            throw new InvalidOperationException( "The prepared AI request is invalid for this provider." );
        }
    }

    private static AiModelDescriptor[] NormalizeModels( IEnumerable<string?>? ids ) =>
        ids?.Where( static id => !string.IsNullOrWhiteSpace( id ) )
            .Select( static id => id!.Trim( ) )
            .Where( static id => System.Text.Encoding.UTF8.GetByteCount( id ) <= AiLimits.MaximumModelIdBytes &&
                id.All( static character => !char.IsControl( character ) ) )
            .Distinct( StringComparer.Ordinal )
            .Order( StringComparer.OrdinalIgnoreCase )
            .Take( AiLimits.MaximumModels )
            .Select( static id => new AiModelDescriptor { Id = id, DisplayName = id } )
            .ToArray( ) ?? [];
}
