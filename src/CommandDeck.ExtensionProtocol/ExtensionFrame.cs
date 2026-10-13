using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommandDeck.ExtensionProtocol;

public static class ExtensionLimits
{
    public const int MaximumFrameBytes = 262144;
    public const int MaximumDepth = 32;
    public const int MaximumPendingCalls = 32;
    public const int MaximumQueuedRequests = 64;
    public const int MaximumRetiredCalls = 256;
}

public sealed record ExtensionFrame
{
    public string Jsonrpc { get; init; } = "2.0";
    public string? Id { get; init; }
    public string? Method { get; init; }
    public JsonElement? Params { get; init; }
    public JsonElement? Result { get; init; }
    public ExtensionError? Error { get; init; }
}

public sealed record ExtensionError( int Code, string Message, ExtensionErrorData? Data = null );
public sealed record ExtensionErrorData( string Code );

public sealed class ExtensionRpcException( int code, string message, string? errorCode = null ) : Exception( message )
{
    public int Code { get; } = code;
    public string? ErrorCode { get; } = ExtensionErrorCodes.IsKnown( errorCode ) ? errorCode : null;
    public bool IsConflict => ErrorCode == ExtensionErrorCodes.RevisionConflict;
    public bool IsPermissionDenied => ErrorCode == ExtensionErrorCodes.PermissionDenied;
    public bool IsQuotaExceeded => ErrorCode == ExtensionErrorCodes.QuotaExceeded;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true,
    GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( ExtensionFrame ) )]
[JsonSerializable( typeof( InitializeRequest ) )]
[JsonSerializable( typeof( InitializeResult ) )]
[JsonSerializable( typeof( CancelRequest ) )]
[JsonSerializable( typeof( JsonElement ) )]
public sealed partial class ExtensionJsonContext : JsonSerializerContext;

public sealed record ProtocolVersion( int Major = 1, int Minor = 0 );
public sealed record SessionLimits( int MaximumFrameBytes = ExtensionLimits.MaximumFrameBytes );
public sealed record InitializeRequest(
    ProtocolVersion Protocol,
    string SessionId,
    long ExecutionEpoch,
    string CommandId,
    string Purpose,
    string[] HostFeatures,
    SessionLimits Limits );
public sealed record InitializeResult( ProtocolVersion Protocol, string SdkVersion, string[] Commands, string[]? Features = null );
public sealed record CancelRequest( string Id );
