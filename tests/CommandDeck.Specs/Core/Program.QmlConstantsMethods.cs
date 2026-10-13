using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CommandDeck.Specs;

public static partial class Program
{
    // Explicit exports keep the QML API reviewable; never reflect the whole assembly.
    private static readonly Type[] QmlConstantTypes =
    [
        typeof( ProtocolOperations ), typeof( ProtocolMessageTypes ),
        typeof( ResultKinds ), typeof( SourceIds ), typeof( ProviderIds ), typeof( ActionKinds ),
        typeof( SystemdUnitActionIds ), typeof( FileSelectionActionIds ),
        typeof( AgentLoginActionIds ), typeof( CommandDeck.ExtensionProtocol.ExtensionActionKinds ),
        typeof( ClipboardPresentationKinds ), typeof( ClipboardActionIds ),
        typeof( ClipboardHandlerStatuses ), typeof( ClipboardHandlerReasons ), typeof( ClipboardHandlerRanks ),
        typeof( ExtensionSessionStatuses ), typeof( CommandDeck.ExtensionProtocol.ExtensionFeatures ),
        typeof( CommandDeck.ExtensionProtocol.ExtensionViewKinds ),
        typeof( CommandDeck.ExtensionProtocol.ExtensionFormFieldKinds ),
        typeof( ExtensionManagementActions ), typeof( ExtensionConnectionKinds ),
        typeof( ExtensionCredentialKinds ), typeof( CommandDeck.ExtensionProtocol.ExtensionPermissionIds ),
        typeof( RuntimeHealthStates ), typeof( IndexWorkStates ), typeof( QueryFieldIds ), typeof( RankingFieldNames ),
        typeof( EditorKinds ), typeof( HeyOpenTargets ), typeof( UndoActions ), typeof( ResultMetadataKeys ), typeof( ShortcutStatuses ),
        typeof( AiAdapterKinds ), typeof( AiCredentialKinds ), typeof( AiLocality ),
        typeof( AiHealthStates ), typeof( AiSelectionStates ), typeof( AiAttachmentKinds ),
        typeof( AiGenerationOutcomes ), typeof( AiErrorCategories ),
        typeof( AiProviderSettingsSources ), typeof( AiProviderTestOutcomes ),
        typeof( AgentBackendIds ), typeof( AgentBackendKinds ),
        typeof( AgentTrustDurations ), typeof( AgentContextPolicies ),
        typeof( AgentAuthenticationStates ), typeof( AgentHealthStates ),
        typeof( AgentLoginMethods ), typeof( AgentConversationStates ),
        typeof( AgentTurnOutcomes ), typeof( AgentEventKinds ),
        typeof( AgentPendingRequestKinds ), typeof( AgentRequestDecisions ),
        typeof( AgentGrantStates ), typeof( AgentTranscriptRoles )
    ];

    private static string QmlConstantGroupName( Type type ) => type == typeof( ProtocolOperations )
        ? "Operations"
        : type == typeof( ProtocolMessageTypes ) ? "Messages" : type.Name;

    private static string BuildQmlProtocolConstants( )
    {
        StringBuilder script = new( );
        script.AppendLine( ".pragma library" );
        script.AppendLine( "// Generated from Core and ExtensionProtocol. Run scripts/generate-protocol-contract.sh; do not edit." );
        script.AppendLine( $"var Version = {ProtocolLimits.CurrentVersion};" );
        foreach ( (var name, var values) in QmlConstantGroups( ) )
        {
            script.AppendLine( );
            script.AppendLine( $"var {name} = Object.freeze({{" );
            var fields = values.OrderBy( static field => field.Name, StringComparer.Ordinal ).ToArray( );
            for ( var index = 0; index < fields.Length; index++ )
            {
                var field = fields[index];
                script.Append( "  " ).Append( field.Name ).Append( ": " ).Append( JsonSerializer.Serialize( field.Value ) );
                script.AppendLine( index + 1 < fields.Length ? "," : string.Empty );
            }
            script.AppendLine( "});" );
        }
        return script.ToString( );
    }

    private static Dictionary<string, (string Name, string Value)[]> QmlConstantGroups( )
    {
        var groups = QmlConstantTypes.ToDictionary( QmlConstantGroupName, ReadConstants, StringComparer.Ordinal );
        foreach ( var type in new[] { typeof( AgentPreferences ), typeof( SourcePreference ) } )
        {
            var group = type == typeof( AgentPreferences ) ? "AgentPreferenceFields" : "SourcePreferenceFields";
            groups.Add( group, ReadProperties( type )
                .Select( static name => (name, JsonNamingPolicy.CamelCase.ConvertName( name )) ).ToArray( ) );
        }
        return groups;
    }

    private static void QmlConstantsMatchCoreAndReferencesResolve( )
    {
        var directory = Path.Combine( FindRepositoryRoot( ), "qml", "common" );
        Equal( NormalizeArtifact( BuildQmlProtocolConstants( ) ),
            NormalizeArtifact( File.ReadAllText( Path.Combine( directory, "Protocol.js" ) ) ) );
        var groups = QmlConstantGroups( ).ToDictionary( static group => group.Key,
            static group => group.Value.Select( static field => field.Name ).ToHashSet( StringComparer.Ordinal ),
            StringComparer.Ordinal );
        Regex uiGroup = new( @"var (?<group>\w+) = Object\.freeze\(\{(?<fields>.*?)\}\);", RegexOptions.Singleline );
        Regex uiField = new( @"(?:^|[,\r\n])\s*(?<name>\w+)\s*:" );
        var uiGroups = uiGroup.Matches( File.ReadAllText( Path.Combine( directory, "UiConstants.js" ) ) )
            .ToDictionary( static match => match.Groups["group"].Value,
                match => uiField.Matches( match.Groups["fields"].Value ).Select( static field => field.Groups["name"].Value )
                    .ToHashSet( StringComparer.Ordinal ), StringComparer.Ordinal );
        var references = 0;
        foreach ( var file in EnumerateUiSources( directory ) )
        {
            references += ValidateUiConstants( file, File.ReadAllText( file ), groups, uiGroups );
        }
        True( references > 0, "Production QML/JS must consume the generated constants." );

        var specDirectory = Path.Combine( FindRepositoryRoot( ), "tests", "CommandDeck.QmlSpecs" );
        foreach ( var file in Directory.EnumerateFiles( specDirectory, "*.qml", SearchOption.AllDirectories ) )
        {
            ValidateSpecProtocolConstants( file, File.ReadAllText( file ) );
        }
        Throws<InvalidOperationException>( ( ) =>
            ValidateSpecProtocolConstants( "fixture.qml", "operation: 'invoke'" ) );
        Throws<InvalidOperationException>( ( ) =>
            ValidateSpecProtocolConstants( "fixture.qml", "type: 'action-result'" ) );

        foreach ( var extension in new[] { ".qml", ".js" } )
        {
            var import = extension == ".js" ? ".import" : "import";
            var prefix = $"{import} \"Protocol.js\" as Protocol\n";
            Equal( 1, ValidateUiConstants( "fixture" + extension, prefix + "Protocol.Operations.Search", groups, uiGroups ) );
            foreach ( var invalid in new[] { prefix + "Protocol.Operations.DoesNotExist", "Protocol.Operations.Search",
                "operation: 'search'", "version: 1", "Ui.DoesNotExist.Value" } )
            {
                Throws<InvalidOperationException>( ( ) => ValidateUiConstants( "fixture" + extension, invalid, groups, uiGroups ) );
            }
        }
    }

    private static IEnumerable<string> EnumerateUiSources( string directory ) =>
        Directory.EnumerateFiles( directory, "*", SearchOption.AllDirectories ).Where( IsUiSource );

    private static bool IsUiSource( string path ) => Path.GetExtension( path ) is ".qml" or ".js";

    private static void ValidateSpecProtocolConstants( string file, string source )
    {
        var operations = ReadConstants( typeof( ProtocolOperations ) ).Select( static item => item.Value ).ToHashSet( StringComparer.Ordinal );
        var messages = ReadConstants( typeof( ProtocolMessageTypes ) ).Select( static item => item.Value ).ToHashSet( StringComparer.Ordinal );
        Regex wireValue = new( "\\b(?<field>operation|type)\\s*(?::|===|==|!==|!=)\\s*[\\\"'](?<value>[^\\\"']+)[\\\"']" );
        foreach ( Match match in wireValue.Matches( source ) )
        {
            var values = match.Groups["field"].Value == "operation" ? operations : messages;
            True( !values.Contains( match.Groups["value"].Value ),
                $"{Path.GetFileName( file )} must use the generated protocol constant for {match.Groups["value"].Value}." );
        }

        if ( source.Contains( "Protocol.", StringComparison.Ordinal ) )
        {
            True( Regex.IsMatch( source, "^import \\\"(?:[^\\\"]*/)?Protocol\\.js\\\" as Protocol$", RegexOptions.Multiline ),
                $"{Path.GetFileName( file )} must import generated protocol constants." );
        }
    }

    private static int ValidateUiConstants( string file, string text,
        Dictionary<string, HashSet<string>> groups, Dictionary<string, HashSet<string>> uiGroups )
    {
        Regex reference = new( @"Protocol\.(?<group>\w+)\.(?<name>\w+)" );
        Regex uiReference = new( @"Ui\.(?<group>\w+)\.(?<name>\w+)" );
        Regex wireLiteral = new( "(?:operation\\s*:|(?:message\\.type|value\\.type|type)\\s*[!=]==)\\s*[\"']" );
        Regex versionLiteral = new( @"\bversion\s*:\s*\d+" );
        var import = Path.GetExtension( file ) == ".js" ? @"^\s*\.import" : @"^\s*import";
        var references = 0;

        True( !reference.IsMatch( text ) || Regex.IsMatch( text, import + "\\s+\"Protocol\\.js\"\\s+as\\s+Protocol\\b", RegexOptions.Multiline ),
            $"{Path.GetFileName( file )} must import its protocol constants." );
        True( !uiReference.IsMatch( text ) || Regex.IsMatch( text, import + "\\s+\"UiConstants\\.js\"\\s+as\\s+Ui\\b", RegexOptions.Multiline ),
            $"{Path.GetFileName( file )} must import its UI constants." );
        True( !wireLiteral.IsMatch( text ), $"{Path.GetFileName( file )} must use named wire constants." );
        True( !versionLiteral.IsMatch( text ), $"{Path.GetFileName( file )} must use Protocol.Version." );
        foreach ( Match match in uiReference.Matches( text ) )
        {
            True( uiGroups.TryGetValue( match.Groups["group"].Value, out var names ) && names.Contains( match.Groups["name"].Value ),
                $"Unresolved UI constant {match.Value} in {Path.GetFileName( file )}." );
        }
        foreach ( Match match in reference.Matches( text ) )
        {
            True( groups.TryGetValue( match.Groups["group"].Value, out var names ) && names.Contains( match.Groups["name"].Value ),
                $"Unresolved QML constant {match.Value} in {Path.GetFileName( file )}." );
            references++;
        }

        return references;
    }
}
