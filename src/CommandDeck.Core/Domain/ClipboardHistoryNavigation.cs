namespace CommandDeck;

public static class ClipboardHistoryNavigation
{
    public const string Id = "navigation:clipboard-history";

    public static IndexedItem CreateItem( ) => new( )
    {
        StableId = Id,
        Kind = ResultKinds.CustomCommand,
        Title = "Clipboard History",
        Subtitle = "Browse retained clipboard entries",
        Icon = "edit-paste",
        ProviderId = ProviderIds.Commands,
        SourceId = SourceIds.CommandsQuicklinks
    };

    public static CommandAction CreateAction( ) => new( )
    {
        Id = "open",
        Title = "Open Clipboard History",
        Kind = ActionKinds.OpenClipboardHistory
    };
}
