using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly JsonSerializerOptions ContractJsonOptions = new( )
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static void GeneratedProtocolContractMatchesCurrentTree( )
    {
        var root = FindRepositoryRoot( );
        (var json, var markdown) = BuildProtocolContractArtifacts( );
        Equal(
            NormalizeArtifact( json ),
            NormalizeArtifact( File.ReadAllText( Path.Combine(
                root,
                "tests/fixtures/protocol/current-contract.json" ) ) ) );
        Equal(
            NormalizeArtifact( markdown ),
            NormalizeArtifact( File.ReadAllText( Path.Combine(
                root,
                "docs/generated/current-protocol-contract.md" ) ) ) );
    }

    // Every operation constant has exactly one route, except cancel, which the
    // connection serves before admission. Only search replaces a search, and
    // only the requests that create, consume, or purge client AI state wait at
    // the AI barrier that ai.clear raises.
    private static void ProtocolRoutesCoverEveryOperationOnce( )
    {
        var operations = ReadConstants( typeof( ProtocolOperations ) )
            .Select( static value => value.Value )
            .Order( StringComparer.Ordinal );
        var routed = ProtocolRoutes.All.Keys
            .Append( ProtocolOperations.Cancel )
            .Order( StringComparer.Ordinal );
        Equal( string.Join( ',', operations ), string.Join( ',', routed ) );
        True( !ProtocolRoutes.TryResolve( ProtocolOperations.Cancel, out _ ), "cancel is served before admission" );

        string[] Admitted( ProtocolAdmission admission ) => [.. ProtocolRoutes.All
            .Where( pair => pair.Value.Admission == admission )
            .Select( static pair => pair.Key )
            .Order( StringComparer.Ordinal )];
        Equal( ProtocolOperations.Search, string.Join( ',', Admitted( ProtocolAdmission.ReplacesSearch ) ) );
        Equal( ProtocolOperations.AiClear, string.Join( ',', Admitted( ProtocolAdmission.AiClearBarrier ) ) );
        Equal(
            string.Join( ',', new[] { ProtocolOperations.AiOutput, ProtocolOperations.AiPreflight, ProtocolOperations.AiStart }
                .Order( StringComparer.Ordinal ) ),
            string.Join( ',', Admitted( ProtocolAdmission.AiOrdered ) ) );
        True( ProtocolRoutes.All.Values.All( static route => Enum.IsDefined( route.Family ) ) );
    }

    private static void WriteProtocolContractArtifacts( string root )
    {
        (var json, var markdown) = BuildProtocolContractArtifacts( );
        var fixturePath = Path.Combine( root, "tests/fixtures/protocol/current-contract.json" );
        var docsPath = Path.Combine( root, "docs/generated/current-protocol-contract.md" );
        Directory.CreateDirectory( Path.GetDirectoryName( fixturePath )! );
        Directory.CreateDirectory( Path.GetDirectoryName( docsPath )! );
        File.WriteAllText( fixturePath, json );
        File.WriteAllText( docsPath, markdown );
        File.WriteAllText( Path.Combine( root, "qml/common/Protocol.js" ), BuildQmlProtocolConstants( ) );
    }

    private static (string Json, string Markdown) BuildProtocolContractArtifacts( )
    {
        var operations = ReadConstants( typeof( ProtocolOperations ) );
        var messageTypes = ReadConstants( typeof( ProtocolMessageTypes ) );
        var requestFields = ReadProperties( typeof( ProtocolRequest ) );
        var responseFields = ReadProperties( typeof( ProtocolMessage ) );
        object[] filters = SearchFilterRegistry.All.Select( static value => new
        {
            value.Id,
            value.Label,
            value.Token,
            aliases = value.Aliases.Order( StringComparer.Ordinal ).ToArray( ),
            value.ProviderId,
            value.SourceId,
            value.Kind
        } ).ToArray( );
        object[] sources = LauncherSourceRegistry.Configurable.Select( static value => new
        {
            value.Id,
            value.Label,
            value.Icon,
            value.HomeAvailable
        } ).ToArray( );
        var json = JsonSerializer.Serialize( new
        {
            version = ProtocolLimits.CurrentVersion,
            operations = operations.Select( static value => value.Value ).ToArray( ),
            messageTypes = messageTypes.Select( static value => value.Value ).ToArray( ),
            requestFields,
            responseFields,
            searchCompletionFields = ReadProperties( typeof( SearchCompletion ) ),
            queryExpressionFields = ReadProperties( typeof( QueryExpression ) ),
            rankingFields = ReadProperties( typeof( RankingExplanation ) ),
            placementFields = ReadProperties( typeof( RankingPlacement ) ),
            filters,
            sources
        }, ContractJsonOptions ) + Environment.NewLine;

        StringBuilder markdown = new( );
        markdown.AppendLine( "# Generated current protocol contract" );
        markdown.AppendLine( );
        markdown.AppendLine(
            "Generated from Core protocol constants and registries. Run " +
            "`scripts/generate-protocol-contract.sh` after an intentional contract change." );
        markdown.AppendLine( );
        AppendValues( markdown, "Operations", operations.Select( static value => value.Value ) );
        AppendValues( markdown, "Message types", messageTypes.Select( static value => value.Value ) );
        AppendValues( markdown, "Request fields", requestFields );
        AppendValues( markdown, "Response fields", responseFields );
        AppendValues( markdown, "Search completion fields", ReadProperties( typeof( SearchCompletion ) ) );
        AppendValues( markdown, "Query expression fields", ReadProperties( typeof( QueryExpression ) ) );
        AppendValues( markdown, "Ranking explanation fields", ReadProperties( typeof( RankingExplanation ) ) );
        AppendValues( markdown, "Ranking placement fields", ReadProperties( typeof( RankingPlacement ) ) );
        markdown.AppendLine( "## Search filters" );
        markdown.AppendLine( );
        markdown.AppendLine( "| ID | Token | Aliases |" );
        markdown.AppendLine( "|---|---|---|" );
        foreach ( var filter in SearchFilterRegistry.All )
        {
            markdown.Append( "| `" ).Append( filter.Id ).Append( "` | `" )
                .Append( filter.Token ).Append( "` | " )
                .Append( filter.Aliases.Count == 0
                    ? "—"
                    : string.Join( ", ", filter.Aliases.Select( static value => $"`{value}`" ) ) )
                .AppendLine( " |" );
        }

        markdown.AppendLine( );
        markdown.AppendLine( "## Configurable sources" );
        markdown.AppendLine( );
        foreach ( var source in LauncherSourceRegistry.Configurable )
        {
            markdown.Append( "- `" ).Append( source.Id ).Append( "` — " )
                .AppendLine( source.Label );
        }

        return (json, markdown.ToString( ));
    }

    private static (string Name, string Value)[] ReadConstants( Type type ) =>
        type.GetFields( BindingFlags.Public | BindingFlags.Static )
            .Where( static field => field.IsLiteral && field.FieldType == typeof( string ) )
            .Select( static field => (
                field.Name,
                ( string ) field.GetRawConstantValue( )!) )
            .OrderBy( static value => value.Item2, StringComparer.Ordinal )
            .ToArray( );

    private static string[] ReadProperties( Type type ) =>
        type.GetProperties( BindingFlags.Public | BindingFlags.Instance )
            .Select( static property => property.Name )
            .Order( StringComparer.Ordinal )
            .ToArray( );

    private static void AppendValues(
        StringBuilder markdown,
        string heading,
        IEnumerable<string> values )
    {
        markdown.Append( "## " ).AppendLine( heading );
        markdown.AppendLine( );
        foreach ( var value in values )
        {
            markdown.Append( "- `" ).Append( value ).AppendLine( "`" );
        }

        markdown.AppendLine( );
    }

    private static string NormalizeArtifact( string value ) =>
        value.Replace( "\r\n", "\n", StringComparison.Ordinal ).TrimEnd( );
}
