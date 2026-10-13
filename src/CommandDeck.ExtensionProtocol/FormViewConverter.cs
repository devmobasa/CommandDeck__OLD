using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.Extensions.Views;

namespace CommandDeck.ExtensionProtocol;

// Private mutable input preserves omitted defaults on the net10 source generator; public models remain init-only.
public sealed class FormViewConverter : JsonConverter<FormView>
{
    public override FormView Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
    {
        var wire = JsonSerializer.Deserialize( ref reader, FormViewWireContext.Default.FormViewWire );
        if ( wire is null )
        {
            throw new JsonException( "Missing wire model." );
        }

        return new FormView
        {
            Fields = wire.Fields is null ? default : wire.Fields.ToImmutableArray( ),
            Submit = wire.Submit,
            FieldErrors = wire.FieldErrors?.ToImmutableDictionary( StringComparer.Ordinal )!,
            ValidatedFormRevision = wire.ValidatedFormRevision,
            Id = wire.Id,
            Title = wire.Title,
            Actions = wire.Actions is null ? default : wire.Actions.ToImmutableArray( ),
            Loading = wire.Loading,
            Error = wire.Error,
        };
    }

    public override void Write( Utf8JsonWriter writer, FormView value, JsonSerializerOptions options ) =>
        JsonSerializer.Serialize( writer, new FormViewWire
        {
            Fields = value.Fields.IsDefault ? null! : value.Fields.ToArray( ),
            Submit = value.Submit,
            FieldErrors = value.FieldErrors?.ToDictionary( StringComparer.Ordinal )!,
            ValidatedFormRevision = value.ValidatedFormRevision,
            Id = value.Id,
            Title = value.Title,
            Actions = value.Actions.IsDefault ? null! : value.Actions.ToArray( ),
            Loading = value.Loading,
            Error = value.Error,
        }, FormViewWireContext.Default.FormViewWire );
}

internal sealed class FormViewWire
{
    public FormField[] Fields { get; set; } = [];
    public required CallbackAction Submit { get; set; }
    public Dictionary<string, string> FieldErrors { get; set; } = new( StringComparer.Ordinal );
    public long ValidatedFormRevision { get; set; }
    public required string Id { get; set; }
    public required string Title { get; set; }
    public ViewAction[] Actions { get; set; } = [];
    public bool Loading { get; set; }
    public string? Error { get; set; }
}

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, GenerationMode = JsonSourceGenerationMode.Metadata )]
[JsonSerializable( typeof( FormViewWire ) )]
internal sealed partial class FormViewWireContext : JsonSerializerContext;
