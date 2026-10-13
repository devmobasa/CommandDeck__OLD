using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

public static class ExtensionSettingsResolver
{
    public static Dictionary<string, JsonElement> Resolve( ExtensionInstallation installation )
    {
        Dictionary<string, JsonElement> values = new( StringComparer.Ordinal );
        foreach ( var preference in installation.Manifest.Preferences )
        {
            if ( preference.Type == "secret" )
            {
                throw new InvalidOperationException( "Secret settings require a credential broker and are not supported yet." );
            }
            var value = installation.Settings.TryGetValue( preference.Id, out var saved ) ? saved : preference.Default;
            if ( value is { } present )
            {
                ManifestValidation.ValidatePreferenceValue( preference, present );
                values.Add( preference.Id, present.Clone( ) );
            }
            else if ( preference.Required )
            {
                throw new InvalidOperationException( "Configure the extension's required settings before opening it." );
            }
        }
        return values;
    }
}
