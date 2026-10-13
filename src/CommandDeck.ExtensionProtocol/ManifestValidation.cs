using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommandDeck.Extensions.Views;

namespace CommandDeck.ExtensionProtocol;

public static partial class ManifestValidation
{
    public const int MaximumManifestBytes = 65536;
    public const int MaximumPackages = 128;
    public const string RuntimeProfile = ExtensionTargets.LinuxGlibcInvariantV1;
    public static readonly IReadOnlySet<string> KnownFeatures = ExtensionFeatures.Interactive.ToFrozenSet( StringComparer.Ordinal );
    public static readonly IReadOnlySet<string> KnownCapabilities = new HashSet<string>( StringComparer.Ordinal )
    {
        ExtensionPermissionIds.ClipboardWrite, ExtensionPermissionIds.DesktopOpenUrl, ExtensionPermissionIds.StorageRead, ExtensionPermissionIds.StorageWrite, ExtensionPermissionIds.FilesRead, ExtensionPermissionIds.HttpRequest, ExtensionPermissionIds.SecretsUse
    };

    [GeneratedRegex( "^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*\\.[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant )]
    private static partial Regex PackageIdPattern( );
    [GeneratedRegex( "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant )]
    private static partial Regex VersionPattern( );

    public static ExtensionManifest Parse( ReadOnlyMemory<byte> bytes )
    {
        if ( bytes.Length > MaximumManifestBytes )
        {
            Invalid( "The manifest exceeds 64 KiB." );
        }
        using var document = StrictJson.Parse( bytes, 16 );
        var manifest = document.RootElement.Deserialize( ExtensionManifestJsonContext.Default.ExtensionManifest );
        if ( manifest is null )
        {
            throw new InvalidDataException( "The extension manifest is missing." );
        }

        Validate( manifest );
        return manifest;
    }

    public static void Validate( ExtensionManifest manifest )
    {
        ArgumentNullException.ThrowIfNull( manifest );
        if ( manifest.ManifestVersion != 1 )
        {
            Invalid( "The manifest version is unsupported." );
        }
        if ( manifest.Id is not { Length: > 0 and <= 128 } || !PackageIdPattern( ).IsMatch( manifest.Id ) )
        {
            Invalid( "The package ID is invalid." );
        }
        ValidateVersion( manifest.Version );
        Label( manifest.Name, 512 );
        Label( manifest.Description, 2048 );
        Label( manifest.License, 128 );
        if ( manifest.Protocol is not { Major: 1, MinimumMinor: 0 } || manifest.Execution?.Profile != "restricted" )
        {
            Invalid( "The protocol or execution profile is unsupported." );
        }
        Features( manifest.RequiredFeatures );
        if ( manifest.Targets is not { Count: > 0 and <= 2 } || manifest.Commands.IsDefault || manifest.Commands.Length is < 1 or > 32 ||
             manifest.Capabilities is null || manifest.Capabilities.Count > 16 || manifest.Preferences.IsDefault || manifest.Preferences.Length > 16 )
        {
            Invalid( "The manifest contributions exceed their limits." );
        }
        ValidateTargets( manifest.Targets! );
        ValidateCapabilities( manifest.Capabilities! );
        ValidateCommands( manifest.Commands!, manifest.Capabilities! );
        ValidatePreferences( manifest.Preferences! );
        ValidateSearchProviders( manifest );
    }

    private static void ValidateSearchProviders( ExtensionManifest manifest )
    {
        if ( manifest.SearchProviders.IsDefault || manifest.SearchProviders.Length > 4 )
        {
            Invalid( "At most four search providers are allowed." );
        }
        HashSet<string> ids = new( StringComparer.Ordinal );
        foreach ( var provider in manifest.SearchProviders! )
        {
            if ( provider is null )
            {
                Invalid( "A search provider is missing." );
            }
            ViewValidation.ValidateId( provider!.Id );
            ViewValidation.ValidateId( provider.CacheKey );
            Label( provider.Title, 128 );
            if ( !ids.Add( provider.Id ) || !manifest.Commands.Any( command => command.Id == provider.CommandId ) ||
                provider.SuggestedPrefix is not null && (provider.SuggestedPrefix.Length is < 2 or > 20 || !provider.SuggestedPrefix.All( char.IsAsciiLetter )) )
            {
                Invalid( "Invalid search provider command or suggested prefix." );
            }
        }
    }

    private static void ValidateTargets( Dictionary<string, ExtensionTarget> targets )
    {
        foreach ( var (rid, target) in targets )
        {
            if ( rid is not (ExtensionTargets.LinuxX64 or ExtensionTargets.LinuxArm64) || target is null ||
                 target.Deployment is not (ExtensionTargets.NativeAot or ExtensionTargets.SelfContained) || target.RuntimeProfile != RuntimeProfile )
            {
                Invalid( "The extension deployment target is unsupported." );
            }
            ValidatePackagePath( target!.Entrypoint );
            if ( !target.Entrypoint.StartsWith( "bin/" + rid + "/", StringComparison.Ordinal ) )
            {
                Invalid( "Entrypoints must be under their target's bin directory." );
            }
        }
    }

    private static void ValidateCapabilities( Dictionary<string, CapabilityRequest> capabilities )
    {
        foreach ( var (capability, request) in capabilities )
        {
            if ( !KnownCapabilities.Contains( capability ) || request is null )
            {
                Invalid( "The capability is unsupported." );
            }
            Label( request!.Reason, 2048 );
        }
    }

    private static void ValidateCommands( ImmutableArray<ExtensionCommand> commands, Dictionary<string, CapabilityRequest> capabilities )
    {
        HashSet<string> ids = new( StringComparer.Ordinal );
        foreach ( var command in commands )
        {
            if ( command is null )
            {
                Invalid( "A command is missing." );
            }
            ViewValidation.ValidateId( command!.Id );
            if ( !ids.Add( command.Id ) || command.Mode is not (ExtensionCommandModes.View or ExtensionCommandModes.NoView) )
            {
                Invalid( "A command ID or mode is invalid." );
            }
            Label( command.Title, 512 );
            Label( command.Description, 2048, allowEmpty: true );
            Features( command.RequiredFeatures );
            if ( command.Icon is not null )
            {
                ValidatePackagePath( command.Icon );
            }
            if ( command.Keywords.IsDefault || command.Keywords.Length > 32 || command.Capabilities.IsDefault || command.Capabilities.Length > 16 )
            {
                Invalid( "A command exceeds its limits." );
            }
            foreach ( var keyword in command.Keywords! )
            {
                Label( keyword, 128 );
            }
            if ( command.Capabilities!.Distinct( StringComparer.Ordinal ).Count( ) != command.Capabilities.Length ||
                 command.Capabilities.Any( capability => capability is null || !capabilities.ContainsKey( capability ) ) )
            {
                Invalid( "A command requests an undeclared or duplicate capability." );
            }
        }
    }

    private static void ValidatePreferences( ImmutableArray<ExtensionPreference> preferences )
    {
        HashSet<string> ids = new( StringComparer.Ordinal );
        foreach ( var preference in preferences )
        {
            if ( preference is null )
            {
                Invalid( "A preference is missing." );
            }
            ViewValidation.ValidateId( preference!.Id );
            if ( !ids.Add( preference.Id ) || preference.Type is not ("text" or "boolean" or "number" or "choice" or "secret") ||
                 preference.MaximumLength is < 1 or > 8192 || preference.Choices.IsDefault || preference.Choices.Length > 100 ||
                 preference.Minimum > preference.Maximum )
            {
                Invalid( "A preference is invalid." );
            }
            Label( preference.Title, 512 );
            Label( preference.Description, 2048, allowEmpty: true );
            HashSet<string> choices = new( StringComparer.Ordinal );
            foreach ( var choice in preference.Choices! )
            {
                if ( choice is null || !choices.Add( choice.Value ) )
                {
                    Invalid( "A preference choice is invalid." );
                }
                Label( choice!.Value, 256 );
                Label( choice.Label, 512 );
            }
            if ( preference.Type == "secret" && preference.Default is not null )
            {
                Invalid( "Secret preferences cannot contain defaults." );
            }
            if ( preference.Default is { } value )
            {
                ValidatePreferenceValue( preference, value );
            }
        }
    }

    public static void ValidatePreferenceValue( ExtensionPreference preference, JsonElement value )
    {
        switch ( preference.Type )
        {
            case "boolean" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return;
            case "number" when value.ValueKind == JsonValueKind.Number && value.TryGetDouble( out var number ) && double.IsFinite( number ):
                if ( number < preference.Minimum || number > preference.Maximum )
                {
                    Invalid( "The preference is outside its range." );
                }
                return;
            case "text" or "choice" when value.ValueKind == JsonValueKind.String:
                var text = value.GetString( )!;
                Label( text, preference.MaximumLength, allowEmpty: !preference.Required );
                if ( preference.Type == "choice" && !preference.Choices.Any( choice => choice.Value == text ) )
                {
                    Invalid( "The preference choice is invalid." );
                }
                return;
            default:
                Invalid( "The preference value has the wrong type." );
                return;
        }
    }

    public static void ValidatePackagePath( string? path )
    {
        if ( path is not { Length: > 0 and <= 512 } || path.Contains( '\\', StringComparison.Ordinal ) || path.Contains( ':', StringComparison.Ordinal ) ||
             path.Any( char.IsControl ) || !path.IsNormalized( NormalizationForm.FormC ) ||
             path.Split( '/' ) is var parts && (parts.Length > 12 || parts.Any( part => part is "" or "." or ".." )) )
        {
            Invalid( "The package path is invalid." );
        }
    }

    public static void ValidateVersion( string? version )
    {
        if ( version is not { Length: > 0 and <= 64 } )
        {
            Invalid( "The package version is invalid." );
        }
        var match = VersionPattern( ).Match( version! );
        if ( !match.Success || match.Groups[4].Value.Split( '.' ).Any( part => part.Length > 1 && part[0] == '0' && part.All( char.IsAsciiDigit ) ) )
        {
            Invalid( "The package version must be SemVer." );
        }
    }

    private static void Features( ImmutableArray<string> features )
    {
        if ( features.IsDefault || features.Length > 16 || features.Distinct( StringComparer.Ordinal ).Count( ) != features.Length ||
             features.Any( feature => feature is null || !KnownFeatures.Contains( feature ) ) )
        {
            Invalid( "An extension feature is unsupported or repeated." );
        }
    }

    private static void Label( string? text, int bytes, bool allowEmpty = false )
    {
        if ( text is null || (!allowEmpty && string.IsNullOrWhiteSpace( text )) || Encoding.UTF8.GetByteCount( text ) > bytes || text.Any( char.IsControl ) )
        {
            Invalid( "A manifest label is invalid or too long." );
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid( string message ) => throw new InvalidDataException( message );
}
