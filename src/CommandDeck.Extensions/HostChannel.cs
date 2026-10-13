using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions;

internal readonly record struct InvocationStamp( string SessionId, long Epoch, long Generation, string InvocationId );

/// <summary>One authority and lifetime boundary for every SDK host call.</summary>
internal sealed class HostChannel( CommandContext context, ExtensionPeer peer, InitializeRequest initialization, string mode )
{
    public FrozenSet<string> Features { get; } = initialization.HostFeatures.ToFrozenSet( StringComparer.Ordinal );
    public InvocationStamp Stamp => new( initialization.SessionId, initialization.ExecutionEpoch, context.Generation, context.InvocationId );
    public CancellationToken Lifetime => context.Lifetime;
    public void EnsureActive( ) => context.EnsureActive( );

    public void RequireMode( string required )
    {
        EnsureActive( );
        if ( mode != required )
        {
            throw new InvalidOperationException( required == ExtensionCommandModes.View ?
                "No-view commands cannot publish views." : "View commands must use displayed actions for desktop effects." );
        }
    }

    public void RequireFeature( string feature )
    {
        EnsureActive( );
        if ( !Features.Contains( feature ) )
        {
            throw new InvalidOperationException( "The host does not support required feature " + feature + "." );
        }
    }

    public async ValueTask<TResult> CallAsync<TRequest, TResult>( string method, string? feature, TRequest request,
        JsonTypeInfo<TRequest> requestType, JsonTypeInfo<TResult> resultType, CancellationToken cancellationToken )
    {
        var response = await CallAsync( method, feature, request, requestType, cancellationToken ).ConfigureAwait( false );
        var result = response.Deserialize( resultType );
        if ( result is null )
        {
            throw new InvalidDataException( "Missing host response." );
        }

        return result;
    }

    public async ValueTask<JsonElement> CallAsync<TRequest>( string method, string? feature, TRequest request,
        JsonTypeInfo<TRequest> requestType, CancellationToken cancellationToken )
    {
        EnsureActive( );
        if ( feature is not null )
        {
            RequireFeature( feature );
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, Lifetime );
        var response = await peer.CallAsync( method, JsonSerializer.SerializeToElement( request, requestType ), lifetime.Token ).ConfigureAwait( false );
        EnsureActive( );
        return response;
    }
}
