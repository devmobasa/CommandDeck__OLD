using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionCopyEffectsRequireCurrentInvocation( )
    {
        ExtensionViewAuthority authority = new( "session", 2, 7 );
        var request = new CopyTextRequest( "session", 2, 1, "invocation", "copied" );
        HashSet<string> grants = ["clipboard.write"];
        Throws<InvalidOperationException>( ( ) => authority.AdmitCopyEffect( request, 7, grants ) );
        var invocation = authority.BeginInvocation( InvocationOrigin.Open );
        request = request with { InvocationId = invocation.Id };
        Throws<InvalidOperationException>( ( ) => authority.AdmitCopyEffect( request with { SessionId = "other" }, 7, grants ) );
        Throws<InvalidOperationException>( ( ) => authority.AdmitCopyEffect( request with { ExecutionEpoch = 3 }, 7, grants ) );
        Throws<InvalidOperationException>( ( ) => authority.AdmitCopyEffect( request with { InvocationId = "other" }, 7, grants ) );
        Throws<InvalidOperationException>( ( ) => authority.AdmitCopyEffect( request, 8, grants ) );
        Throws<InvalidOperationException>( ( ) => authority.AdmitCopyEffect( request, 7, new HashSet<string>( ) ) );
        authority.AdmitCopyEffect( request, 7, grants );
        Throws<InvalidOperationException>( ( ) => authority.AdmitCopyEffect( request, 7, grants ) );
        var nextInvocation = authority.BeginInvocation( InvocationOrigin.UserAction );
        Throws<InvalidOperationException>( ( ) => authority.AdmitCopyEffect( request, 7, grants ) );
        authority.CompleteInvocation( nextInvocation );
        Throws<InvalidOperationException>( ( ) => authority.AdmitCopyEffect( request with { Generation = 2 }, 7, grants ) );
        authority.BeginInvocation( InvocationOrigin.UserAction );
        authority.Revoke( );
        Throws<InvalidOperationException>( ( ) => authority.AdmitCopyEffect( request with { Generation = 3 }, 7, grants ) );
    }

    private static void ExtensionUrlsBindPresentedTargetsAndGrants( )
    {
        ExtensionViewAuthority authority = new( "session", 2, 7 );
        var invocation = authority.BeginInvocation( InvocationOrigin.Open );
        ViewUpdate Update( string url, long revision ) => new( "session", 2, "detail", revision, 1,
            new DetailView { Id = "detail", Title = "Docs", Actions = [ViewAction.OpenUrl( "docs", "Open docs", url )] } );
        var first = authority.Replace( Update( "https://example.com/first", 0 ) );
        True( !first.Content.GetRawText( ).Contains( "example.com", StringComparison.Ordinal ) );
        authority.Present( 1, first.Revision );
        var handle = first.Content.GetProperty( "actions" )[0].GetProperty( "handle" ).GetString( )!;
        var denied = authority.AdmitAction( 2, first.Revision, handle, null );
        Throws<InvalidOperationException>( ( ) => authority.BeginAction( denied, 7, new HashSet<string>( ) ) );
        var accepted = authority.AdmitAction( 3, first.Revision, handle, null );
        authority.Replace( Update( "https://example.com/second", 1 ) );
        Equal( "https://example.com/first", (( EffectBinding ) authority.BeginAction( accepted, 7, new HashSet<string> { "desktop.open-url" } )).Action.Url );
        Throws<InvalidOperationException>( ( ) => authority.BeginAction( accepted, 7, new HashSet<string> { "desktop.open-url" } ) );
    }

    private static async Task ExtensionSdkNoViewCopyRetiresContextAsync( )
    {
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource lifetime = new( TimeSpan.FromSeconds( 5 ) );
        List<CopyTextRequest> effects = [];
        await using ExtensionPeer host = new( pipes.Host, pipes.Host, true, ["effects.copy-text"], ( frame, _ ) =>
        {
            effects.Add( frame.Params!.Value.Deserialize( ExtensionViewJsonContext.Default.CopyTextRequest )! );
            return ValueTask.FromResult( JsonElement.Parse( "{}" ) );
        } );
        host.Start( );
        CopySdkCommand command = new( );
        var sdk = ExtensionApp.RunAsync( pipes.Child, pipes.Child, [command], lifetime.Token );
        try
        {
            var initialize = new InitializeRequest( new( ), "session", 2, "copy", "interactive", ["commands@1", "commands.no-view@1", "effects.copy-text@1"], new( ) );
            await host.CallAsync( "initialize", JsonSerializer.SerializeToElement( initialize, ExtensionJsonContext.Default.InitializeRequest ), lifetime.Token );
            host.Notify( "initialized" );
            Equal( 0, effects.Count );
            var result = await host.CallAsync( "command.open", JsonSerializer.SerializeToElement( new CommandOpenRequest( 1, "invocation", [] ), ExtensionViewJsonContext.Default.CommandOpenRequest ), lifetime.Token );
            Equal( "completed", result.Deserialize( ExtensionViewJsonContext.Default.CommandOutcome )!.Outcome );
            Equal( new CopyTextRequest( "session", 2, 1, "invocation", "copied" ), effects.Single( ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await command.Context!.Effects.CopyTextAsync( "late", lifetime.Token ) );
            Equal( 1, effects.Count );
        }
        finally
        {
            await lifetime.CancelAsync( );
            try
            {
                await sdk;
            }
            catch ( OperationCanceledException ) { }
        }
    }

    private sealed class CopySdkCommand : IExtensionCommand
    {
        public string Id => "copy";
        public string Mode => "no-view";
        public CommandContext? Context { get; private set; }
        public ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            Context = context;
            return context.Effects.CopyTextAsync( "copied", cancellationToken );
        }
        public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
    }
}
