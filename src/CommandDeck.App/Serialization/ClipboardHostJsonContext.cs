using System.Text.Json.Serialization;

namespace CommandDeck;

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase )]
[JsonSerializable( typeof( ClipboardWaylandCapabilities ) )]
internal partial class ClipboardHostJsonContext : JsonSerializerContext;
