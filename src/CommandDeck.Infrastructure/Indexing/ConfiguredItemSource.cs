using System.Runtime.CompilerServices;

namespace CommandDeck;

public sealed class ConfiguredItemSource
{
    private readonly CommandDeckOptions _options;

    public ConfiguredItemSource( CommandDeckOptions options )
    {
        _options = options;
    }

    public async IAsyncEnumerable<IndexedItem> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken )
    {
        await Task.Yield( );

        cancellationToken.ThrowIfCancellationRequested( );
        yield return ClipboardHistoryNavigation.CreateItem( );
        foreach ( var item in ClipboardSequenceCommands.CreateItems( ) )
        {
            yield return item;
        }

        foreach ( var link in _options.Quicklinks )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            yield return CreateQuicklinkItem( link );
        }

        foreach ( var command in _options.Commands )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            yield return new IndexedItem
            {
                StableId = $"command:{command.Id}",
                Kind = ResultKinds.CustomCommand,
                Title = command.Title,
                Subtitle = command.Arguments.Count == 0
                    ? null
                    : string.Join( ' ', command.Arguments ),
                Icon = command.Icon ?? "utilities-terminal",
                Body = string.Join( ' ', command.Keywords.Concat( command.Arguments ) ),
                PayloadJson = PayloadWriter.Create(
                    ("argv", string.Join( '\u001f', command.Arguments )),
                    ("workingDirectory", command.WorkingDirectory),
                    ("confirm", command.RequiresConfirmation ? "true" : "false") ),
                ProviderId = ProviderIds.Commands
                ,
                SourceId = SourceIds.CommandsQuicklinks
            };
        }
    }

    public static IndexedItem CreateQuicklinkItem( QuicklinkOptions link )
    {
        return new IndexedItem
        {
            StableId = $"quicklink:{link.Id}",
            Kind = ResultKinds.Quicklink,
            Title = link.Title,
            Subtitle = string.IsNullOrWhiteSpace( link.Alias )
                ? link.UrlTemplate
                : $"{link.Alias} · {link.UrlTemplate}",
            Icon = link.Icon ?? "internet-web-browser",
            Body = $"{link.Alias} {link.UrlTemplate}",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(
                new UrlActionPayload( link.UrlTemplate, link.Alias ).ToArguments( ),
                CommandDeckJsonContext.Default.DictionaryStringString ),
            ProviderId = ProviderIds.Quicklinks,
            SourceId = SourceIds.CommandsQuicklinks
        };
    }

}
