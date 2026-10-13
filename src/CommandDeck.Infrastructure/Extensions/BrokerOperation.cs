using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

internal abstract record BrokerOperation
{
    public static BrokerOperation FromRequest( BrokerRequest request ) => request.Operation switch
    {
        ExtensionBrokerOperations.ListFiles => new FileListOperation( request.Reference, request.Offset ),
        ExtensionBrokerOperations.ReadFile => new FileReadOperation( request.Reference ),
        ExtensionBrokerOperations.SendHttp when request.Http is { } http => new HttpSendOperation( http ),
        _ => throw new ExtensionApplicationException( ExtensionErrorCodes.InvalidRequest )
    };
}
internal sealed record FileListOperation( string? Reference, int Offset ) : BrokerOperation;
internal sealed record FileReadOperation( string? Reference ) : BrokerOperation;
internal sealed record HttpSendOperation( BrokerHttpRequest Request ) : BrokerOperation;
