using System.Text.Json.Serialization;

namespace CommandDeck.ExtensionProtocol;

public sealed record BrokerRequest( string SessionId, long ExecutionEpoch, long Generation, string InvocationId,
    string Operation, string Connection, string? Reference = null, int Offset = 0, BrokerHttpRequest? Http = null );
public sealed record BrokerHttpRequest( string Method, string Path, string? Body = null, string? ContentType = null, string? IdempotencyKey = null );
public sealed record BrokerFileEntry( string Name, string Reference, bool Directory, long Size );
public sealed record BrokerResult( string Outcome, BrokerFileEntry[]? Entries = null, int? NextOffset = null,
    string? Body = null, int StatusCode = 0, string? ContentType = null );

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true, GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( BrokerRequest ) )]
[JsonSerializable( typeof( BrokerResult ) )]
[JsonSerializable( typeof( BrokerHttpRequest ) )]
public sealed partial class BrokerJsonContext : JsonSerializerContext;
