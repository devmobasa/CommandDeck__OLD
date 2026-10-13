using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

await ExtensionApp.RunAsync( args, new ReferenceCommand( ), new CopyCommand( ) );

sealed class ReferenceCommand : IExtensionCommand
{
    public string Id => "reference";
    public ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken ) => context.Views.ReplaceAsync( new ListView
    {
        Id = "reference",
        Title = "UUID reference",
        Items = [new ListItem
        {
            Id = "uuid", Title = "UUID format", Subtitle = "View the packaged icon and specification", Asset = "assets/uuid.png",
            Actions = [ViewAction.Callback( "details", "Details" )]
        }]
    }, cancellationToken );

    public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) =>
        commandEvent is ActionInvoked { ActionId: "details" } ? context.Views.PushAsync( new DetailView
        {
            Id = "details",
            Title = "UUID specification",
            Asset = "assets/uuid.png",
            Body = "**UUIDs** identify values using 128 bits.\n\nThe icon is a packaged image. Opening the specification requires the desktop URL permission.",
            Actions = [ViewAction.OpenUrl( "spec", "Open specification", "https://www.rfc-editor.org/rfc/rfc9562.html" )]
        }, cancellationToken ) : ValueTask.CompletedTask;
}

sealed class CopyCommand : IExtensionCommand
{
    public string Id => "copy";
    public string Mode => "no-view";
    public ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken ) =>
        context.Effects.CopyTextAsync( Guid.NewGuid( ).ToString( ), cancellationToken );
    public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
}
