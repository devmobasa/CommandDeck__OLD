using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommandDeck.ExtensionProtocol;

// Private mutable input preserves omitted defaults on the net10 source generator; public models remain init-only.
public sealed class ExtensionManifestConverter : JsonConverter<ExtensionManifest>
{
    public override ExtensionManifest Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
    {
        var wire = JsonSerializer.Deserialize( ref reader, ExtensionManifestWireContext.Default.ExtensionManifestWire );
        if ( wire is null )
        {
            throw new JsonException( "Missing wire model." );
        }

        return new ExtensionManifest
        {
            ManifestVersion = wire.ManifestVersion,
            Id = wire.Id,
            Version = wire.Version,
            Name = wire.Name,
            Description = wire.Description,
            License = wire.License,
            Protocol = wire.Protocol,
            Execution = wire.Execution,
            Targets = wire.Targets,
            Commands = wire.Commands is null ? default : wire.Commands.ToImmutableArray( ),
            RequiredFeatures = wire.RequiredFeatures is null ? default : wire.RequiredFeatures.ToImmutableArray( ),
            Capabilities = wire.Capabilities,
            Preferences = wire.Preferences is null ? default : wire.Preferences.ToImmutableArray( ),
            SearchProviders = wire.SearchProviders is null ? default : wire.SearchProviders.ToImmutableArray( ),
        };
    }

    public override void Write( Utf8JsonWriter writer, ExtensionManifest value, JsonSerializerOptions options ) =>
        JsonSerializer.Serialize( writer, new ExtensionManifestWire
        {
            ManifestVersion = value.ManifestVersion,
            Id = value.Id,
            Version = value.Version,
            Name = value.Name,
            Description = value.Description,
            License = value.License,
            Protocol = value.Protocol,
            Execution = value.Execution,
            Targets = value.Targets,
            Commands = value.Commands.IsDefault ? null! : value.Commands.ToArray( ),
            RequiredFeatures = value.RequiredFeatures.IsDefault ? null! : value.RequiredFeatures.ToArray( ),
            Capabilities = value.Capabilities,
            Preferences = value.Preferences.IsDefault ? null! : value.Preferences.ToArray( ),
            SearchProviders = value.SearchProviders.IsDefault ? null! : value.SearchProviders.ToArray( ),
        }, ExtensionManifestWireContext.Default.ExtensionManifestWire );
}

internal sealed class ExtensionManifestWire
{
    public required int ManifestVersion { get; set; }
    public required string Id { get; set; }
    public required string Version { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string License { get; set; }
    public required ManifestProtocol Protocol { get; set; }
    public required ManifestExecution Execution { get; set; }
    public required Dictionary<string, ExtensionTarget> Targets { get; set; }
    public required ExtensionCommand[] Commands { get; set; }
    public string[] RequiredFeatures { get; set; } = [];
    public Dictionary<string, CapabilityRequest> Capabilities { get; set; } = new( StringComparer.Ordinal );
    public ExtensionPreference[] Preferences { get; set; } = [];
    public ExtensionSearchProvider[] SearchProviders { get; set; } = [];
}

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( ExtensionManifestWire ) )]
internal sealed partial class ExtensionManifestWireContext : JsonSerializerContext;
