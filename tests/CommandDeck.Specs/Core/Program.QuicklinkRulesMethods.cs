using System.Text;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static string BuildQuicklinkRules( )
    {
        StringBuilder script = new( ".pragma library\n// Generated from QuicklinkValidator, AliasValidator and SearchFilterRegistry. Do not edit.\n" );
        foreach ( var (name, value) in new[]
        {
            ("titleMessage", QuicklinkValidator.TitleMessage),
            ("urlLengthMessage", QuicklinkValidator.UrlLengthMessage),
            ("urlMessage", QuicklinkValidator.UrlMessage),
            ("iconMessage", QuicklinkValidator.IconMessage),
            ("aliasPattern", AliasValidator.Pattern),
            ("aliasMessage", AliasValidator.InvalidMessage),
            ("reservedAliasMessage", AliasValidator.ReservedMessage)
        } )
        {
            script.Append( "var " ).Append( name ).Append( " = " ).Append( JsonSerializer.Serialize( value ) ).AppendLine( );
        }
        script.Append( "var reservedAliases = [" );
        script.AppendJoin( ", ", SearchFilterRegistry.ReservedAliases.Order( StringComparer.Ordinal )
            .Select( static alias => JsonSerializer.Serialize( alias ) ) );
        script.AppendLine( "]" );
        return script.ToString( );
    }

    private static void GeneratedQuicklinkRulesMatchValidators( ) => Equal(
        NormalizeArtifact( BuildQuicklinkRules( ) ),
        NormalizeArtifact( File.ReadAllText( Path.Combine( FindRepositoryRoot( ), "qml/common/QuicklinkRules.js" ) ) ) );
}
