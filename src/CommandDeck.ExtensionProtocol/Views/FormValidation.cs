using System.Collections.Immutable;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions.Views;

public static class FormValidation
{
    public static void ValidateSubmission( ImmutableArray<FormField> fields, Dictionary<string, JsonElement>? values )
    {
        if ( values is null || values.Count != fields.Length || values.Keys.Except( fields.Select( field => field.Id ), StringComparer.Ordinal ).Any( ) )
        {
            throw new InvalidDataException( "Submit exactly the declared form fields." );
        }
        foreach ( var field in fields )
        {
            if ( !values.TryGetValue( field.Id, out var value ) )
            {
                throw new InvalidDataException( "A form field is missing." );
            }
            ViewValidation.ValidateValue( field, value );
            if ( field.Required && field.Kind == ExtensionFormFieldKinds.Checkbox && value.ValueKind != JsonValueKind.True )
            {
                throw new InvalidDataException( "A required checkbox must be checked." );
            }
        }
    }
}
