using System.Text;
using System.Collections.Immutable;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

await ExtensionApp.RunAsync( args, new NotesCommand( ), new TasksCommand( ) );

sealed class NotesCommand : IExtensionCommand
{
    public string Id => "notes";
    private ImmutableArray<FileEntry> _entries = [];
    public ValueTask OpenAsync( CommandContext context, CancellationToken token ) => ListAsync( context, 0, token );
    private async ValueTask ListAsync( CommandContext context, int offset, CancellationToken token )
    {
        try
        {
            var result = await context.Brokers.ListAsync( "notes", offset: offset, cancellationToken: token );
            _entries = result.Entries;
            await context.Views.ReplaceAsync( new ListView
            {
                Id = "notes",
                Title = "Selected notes",
                Items = _entries.Where( entry => !entry.IsDirectory ).Select( entry => new ListItem
                {
                    Id = entry.Reference,
                    Title = entry.Name,
                    Actions = [ViewAction.Callback( "read", "Read note" )]
                } ).ToImmutableArray( ),
                NextPage = result.NextOffset?.ToString( ),
                Actions = [ViewAction.Callback( "refresh", "Refresh" )]
            }, token );
        }
        catch ( Exception exception ) when (exception is ExtensionRpcException or BrokerOutcomeException)
        {
            await context.Views.ReplaceAsync( new ListView { Id = "notes", Title = "Select a notes root in Connections, then refresh", Items = [], Actions = [ViewAction.Callback( "refresh", "Refresh" )] }, token );
        }
    }
    public async ValueTask HandleAsync( CommandContext context, CommandEvent value, CancellationToken token )
    {
        if ( value is PageRequested page )
        {
            await ListAsync( context, int.Parse( page.Token ), token );
            return;
        }
        if ( value is ActionInvoked { ActionId: "refresh" } )
        {
            await ListAsync( context, 0, token );
            return;
        }
        if ( value is not ActionInvoked { ActionId: "read", ItemId: { } id } )
        {
            return;
        }
        try
        {
            var entry = _entries.Single( entry => entry.Reference == id );
            var result = await context.Brokers.ReadAsync( "notes", entry.Reference, token );
            await context.Views.PushAsync( new DetailView { Id = "note", Title = entry.Name, Body = new UTF8Encoding( false, true ).GetString( result.Bytes.Span ).Replace( "\r\n", "\n", StringComparison.Ordinal ) }, token );
        }
        catch ( Exception exception ) when ( exception is ExtensionRpcException or BrokerOutcomeException or DecoderFallbackException or InvalidDataException )
        {
            await context.Views.PushAsync( new DetailView { Id = "note-error", Title = "Note unavailable", Body = "The file changed, was revoked, or exceeds the UTF-8 text view's 64 KiB limit. Go Back and refresh." }, token );
        }
    }
}

sealed class TasksCommand : IExtensionCommand
{
    public string Id => "tasks";
    private long _revision;
    private string _key = Guid.NewGuid( ).ToString( "N" );
    private FormView? _form;
    public async ValueTask OpenAsync( CommandContext context, CancellationToken token )
    {
        var data = await context.Data.ReadAsync( token );
        _revision = data.Revision;
        if ( data.Values.TryGetValue( "pending", out var pending ) )
        {
            _key = pending.GetProperty( "key" ).GetString( )!;
            await FormAsync( context, pending.GetProperty( "title" ).GetString( )!, "A prior request may have completed. Check the task list before starting another request.", token );
        }
        else
        {
            await ListAsync( context, token );
        }
    }
    private static async ValueTask ListAsync( CommandContext context, CancellationToken token )
    {
        ImmutableArray<ListItem> items = [];
        var title = "Tasks";
        try
        {
            using var client = context.Brokers.CreateHttpClient( "tasks" );
            using var response = await client.GetAsync( "tasks", token );
            response.EnsureSuccessStatusCode( );
            using var json = JsonDocument.Parse( await response.Content.ReadAsStringAsync( token ) );
            items = json.RootElement.EnumerateArray( ).Take( 64 ).Select( ( value, index ) => new ListItem { Id = "task" + index, Title = value.GetProperty( "title" ).GetString( )! } ).ToImmutableArray( );
        }
        catch ( Exception exception ) when ( exception is ExtensionRpcException or HttpRequestException or JsonException )
        {
            title = "Tasks unavailable · check Connections, then refresh";
        }
        await context.Views.ReplaceAsync( new ListView
        {
            Id = "tasks",
            Title = title,
            Items = items,
            Actions = [ViewAction.Callback( "refresh", "Refresh" ), ViewAction.Callback( "new", "New task" )]
        }, token );
    }
    private async ValueTask FormAsync( CommandContext context, string title, string? error, CancellationToken token )
    {
        _form = new FormView
        {
            Id = "tasks",
            Title = "Create task",
            Fields = [FormField.Text( "title", "Title", true, 120 ) with { DefaultValue = string.IsNullOrEmpty( title ) ? null : JsonElement.Parse( JsonSerializer.Serialize( title, SampleJson.Default.String ) ) }],
            Submit = ViewAction.Callback( "create", "Create task" ),
            Actions = [ViewAction.Callback( "refresh", "View tasks" )],
            Error = error
        };
        await context.Views.ReplaceAsync( _form, token );
    }
    public async ValueTask HandleAsync( CommandContext context, CommandEvent value, CancellationToken token )
    {
        if ( value is ActionInvoked { ActionId: "refresh" } )
        {
            await ListAsync( context, token );
            return;
        }
        if ( value is ActionInvoked { ActionId: "new" } )
        {
            _key = Guid.NewGuid( ).ToString( "N" );
            await FormAsync( context, "", null, token );
            return;
        }
        if ( value is not FormSubmitted submit || _form is null )
        {
            return;
        }
        var title = submit.Values["title"].GetString( )!;
        var pending = JsonElement.Parse( "{\"key\":" + JsonSerializer.Serialize( _key, SampleJson.Default.String ) + ",\"title\":" + JsonSerializer.Serialize( title, SampleJson.Default.String ) + "}" );
        try
        {
            var saved = await context.Data.WriteAsync( "pending", pending, _revision, token );
            _revision = saved.Revision;
            var body = "{\"title\":" + JsonSerializer.Serialize( title, SampleJson.Default.String ) + "}";
            var result = await context.Brokers.SendHttpAsync( "tasks", new( "POST", "/tasks", Convert.ToBase64String( Encoding.UTF8.GetBytes( body ) ), "application/json", _key ), token );
            if ( result.StatusCode is >= 200 and < 300 )
            {
                var cleared = await context.Data.DeleteAsync( "pending", _revision, token );
                _revision = cleared.Revision;
                _form = _form with
                {
                    Title = "Task created",
                    Error = null,
                    ValidatedFormRevision = submit.FormRevision
                };
            }
            else
            {
                _form = _form with
                {
                    Error = "Request was rejected. Check the task list before creating another request. This request will not be retried automatically.",
                    ValidatedFormRevision = submit.FormRevision
                };
            }
        }
        catch ( BrokerOutcomeException exception )
        {
            _form = _form with
            {
                Error = "Outcome: " + exception.Outcome + ". Check the task list before creating another request. This request will not be retried automatically.",
                ValidatedFormRevision = submit.FormRevision
            };
        }
        catch ( ExtensionRpcException )
        {
            _form = _form with
            {
                Error = "Request unavailable. Check Connections and the task list before retrying.",
                ValidatedFormRevision = submit.FormRevision
            };
        }
        await context.Views.ReplaceAsync( _form, token );
    }
}

[System.Text.Json.Serialization.JsonSerializable( typeof( string ) )]
sealed partial class SampleJson : System.Text.Json.Serialization.JsonSerializerContext;
