using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CommandDeck;

internal static class AiHttpContent
{
    public static async ValueTask<T?> ReadBoundedJsonAsync<T>(
        HttpContent content,
        JsonTypeInfo<T> typeInfo,
        int maximumBytes,
        CancellationToken cancellationToken )
    {
        await using var input = await content.ReadAsStreamAsync( cancellationToken )
            .ConfigureAwait( false );
        using MemoryStream buffer = new( Math.Min( maximumBytes, 64 * 1024 ) );
        var block = new byte[16 * 1024];
        while ( true )
        {
            var count = await input.ReadAsync( block, cancellationToken ).ConfigureAwait( false );
            if ( count == 0 )
            {
                break;
            }
            if ( buffer.Length + count > maximumBytes )
            {
                throw new AiProviderException(
                    AiErrorCategories.ResponseTooLarge,
                    safeSummary: "The AI provider response exceeded its byte limit." );
            }
            buffer.Write( block, 0, count );
        }
        return JsonSerializer.Deserialize( buffer.GetBuffer( ).AsSpan( 0, ( int ) buffer.Length ), typeInfo );
    }

    public static void RejectRedirect( HttpResponseMessage response )
    {
        if ( ( int ) response.StatusCode is >= 300 and < 400 )
        {
            var source = response.RequestMessage?.RequestUri;
            var target = response.Headers.Location;
            if ( source is not null && target is not null && !target.IsAbsoluteUri )
            {
                target = new Uri( source, target );
            }
            var sourceOrigin = source?.GetLeftPart( UriPartial.Authority ) ?? "configured endpoint";
            var targetOrigin = target?.GetLeftPart( UriPartial.Authority ) ?? "undisclosed target";
            throw new AiProviderException(
                AiErrorCategories.RedirectRejected,
                safeSummary: $"Redirect from {sourceOrigin} to {targetOrigin} was rejected." );
        }
    }

    public static void EnsureSuccess( HttpResponseMessage response )
    {
        RejectRedirect( response );
        if ( response.IsSuccessStatusCode )
        {
            return;
        }

        var category = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => AiErrorCategories.Authentication,
            System.Net.HttpStatusCode.Forbidden => AiErrorCategories.Permission,
            System.Net.HttpStatusCode.TooManyRequests => AiErrorCategories.RateLimited,
            _ => AiErrorCategories.Unavailable
        };
        int? retryAfter = response.Headers.RetryAfter?.Delta is TimeSpan delay
            ? Math.Max( 0, ( int ) Math.Ceiling( delay.TotalSeconds ) )
            : null;
        var summary = category switch
        {
            AiErrorCategories.Authentication => "The AI provider rejected its credential.",
            AiErrorCategories.Permission => "The AI provider denied this operation.",
            AiErrorCategories.RateLimited => "The AI provider rate limit was reached.",
            _ => "The AI provider returned an unavailable status."
        };
        throw new AiProviderException( category, retryAfter, summary );
    }
}

public sealed class AiProviderException(
    string category,
    int? retryAfterSeconds = null,
    string? safeSummary = null ) : AiOperationException(
        category,
        retryAfterSeconds,
        safeSummary ?? "The AI provider request failed." );
