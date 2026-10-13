using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions.Views;

[JsonConverter( typeof( FormFieldConverter<FormField> ) )]
public abstract record FormField
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public bool Required { get; init; }
    public JsonElement? DefaultValue { get; init; }
    public string Kind => this switch
    {
        TextField => ExtensionFormFieldKinds.Text,
        MultilineField => ExtensionFormFieldKinds.Multiline,
        ChoiceField => ExtensionFormFieldKinds.Choice,
        CheckboxField => ExtensionFormFieldKinds.Checkbox,
        _ => throw new InvalidOperationException( "Unsupported form field type." )
    };
    // Legacy wire fields remain readable; authors can set these only on the applicable derived types.
    internal int LegacyMaximumLength { get; init; } = 8192;
    internal ImmutableArray<Choice> LegacyChoices { get; init; } = [];
    public int MaxLength => this is TextualField text ? text.MaxLength : LegacyMaximumLength;
    public ImmutableArray<Choice> Choices => this is ChoiceField choice ? choice.Choices : LegacyChoices;

    public static TextField Text( string id, string label, bool required = false, int maxLength = 8192 ) =>
        new( ) { Id = id, Label = label, Required = required, MaxLength = maxLength };
    public static MultilineField Multiline( string id, string label, int maxLength = 8192 ) =>
        new( ) { Id = id, Label = label, MaxLength = maxLength };
    public static ChoiceField Choice( string id, string label, IEnumerable<Choice> choices ) => new( ) { Id = id, Label = label, Choices = choices.ToImmutableArray( ) };
    public static CheckboxField Checkbox( string id, string label ) => new( ) { Id = id, Label = label };
}

public abstract record TextualField : FormField
{
    public new int MaxLength { get; init; } = 8192;
}

[JsonConverter( typeof( FormFieldConverter<TextField> ) )]
public sealed record TextField : TextualField
{
    public TextField WithDefault( string value ) => this with { DefaultValue = JsonSerializer.SerializeToElement( value, ViewPayloadJsonContext.Default.String ) };
}

[JsonConverter( typeof( FormFieldConverter<MultilineField> ) )]
public sealed record MultilineField : TextualField
{
    public MultilineField WithDefault( string value ) => this with { DefaultValue = JsonSerializer.SerializeToElement( value, ViewPayloadJsonContext.Default.String ) };
}

[JsonConverter( typeof( FormFieldConverter<ChoiceField> ) )]
public sealed record ChoiceField : FormField
{
    public new ImmutableArray<Choice> Choices { get; init; } = [];
    public ChoiceField WithDefault( string value ) => this with { DefaultValue = JsonSerializer.SerializeToElement( value, ViewPayloadJsonContext.Default.String ) };
}

[JsonConverter( typeof( FormFieldConverter<CheckboxField> ) )]
public sealed record CheckboxField : FormField
{
    public CheckboxField WithDefault( bool value ) => this with { DefaultValue = JsonSerializer.SerializeToElement( value, ViewPayloadJsonContext.Default.Boolean ) };
}

public sealed record Choice( string Value, string Label );
