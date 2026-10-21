using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task QuicklinkRuntimeChangesSearchAndFallbackAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            var options = new CommandDeckOptions
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "runtime.db" ) },
                Indexing = new IndexingOptions { Roots = [], IndexDesktopApplications = false, IndexOmarchyCommands = false },
                Ai = new AiOptions { Enabled = false, Providers = [] },
                Quicklinks = [TestQuicklink( "configured", alias: "configured" )]
            };
            var config = Path.Combine( root, "config.json" );
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config,
                new RuntimeConstructionOptions { StartIndexer = false }, includeClipboardHistory: false, default );
            await runtime.ChangeQuicklinkAsync( "save", TestQuicklink( ), null, default );
            var found = await runtime.SearchAsync( new SearchRequest { Query = "docs some arbitrary words" }, default );
            var alias = found.Single( result => result.Id == "quicklink:docs" );
            Equal( SourceIds.CommandsQuicklinks, alias.SourceId );
            var payload = UrlActionPayload.Read( alias.Actions.First( ).Arguments );
            Equal( "https://example.test/?q=some%20arbitrary%20words", payload.Expand( "docs some arbitrary words" ) );
            var pathQuery = await runtime.SearchAsync( new SearchRequest { Query = "docs /literal/path" }, default );
            True( pathQuery.Any( result => result.Id == "quicklink:docs" ) );
            var bootstrap = await runtime.GetBootstrapAsync( default );
            True( bootstrap.FallbackQuicklinks.Any( link => link.Id == "docs" ) );
            var preferences = (await runtime.GetPreferencesAsync( false, default )).Preferences;
            await runtime.UpdatePreferencesAsync( CopyPreferencesWithFallback( preferences, "docs" ), default );
            var fallback = (await runtime.SearchAsync( new SearchRequest { Query = "unmatchedzzword" }, default ))
                .Single( result => result.Metadata.GetValueOrDefault( "fallback" ) == "web" );
            Equal( "docs", fallback.Metadata["quicklinkId"] );
            await runtime.ChangeQuicklinkAsync( "save", TestQuicklink( title: "Renamed docs" ), null, default );
            Equal( "Renamed docs", (await runtime.GetBootstrapAsync( default )).FallbackQuicklinks.Single( link => link.Id == "docs" ).Title );

            // A CLI-style second process writes the same durable state; the running daemon sees it immediately.
            await using SqliteCatalog other = new( options.Storage );
            await other.ConfigureQuicklinksAsync( options.Quicklinks, default );
            var result = await other.ChangeQuicklinkAsync( "remove", null, "docs", default );
            True( result.Message?.Contains( "default", StringComparison.Ordinal ) == true );
            True( (await runtime.GetBootstrapAsync( default )).FallbackQuicklinks.All( link => link.Id != "docs" ) );
            True( (await runtime.GetPreferencesAsync( false, default )).Preferences.WebFallbackQuicklinkId is null );
            True( (await runtime.SearchAsync( new SearchRequest { Query = "docs some arbitrary words" }, default ))
                .All( value => value.Id != "quicklink:docs" ) );
            await runtime.ChangeQuicklinkAsync( "disable", null, "configured", default );
            Equal( 0, (await runtime.GetBootstrapAsync( default )).FallbackQuicklinks.Count );
            await runtime.ChangeQuicklinkAsync( "enable", null, "configured", default );
            Equal( 1, (await runtime.GetBootstrapAsync( default )).FallbackQuicklinks.Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task VerifyQuicklinksProtocolAsync( UnixProtocolClient client, CancellationToken token )
    {
        long sequence = 810;
        async Task<ProtocolMessage?> SendAsync( ProtocolRequest request, string type )
        {
            await client.SendAsync( request, token );
            return await ReadMessageAsync( client, request.RequestId, type, token );
        }
        var saved = await SendAsync( new ProtocolRequest
        {
            RequestId = ++sequence,
            Operation = ProtocolOperations.QuicklinksSave,
            Quicklink = TestQuicklink( )
        }, ProtocolMessageTypes.QuicklinksResult );
        True( saved?.Quicklinks?.Quicklinks.Any( entry => entry.Quicklink.Id == "docs" ) == true );
        var rejected = await SendAsync( new ProtocolRequest
        {
            RequestId = ++sequence,
            Operation = ProtocolOperations.QuicklinksRemove,
            QuicklinkId = "docs"
        }, ProtocolMessageTypes.Error );
        True( rejected?.Error?.Contains( "confirmed", StringComparison.Ordinal ) == true );
        var invalid = await SendAsync( new ProtocolRequest
        {
            RequestId = ++sequence,
            Operation = ProtocolOperations.QuicklinksSave,
            Quicklink = TestQuicklink( url: "file:///tmp/a" )
        }, ProtocolMessageTypes.Error );
        True( invalid?.Error?.Contains( "HTTP", StringComparison.Ordinal ) == true );
        foreach ( var operation in new[] { ProtocolOperations.QuicklinksDisable, ProtocolOperations.QuicklinksEnable } )
        {
            var changed = await SendAsync( new ProtocolRequest
            {
                RequestId = ++sequence,
                Operation = operation,
                QuicklinkId = "web"
            }, ProtocolMessageTypes.QuicklinksResult );
            Equal( operation == ProtocolOperations.QuicklinksEnable,
                changed!.Quicklinks!.Quicklinks.Single( entry => entry.Quicklink.Id == "web" ).Enabled );
        }
        var removed = await SendAsync( new ProtocolRequest
        {
            RequestId = ++sequence,
            Operation = ProtocolOperations.QuicklinksRemove,
            QuicklinkId = "docs",
            Confirmed = true
        }, ProtocolMessageTypes.QuicklinksResult );
        True( removed?.Quicklinks?.Quicklinks.All( entry => entry.Quicklink.Id != "docs" ) == true );
        var listed = await SendAsync( new ProtocolRequest
        {
            RequestId = ++sequence,
            Operation = ProtocolOperations.QuicklinksGet
        }, ProtocolMessageTypes.QuicklinksResult );
        Equal( 1, listed?.Quicklinks?.Quicklinks.Count );
    }
}
