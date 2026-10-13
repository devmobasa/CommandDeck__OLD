using System.Text.Json.Serialization;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions.Views;

[JsonConverter( typeof( ViewActionConverter<ViewAction> ) )]
public abstract record ViewAction
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Kind => this switch
    {
        CallbackAction => ExtensionActionKinds.Callback,
        CopyTextAction => ExtensionActionKinds.CopyText,
        OpenUrlAction => ExtensionActionKinds.OpenUrl,
        _ => throw new InvalidOperationException( "Unsupported action type." )
    };
    public string? Text => this is CopyTextAction copy ? copy.Text : null;
    public string? Url => this is OpenUrlAction open ? open.Url : null;

    public static CopyTextAction CopyText( string id, string title, string text ) => new( ) { Id = id, Title = title, Text = text };
    public static CallbackAction Callback( string id, string title ) => new( ) { Id = id, Title = title };
    public static OpenUrlAction OpenUrl( string id, string title, string url ) => new( ) { Id = id, Title = title, Url = url };
}

[JsonConverter( typeof( ViewActionConverter<CallbackAction> ) )]
public sealed record CallbackAction : ViewAction;

[JsonConverter( typeof( ViewActionConverter<CopyTextAction> ) )]
public sealed record CopyTextAction : ViewAction
{
    public required new string Text { get; init; }
}

[JsonConverter( typeof( ViewActionConverter<OpenUrlAction> ) )]
public sealed record OpenUrlAction : ViewAction
{
    public required new string Url { get; init; }
}
