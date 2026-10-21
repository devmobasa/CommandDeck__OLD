using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task InlineCalculatorAcceptsLauncherProtocolAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-action-payload-", preservePrefix: true ).FullName;
        var files = Directory.CreateDirectory( Path.Combine( root, "notes" ) ).FullName;
        var titlePath = Path.Combine( files, "days until 11 sep.md" );
        var bodyPath = Path.Combine( files, "Notes.md" );
        var conversionPath = Path.Combine( files, "5 km in miles.md" );
        var ordinaryPath = Path.Combine( files, "10 years in prison.md" );
        await File.WriteAllTextAsync( titlePath, "Date notes" );
        await File.WriteAllTextAsync( conversionPath, "Conversion notes" );
        await File.WriteAllTextAsync( ordinaryPath, "Sentence notes" );
        await File.WriteAllTextAsync( bodyPath, "11 sep: days until launch\n5 km in miles\n10 years in prison" );
        var titleId = StableId.Create( ResultKinds.File, titlePath );
        var bodyId = StableId.Create( ResultKinds.File, bodyPath );
        CommandDeckOptions options = new( )
        {
            Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
            Indexing = new IndexingOptions
            {
                Roots = [new IndexRootOptions { Path = files, IncludeContentInSearch = true }],
                IndexDesktopApplications = false,
                IndexOmarchyCommands = false
            },
            Ai = new AiOptions { Enabled = false, Providers = [] }
        };
        SqliteCatalog catalog = new( options.Storage );
        await catalog.InitializeAsync( default );
        DateCalculatorClock clock = new( new DateTimeOffset( 2026, 10, 21, 12, 0, 0, TimeSpan.Zero ), TimeZoneInfo.Utc );
        CalculatorProvider calculator = new( clock: clock, inlineEnabled: ( ) => catalog.History.InlineCalculator,
            rates: ( ) => throw new InvalidOperationException( "Inline arithmetic must stay offline." ) );
        await using SearchCoordinator search = new( [calculator, new CatalogSearchProvider( catalog, catalog )], options.Search,
            catalog, catalog.LauncherPreferences, history: catalog.History );
        await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory,
            new LinuxActionExecutor( ), search, historyPolicy: catalog.History );
        await runtime.Indexer.ReindexAsync( null, default );
        using CancellationTokenSource stop = new( );
        var socket = SpecTemporaryFiles.SocketPath( root, "daemon.sock" );
        UnixProtocolServer server = new( runtime, socket );
        _ = server.RunAsync( stop.Token );
        var oldPath = Environment.GetEnvironmentVariable( "PATH" );
        var oldFixture = Environment.GetEnvironmentVariable( ActionPayloadFixtureDirectory );
        File.CreateSymbolicLink( Path.Combine( root, "wl-copy" ), Environment.ProcessPath! );
        Environment.SetEnvironmentVariable( "PATH", root + Path.PathSeparator + oldPath );
        Environment.SetEnvironmentVariable( ActionPayloadFixtureDirectory, root );
        try
        {
            await using var client = await ConnectWithRetryAsync( socket );
            long requestId = 0;
            foreach ( var (query, expected) in new[]
            {
                ("100 * 5", "500"), ("2 + 2", "4"),
                ("days until 11 sep", "325 days"), ("-2 + 7", "5"), ("5 km in miles", "3.10685596118667 mi")
            } )
            {
                var expression = QueryParser.Parse( query ).Expression;
                var json = JsonSerializer.Serialize( expression, CommandDeckJsonContext.Default.QueryExpression );
                var id = ++requestId;
                // The complete SearchSessionController payload carries raw text
                // alongside ordinary clauses, including deduplicated operands.
                var payload = $$"""
                    {"version":3,"requestId":{{id}},"operation":"search","query":"{{query}}",
                     "browseDirectory":null,"herdrWorkspaceId":null,"omarchyMenuId":null,"browseOffset":0,
                     "expression":{{json}},"limit":50,"activeWorkspace":""}
                    """;
                await client.SendRawAsync( payload.Replace( "\n", "", StringComparison.Ordinal ), default );
                var messages = await CollectUntilDoneAsync( client, id );
                var row = messages.First( static message => message.Type == ProtocolMessageTypes.Result ).Result!;
                Equal( expected, row.Title );

                id = ++requestId;
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = id,
                    Operation = ProtocolOperations.Invoke,
                    ResultId = row.Id,
                    InvocationReference = row.InvocationReference,
                    ActionId = "copy",
                    Query = query
                }, default );
                var invoked = await CollectUntilDoneAsync( client, id );
                True( !invoked.Any( static message => message.Type == ProtocolMessageTypes.Error ) );
                Equal( query.StartsWith( "days", StringComparison.Ordinal ) ? "325" : expected,
                    await File.ReadAllTextAsync( Path.Combine( root, "stdin" ) ) );
            }

            foreach ( var enabled in new[] { true, false } )
            {
                var id = ++requestId;
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = id,
                    Operation = ProtocolOperations.PreferencesUpdate,
                    Preferences = new LauncherPreferences
                    {
                        Sources = LauncherPreferences.CreateDefault( ).Sources,
                        InlineCalculator = enabled
                    }
                }, default );
                var preferences = await ReadMessageAsync( client, id, ProtocolMessageTypes.PreferencesResult, default );
                Equal( ( bool? ) enabled, preferences!.Preferences!.Preferences.InlineCalculator );

                foreach ( var (input, matchId, isCandidate, literal) in new[]
                {
                    ("days until 11 sep", titleId, true, false),
                    ("5 km in miles", StableId.Create( ResultKinds.File, conversionPath ), true, false),
                    ("10 years in prison", StableId.Create( ResultKinds.File, ordinaryPath ), false, false),
                    ("5 km in miles", StableId.Create( ResultKinds.File, conversionPath ), true, true),
                    ("10 years in prison", StableId.Create( ResultKinds.File, ordinaryPath ), false, true)
                } )
                {
                    foreach ( var excluded in new[] { false, true } )
                    {
                        var query = input + (excluded ? " -launch" : "");
                        id = ++requestId;
                        await client.SendAsync( new ProtocolRequest { RequestId = id, Operation = ProtocolOperations.Search, Query = query }, default );
                        var original = await CollectUntilDoneAsync( client, id );
                        Equal( enabled && isCandidate && !excluded, original.First( static message => message.Type == ProtocolMessageTypes.Result ).Result!.Kind == ResultKinds.Calculator );

                        id = ++requestId;
                        await client.SendAsync( new ProtocolRequest
                        {
                            RequestId = id,
                            Operation = ProtocolOperations.SavedSearchSave,
                            SavedSearch = new SavedSearch
                            {
                                Id = "saved-search:date",
                                Name = "Date notes",
                                Expression = new QueryExpression { LiteralQuery = literal ? query : null, Clauses = QueryParser.Parse( query ).Expression.Clauses }
                            }
                        }, default );
                        _ = await ReadMessageAsync( client, id, ProtocolMessageTypes.SavedSearchResult, default );
                        id = ++requestId;
                        await client.SendAsync( new ProtocolRequest { RequestId = id, Operation = ProtocolOperations.SavedSearchGet }, default );
                        var saved = await ReadMessageAsync( client, id, ProtocolMessageTypes.SavedSearchResult, default );
                        var expression = saved!.SavedSearches!.Searches.Single( ).Expression;
                        // DeckSearchActions restores each clause separately unless a
                        // literal query exists. Reparse after typing into that field.
                        var restoredText = expression.LiteralQuery ?? string.Join( ' ', expression.Clauses.Select( static clause =>
                            (clause.Excluded ? "-" : "") + "\"" + clause.Text + "\"" ) );
                        foreach ( var text in new[] { query, restoredText } )
                        {
                            id = ++requestId;
                            await client.SendAsync( new ProtocolRequest
                            {
                                RequestId = id,
                                Operation = ProtocolOperations.Search,
                                Query = text,
                                Expression = QueryParser.Parse( text ).Expression
                            }, default );
                            var messages = await CollectUntilDoneAsync( client, id );
                            var rows = messages.Where( static message => message.Type == ProtocolMessageTypes.Result )
                                .Select( static message => message.Result! ).ToArray( );
                            True( rows.Any( row => row.Id == matchId ), "The title must still match after reopening." );
                            Equal( !excluded, rows.Any( row => row.Id == bodyId ) );
                            var inline = enabled && isCandidate && !excluded && text == query;
                            Equal( inline, rows[0].Kind == ResultKinds.Calculator );
                            Equal( (inline ? 1 : 0) + (excluded ? 1 : 2), rows.Length );
                            foreach ( var row in rows.Where( static row => row.Kind == ResultKinds.File ) )
                            {
                                True( row.Ranking.Contributions.Any( static value => value.Label == "Catalog lexical match" ),
                                    "Saved and reopened searches must retain independent FTS clauses and exclusions." );
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", oldPath );
            Environment.SetEnvironmentVariable( ActionPayloadFixtureDirectory, oldFixture );
            await stop.CancelAsync( );
            await server.Completion;
        }
    }
}
