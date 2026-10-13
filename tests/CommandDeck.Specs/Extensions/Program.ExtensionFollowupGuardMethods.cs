using System.Reflection;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionNavigationNamesRemainCallbacks( )
    {
        foreach ( var name in new[] { "page", "back", "submit" } )
        {
            ExtensionViewAuthority authority = new( "session", 1, 1 );
            authority.BeginInvocation( InvocationOrigin.Open );
            var view = authority.Replace( new( "session", 1, "list", 0, 1, new ListView
            {
                Id = "list",
                Title = "Callbacks",
                Actions = [ViewAction.Callback( name, name )],
                NextPage = "token"
            } ) );
            authority.Present( 1, view.Revision );
            var handle = view.Content.GetProperty( "actions" )[0].GetProperty( "handle" ).GetString( )!;
            var admitted = authority.AdmitAction( 2, view.Revision, handle, null );
            var binding = authority.BeginAction( admitted, 1, new HashSet<string>( ) );
            True( binding is CallbackBinding );
            Equal( name, (( CallbackBinding ) binding).ActionId );
        }
    }

    private static void ExtensionFeatureRequirementsMatchSupport( )
    {
        // These four features belong to other handshakes or are reserved, not interactive manifest requirements.
        string[] separate = [ExtensionFeatures.Search, ExtensionFeatures.DevelopmentDebug, ExtensionFeatures.ErrorCodes, ExtensionFeatures.RemoteSearch];
        var declared = typeof( ExtensionFeatures ).GetFields( BindingFlags.Public | BindingFlags.Static )
            .Where( field => field.IsLiteral && field.FieldType == typeof( string ) ).Select( field => ( string ) field.GetRawConstantValue( )! )
            .Except( separate, StringComparer.Ordinal ).ToHashSet( StringComparer.Ordinal );
        True( declared.SetEquals( ExtensionFeatures.Interactive ) );
        True( declared.SetEquals( ManifestValidation.KnownFeatures ) );
        True( declared.SetEquals( ExtensionSessionService.Features ) );
        foreach ( var feature in declared )
        {
            ManifestValidation.Validate( PackageFixture( ) with { RequiredFeatures = [feature] } );
        }
        foreach ( var feature in separate )
        {
            Throws<InvalidDataException>( ( ) => ManifestValidation.Validate( PackageFixture( ) with { RequiredFeatures = [feature] } ) );
        }
    }

    private static void ExtensionFormErrorsRetainSnapshots( )
    {
        const string json = """{"kind":"form","id":"form","title":"Form","fields":[{"id":"name","label":"Name","kind":"text"}],"submit":{"id":"save","kind":"callback","title":"Save"},"fieldErrors":{"name":"Required"}}""";
        var form = ( FormView ) JsonSerializer.Deserialize( json, ExtensionViewJsonContext.Default.ExtensionView )!;
        var changed = form with { FieldErrors = form.FieldErrors.SetItem( "name", "Different" ) };
        Equal( "Required", form.FieldErrors["name"] );
        Equal( "Different", changed.FieldErrors["name"] );
        var restored = ( FormView ) JsonSerializer.Deserialize( JsonSerializer.Serialize( ( ExtensionView ) form,
            ExtensionViewJsonContext.Default.ExtensionView ), ExtensionViewJsonContext.Default.ExtensionView )!;
        Equal( "Required", restored.FieldErrors["name"] );
        var missing = ( FormView ) JsonSerializer.Deserialize( json.Replace( ",\"fieldErrors\":{\"name\":\"Required\"}", "", StringComparison.Ordinal ),
            ExtensionViewJsonContext.Default.ExtensionView )!;
        ViewValidation.Validate( missing );
        Equal( 0, missing.FieldErrors.Count );
        var explicitNull = JsonSerializer.Deserialize( json.Replace( "{\"name\":\"Required\"}", "null", StringComparison.Ordinal ), ExtensionViewJsonContext.Default.ExtensionView )!;
        Throws<InvalidDataException>( ( ) => ViewValidation.Validate( explicitNull ) );
    }

    private static async Task ExtensionRemoteWritesRequireUserActivationAsync( )
    {
        await using BrokerTestServer server = new( );
        ActivatedRemoteWriteCommand command = new( );
        await using var harness = await ExtensionSessionHarness.CreateAsync( command, [ExtensionPermissionIds.HttpRequest] );
        await harness.Management.HandleAsync( new SetConnection( harness.Installation.Key, new( )
        {
            Name = "service",
            Kind = "http",
            Origin = server.Origin,
            PrivateNetwork = true,
            Route = "/tasks",
            Methods = ["GET", "POST"]
        }, harness.Registry.Document.Revision ), CancellationToken.None );
        var confirmations = 0;
        harness.OnPublish = ( state, token ) =>
        {
            if ( state.Confirmation is not null )
            {
                confirmations++;
                return harness.DecideAsync( state, true, token );
            }
            return ValueTask.CompletedTask;
        };

        await harness.OpenAsync( );
        True( command.OpenDenied );
        Equal( 0, confirmations );
        Equal( 0, server.Writes );
        await harness.ActAsync( "Write" );
        Equal( 1, confirmations );
        Equal( 1, server.Writes );
        True( command.Written );
    }

    private sealed class ActivatedRemoteWriteCommand : IExtensionCommand
    {
        public string Id => "remote-write";
        public bool OpenDenied { get; private set; }
        public bool Written { get; private set; }
        public async ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            await context.Views.ReplaceAsync( new DetailView
            {
                Id = "write",
                Title = "Remote write",
                Actions = [ViewAction.Callback( "write", "Write" )]
            }, cancellationToken );

            try
            {
                await WriteAsync( context, cancellationToken );
            }
            catch ( ExtensionRpcException exception ) when ( exception.IsPermissionDenied )
            {
                OpenDenied = true;
            }
        }

        public async ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken )
        {
            await WriteAsync( context, cancellationToken );
            Written = true;
        }

        private static ValueTask<HttpResult> WriteAsync( CommandContext context, CancellationToken cancellationToken ) =>
            context.Brokers.SendHttpAsync( "service", new( "POST", "/tasks", IdempotencyKey: "activated-write-key" ), cancellationToken );
    }
}
