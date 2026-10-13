using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions.Views;

// Explicit converters retain the preview wire order and defaults while the author models become typed.
public sealed class ViewActionConverter<T> : JsonConverter<T> where T : ViewAction
{
    public override T Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
    {
        var wire = JsonSerializer.Deserialize( ref reader, ViewPayloadJsonContext.Default.ActionWire );
        if ( wire is null )
        {
            throw new JsonException( "Missing action." );
        }

        ViewAction action = wire.Kind switch
        {
            ExtensionActionKinds.Callback when wire.Text is null && wire.Url is null => ViewAction.Callback( wire.Id, wire.Title ),
            ExtensionActionKinds.CopyText when wire.Text is not null && wire.Url is null => ViewAction.CopyText( wire.Id, wire.Title, wire.Text ),
            ExtensionActionKinds.OpenUrl when wire.Url is not null && wire.Text is null => ViewAction.OpenUrl( wire.Id, wire.Title, wire.Url ),
            _ => throw new JsonException( "Invalid action shape." )
        };
        if ( action is not T typed )
        {
            throw new JsonException( "Invalid action type." );
        }

        return typed;
    }

    public override void Write( Utf8JsonWriter writer, T value, JsonSerializerOptions options )
    {
        writer.WriteStartObject( );
        writer.WriteString( "id", value.Id );
        writer.WriteString( "kind", value.Kind );
        writer.WriteString( "title", value.Title );
        if ( value is CopyTextAction copy )
        {
            writer.WriteString( "text", copy.Text );
        }
        else if ( value is OpenUrlAction open )
        {
            writer.WriteString( "url", open.Url );
        }
        writer.WriteEndObject( );
    }
}

public sealed class FormFieldConverter<T> : JsonConverter<T> where T : FormField
{
    public override T Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
    {
        var wire = JsonSerializer.Deserialize( ref reader, ViewPayloadJsonContext.Default.FieldWire );
        if ( wire is null || wire.Choices is null )
        {
            throw new JsonException( "Missing field." );
        }

        FormField field = wire.Kind switch
        {
            ExtensionFormFieldKinds.Text => FormField.Text( wire.Id, wire.Label, wire.Required, wire.MaxLength ),
            ExtensionFormFieldKinds.Multiline => FormField.Multiline( wire.Id, wire.Label, wire.MaxLength ),
            ExtensionFormFieldKinds.Choice => FormField.Choice( wire.Id, wire.Label, wire.Choices ),
            ExtensionFormFieldKinds.Checkbox => FormField.Checkbox( wire.Id, wire.Label ),
            _ => throw new JsonException( "Invalid field shape." )
        };
        field = field with
        {
            Required = wire.Required,
            DefaultValue = wire.DefaultValue?.Clone( ),
            LegacyMaximumLength = wire.MaxLength,
            LegacyChoices = wire.Choices.ToImmutableArray( )
        };
        if ( field is not T typed )
        {
            throw new JsonException( "Invalid field type." );
        }

        return typed;
    }

    public override void Write( Utf8JsonWriter writer, T value, JsonSerializerOptions options )
    {
        writer.WriteStartObject( );
        writer.WriteString( "id", value.Id );
        writer.WriteString( "label", value.Label );
        writer.WriteString( "kind", value.Kind );
        writer.WriteBoolean( "required", value.Required );
        writer.WriteNumber( "maxLength", value.MaxLength );
        writer.WritePropertyName( "choices" );
        JsonSerializer.Serialize( writer, value.Choices, ViewPayloadJsonContext.Default.ImmutableArrayChoice );
        if ( value.DefaultValue is { } defaultValue )
        {
            writer.WritePropertyName( "defaultValue" );
            defaultValue.WriteTo( writer );
        }
        writer.WriteEndObject( );
    }
}

internal sealed record ActionWire
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public string? Text { get; init; }
    public string? Url { get; init; }
}

internal sealed class FieldWire
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Kind { get; init; }
    public bool Required { get; set; }
    public int MaxLength { get; set; } = 8192;
    public Choice[] Choices { get; set; } = [];
    public JsonElement? DefaultValue { get; init; }
}

[JsonSourceGenerationOptions( PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow )]
[JsonSerializable( typeof( ActionWire ) )]
[JsonSerializable( typeof( FieldWire ) )]
[JsonSerializable( typeof( ImmutableArray<Choice> ) )]
[JsonSerializable( typeof( string ) )]
[JsonSerializable( typeof( bool ) )]
internal sealed partial class ViewPayloadJsonContext : JsonSerializerContext;
