using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Extensions.Views;

namespace CommandDeck.ExtensionProtocol;

public sealed record CommandOpenRequest( long Generation, string InvocationId, Dictionary<string, JsonElement> Inputs );
public sealed record CommandOutcome( string Outcome, string? Message = null );
public sealed record CopyTextRequest( string SessionId, long ExecutionEpoch, long Generation, string InvocationId, string Text );
public sealed record CommandDataRequest( string SessionId, long ExecutionEpoch, long Generation, string InvocationId,
    string Operation, string? Key = null, long ExpectedRevision = 0, JsonElement? Value = null );
public sealed record CommandDataResult( long Revision, Dictionary<string, JsonElement> Values );
public sealed record ViewUpdate( string SessionId, long ExecutionEpoch, string ViewId, long ExpectedRevision, long Generation, ExtensionView Content );
public sealed record ViewPop( string SessionId, long ExecutionEpoch, long Generation );
public sealed record ViewAccepted( string ViewId, long Revision );
public sealed record CommandEventRequest( long Generation, string InvocationId, string ViewId, long ViewRevision, CommandEvent Event );

// Keep reserved events readable on the wire while warning authors that the host never emits them.
#pragma warning disable CDEXT0001
[JsonPolymorphic( TypeDiscriminatorPropertyName = "kind" )]
[JsonDerivedType( typeof( ActionInvoked ), "action" )]
[JsonDerivedType( typeof( SearchChanged ), "search" )]
[JsonDerivedType( typeof( FormSubmitted ), "submit" )]
[JsonDerivedType( typeof( PageRequested ), "page" )]
[JsonDerivedType( typeof( SelectionChanged ), "selection" )]
public abstract record CommandEvent;
public sealed record ActionInvoked( string ActionId, string? ItemId = null ) : CommandEvent;
[System.Diagnostics.CodeAnalysis.Experimental( "CDEXT0001" )]
public sealed record SearchChanged( string Query ) : CommandEvent;
public sealed record FormSubmitted( string ActionId, long FormRevision, Dictionary<string, JsonElement> Values ) : CommandEvent;
public sealed record PageRequested( string Token ) : CommandEvent;
[System.Diagnostics.CodeAnalysis.Experimental( "CDEXT0001" )]
public sealed record SelectionChanged( string? ItemId ) : CommandEvent;
#pragma warning restore CDEXT0001

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectRequiredConstructorParameters = true,
    GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( CommandOpenRequest ) )]
[JsonSerializable( typeof( CommandOutcome ) )]
[JsonSerializable( typeof( CopyTextRequest ) )]
[JsonSerializable( typeof( CommandDataRequest ) )]
[JsonSerializable( typeof( CommandDataResult ) )]
[JsonSerializable( typeof( CommandEventRequest ) )]
[JsonSerializable( typeof( ViewUpdate ) )]
[JsonSerializable( typeof( ViewPop ) )]
[JsonSerializable( typeof( ViewAccepted ) )]
[JsonSerializable( typeof( ExtensionView ) )]
[JsonSerializable( typeof( ListView ) )]
[JsonSerializable( typeof( DetailView ) )]
[JsonSerializable( typeof( FormView ) )]
public sealed partial class ExtensionViewJsonContext : JsonSerializerContext;
