using System.Collections.Immutable;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

await ExtensionApp.RunAsync( args, new UuidsCommand( ) );

sealed class UuidsCommand : IExtensionCommand
{
    private string[] _values = [];
    private int _page;
    private int _generation;
    public string Id => "uuids";

    public ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
    {
        _values = Enumerable.Range( 0, 24 ).Select( _ => Guid.NewGuid( ).ToString( ) ).ToArray( );
        _generation++;
        _page = 0;
        return ShowPageAsync( context, cancellationToken );
    }

    private ValueTask ShowPageAsync( CommandContext context, CancellationToken cancellationToken, string? error = null )
    {
        return context.Views.ReplaceAsync( new ListView
        {
            Id = "uuids",
            Title = $"UUIDs · Page {_page + 1} of 3",
            Search = new SearchField { Mode = SearchMode.Local },
            Error = error,
            PreviousPage = _page > 0 ? $"{_generation}:{_page - 1}" : null,
            NextPage = _page < 2 ? $"{_generation}:{_page + 1}" : null,
            Items = _values.Skip( _page * 8 ).Take( 8 ).Select( value => new ListItem
            {
                Id = value,
                Title = value,
                Actions = [ViewAction.CopyText( "copy", "Copy UUID", value ), ViewAction.Callback( "details", "UUID details" )]
            } ).ToImmutableArray( ),
            Actions = [ViewAction.Callback( "regenerate", "Generate again" )]
        }, cancellationToken );
    }

    public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken )
    {
        if ( commandEvent is ActionInvoked { ActionId: "regenerate" } )
        {
            return OpenAsync( context, cancellationToken );
        }
        if ( commandEvent is ActionInvoked { ActionId: "details", ItemId: { } value } && _values.Contains( value, StringComparer.Ordinal ) )
        {
            return context.Views.PushAsync( new DetailView
            {
                Id = "uuid-details",
                Title = "UUID details",
                Body = $"**UUID**\n`{value}`\n\nGenerated locally using .NET.",
                Metadata = [new( "Version", "4 (random)" ), new( "Format", "8-4-4-4-12 hexadecimal digits" )],
                Actions = [ViewAction.CopyText( "copy", "Copy UUID", value )]
            }, cancellationToken );
        }
        if ( commandEvent is PageRequested page )
        {
            var parts = page.Token.Split( ':' );
            if ( parts.Length != 2 || !int.TryParse( parts[0], out var generation ) || generation != _generation ||
                 !int.TryParse( parts[1], out var index ) || index is < 0 or > 2 )
            {
                return ShowPageAsync( context, cancellationToken, "This page has expired. Generate again." );
            }
            _page = index;
            return ShowPageAsync( context, cancellationToken );
        }
        return ValueTask.CompletedTask;
    }
}
