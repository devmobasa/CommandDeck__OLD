using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Testing;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionAuthorKitExercisesRealSdkAsync( )
    {
        AuthorKitCommand command = new( );
        var options = new ExtensionTestHostOptions
        {
            Files = new( ) { ["notes"] = new( ) { ["one.txt"] = Encoding.UTF8.GetBytes( "hello λ" ) } },
            Http = new( ) { ["tasks"] = _ => new( 201, "text/plain", Encoding.UTF8.GetBytes( "created" ) ) }
        };
        await using var host = await ExtensionTestHost.StartAsync( command, options );
        await host.OpenAsync( );
        Equal( "hello λ", command.ReadText );
        Equal( "created", command.HttpText );
        Equal( "one.txt", command.FileName );
        await host.InvokeAsync( "push" );
        Equal( "form", host.CurrentView!.Id );
        await host.SubmitAsync( new Dictionary<string, JsonElement> { ["title"] = JsonElement.Parse( "\"saved\"" ) } );
        Equal( "saved", command.SavedText );
        Equal( 0, command.WriteReturnedValueCount );
        True( command.Conflict );
        await host.BackAsync( );
        await host.NextPageAsync( );
        Equal( "next-token", command.PageToken );
        await host.InvokeAsync( "copy" );
        Equal( "copied", host.CopiedText.Single( ) );

        await using var denied = await ExtensionTestHost.StartAsync( new AuthorKitCommand( ), options with { GrantedPermissions = new HashSet<string>( ) } );
        await ThrowsAsync<ExtensionRpcException>( async ( ) => await denied.OpenAsync( ) );
        await using var unsupported = await ExtensionTestHost.StartAsync( new SessionViewCommand( ), options with
        { HostFeatures = new HashSet<string> { ExtensionFeatures.Commands } } );
        await ThrowsAsync<ExtensionRpcException>( async ( ) => await unsupported.OpenAsync( ) );
    }

    private sealed class AuthorKitCommand : ExtensionCommandBase
    {
        public override string Id => "kit";
        public string? FileName { get; private set; }
        public string? ReadText { get; private set; }
        public string? HttpText { get; private set; }
        public string? SavedText { get; private set; }
        public string? PageToken { get; private set; }
        public bool Conflict { get; private set; }
        public int WriteReturnedValueCount { get; private set; }
        public override async ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            var listing = await context.Brokers.ListAsync( "notes", cancellationToken: cancellationToken );
            FileName = listing.Entries.Single( ).Name;
            ReadText = Encoding.UTF8.GetString( (await context.Brokers.ReadAsync( "notes", FileName, cancellationToken )).Bytes.Span );
            HttpText = Encoding.UTF8.GetString( (await context.Brokers.SendHttpAsync( "tasks", new( "GET", "/" ), cancellationToken )).Body.Span );
            await context.Views.ReplaceAsync( new ListView
            {
                Id = "list",
                Title = "List",
                NextPage = "next-token",
                Actions = [ViewAction.Callback( "push", "Push" ), ViewAction.CopyText( "copy", "Copy", "copied" )]
            }, cancellationToken );
        }
        protected override ValueTask OnActionAsync( CommandContext context, ActionInvoked action, CancellationToken cancellationToken ) =>
            context.Views.PushAsync( new FormView
            {
                Id = "form",
                Title = "Form",
                Fields = [FormField.Text( "title", "Title", true )],
                Submit = ViewAction.Callback( "save", "Save" )
            }, cancellationToken );
        protected override async ValueTask OnSubmitAsync( CommandContext context, FormSubmitted submission, CancellationToken cancellationToken )
        {
            var saved = await context.Data.WriteAsync( "draft", submission.Values["title"], 0, cancellationToken );
            WriteReturnedValueCount = saved.Values.Count;
            SavedText = (await context.Data.ReadAsync( cancellationToken )).Values["draft"].GetString( );
            try
            { await context.Data.WriteAsync( "draft", JsonElement.Parse( "\"stale\"" ), 0, cancellationToken ); }
            catch ( ExtensionRpcException exception ) when ( exception.IsConflict ) { Conflict = true; }
        }
        protected override ValueTask OnPageAsync( CommandContext context, PageRequested page, CancellationToken cancellationToken )
        {
            PageToken = page.Token;
            return ValueTask.CompletedTask;
        }
    }

    private static async Task ExtensionSdkLogsBoundedAuthorErrorsAsync( )
    {
        using StringWriter diagnostics = new( );
        SessionViewCommand command = new( ) { Handler = ( _, _, _ ) => throw new InvalidOperationException( new string( 'λ', 10000 ) ) };
        await using var harness = await ExtensionSessionHarness.CreateAsync( command, diagnostics: diagnostics );
        await harness.OpenAsync( );
        await harness.ActAsync( "Update" );
        var logged = diagnostics.ToString( );
        True( logged.StartsWith( "[commanddeck-sdk] session HandleAsync: InvalidOperationException:", StringComparison.Ordinal ) );
        True( Encoding.UTF8.GetByteCount( logged ) is > 0 and <= 4096 );
        Equal( "The extension couldn't complete that action.", harness.Latest.Error );
        True( !harness.Latest.Error!.Contains( 'λ' ) );
    }

    private static async Task ExtensionSdkReturnsTypedBrokerOutcomesAsync( )
    {
        foreach ( var outcome in new[] { ExtensionBrokerOutcomes.Unknown, ExtensionBrokerOutcomes.Cancelled,
            ExtensionBrokerOutcomes.RedirectDenied, ExtensionBrokerOutcomes.ConnectionUnavailable, ExtensionBrokerOutcomes.CredentialEchoDenied } )
        {
            var attempts = 0;
            BrokerOutcomeCommand command = new( );
            await using var host = await ExtensionTestHost.StartAsync( command, new( )
            {
                Http = new( ) { ["service"] = _ => { attempts++; throw new BrokerOutcomeException( outcome ); } }
            } );
            await host.OpenAsync( );
            Equal( outcome, command.Error!.Outcome );
            Equal( outcome == ExtensionBrokerOutcomes.Unknown, command.Error.IsUnknown );
            Equal( outcome == ExtensionBrokerOutcomes.Cancelled, command.Error.IsCancelled );
            Equal( outcome == ExtensionBrokerOutcomes.RedirectDenied, command.Error.IsRedirectDenied );
            Equal( 1, attempts );
        }
    }
    private sealed class BrokerOutcomeCommand : ExtensionCommandBase
    {
        public override string Id => "outcome";
        public BrokerOutcomeException? Error { get; private set; }
        public override async ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            try
            { await context.Brokers.SendHttpAsync( "service", new( "POST", "/", IdempotencyKey: "stable-effect-key" ), cancellationToken ); }
            catch ( BrokerOutcomeException exception ) { Error = exception; }
            await context.Views.ReplaceAsync( new DetailView { Id = "outcome", Title = "Outcome" }, cancellationToken );
        }
    }

    private static async Task ExtensionSdkReceivesHostDeadlineCodeAsync( )
    {
        DeadlineCopyCommand command = new( );
        await using var harness = await ExtensionSessionHarness.CreateAsync( command, [ExtensionPermissionIds.ClipboardWrite] );
        harness.Effects.BeforeExecute = async token => await Task.Delay( Timeout.Infinite, token );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 10 ) );
        await harness.OpenAsync( cancellationToken: deadline.Token );
        Equal( ExtensionErrorCodes.DeadlineExceeded, command.ErrorCode );
        Equal( ExtensionSessionStatuses.Completed, harness.Latest.Status );
    }
    private sealed class DeadlineCopyCommand : IExtensionCommand
    {
        public string Id => "deadline";
        public string Mode => ExtensionCommandModes.NoView;
        public string? ErrorCode { get; private set; }
        public async ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            try
            { await context.Effects.CopyTextAsync( "copy", cancellationToken ); }
            catch ( ExtensionRpcException exception ) { ErrorCode = exception.ErrorCode; }
        }
        public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
    }

    private static void ExtensionValidationReportsStructuralPaths( )
    {
        ListView list = new( ) { Id = "list", Title = "List", Items = [new ListItem { Id = "one", Title = "One" }] };
        Check( list with { Title = new string( 'λ', 513 ) }, "title", "too long" );
        Check( list with { Title = "secret\0" }, "title", "control character" );
        Check( list with { Items = [list.Items[0], list.Items[0]] }, "items[1].id", "duplicate ID" );
        Check( list with { Items = [list.Items[0] with { Actions = [ViewAction.OpenUrl( "open", "Open", "file:///secret" )] }] }, "items[0].actions[0].url", "invalid URL" );
        Check( list with { Items = default }, "items", "missing" );
        Check( list with { Items = [list.Items[0] with { Asset = "private-file" }] }, "items[0].asset", "unsupported kind" );
        Check( list with { Items = Enumerable.Range( 0, 101 ).Select( index => new ListItem { Id = "item" + index, Title = "Item" } ).ToImmutableArray( ) }, "items", "too many" );
        static void Check( ExtensionView view, string path, string reason )
        {
            try
            { ViewValidation.Validate( view ); throw new InvalidOperationException( "Expected rejection." ); }
            catch ( InvalidDataException exception )
            {
                var diagnostic = ( ExtensionViewValidationException ) exception.InnerException!;
                Equal( path, diagnostic.Path );
                Equal( reason, diagnostic.Reason );
                True( !exception.Message.Contains( "secret", StringComparison.Ordinal ) );
            }
        }
    }
}
