using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task HeyEmptyQueryOffersBoxesAndPagesAsync( )
    {
        HeyFake fake = new( );
        var rows = await HeySearchAsync( HeyProviderFor( fake ), "hey:" );

        Equal( "hey:box:imbox,hey:box:reply-later,hey:box:set-aside,hey:box:feed,hey:box:paper-trail,hey:box:bubbled-up," +
            "hey:page:screener,hey:page:compose,hey:page:home,hey:state:hint", string.Join( ',', rows.Select( static row => row.Id ) ) );
        Equal( 0, fake.Calls.Count );
        True( rows.All( static row => !row.CanCustomize && PrivateSources.IsPrivate( row ) ) );
        True( rows.Zip( rows.Skip( 1 ) ).All( static pair => pair.First.Score > pair.Second.Score ), "the rows keep their order" );
        True( !rows.Any( static row => (row.Title + row.Subtitle).Any( char.IsDigit ) ), "no row carries a count" );

        var imbox = rows[0];
        Equal( "Imbox", imbox.Title );
        Equal( ActionKinds.ApplyQuery, imbox.Actions.Single( ).Kind );
        Equal( "box:imbox", imbox.Actions[0].Arguments["query"] );
        Equal( HeySearch.FilterId, imbox.Actions[0].Arguments["filterId"] );

        Equal( ActionKinds.HeyOpenPage, rows[6].Actions.Single( ).Kind );
        Equal( HeyPages.Screener, rows[6].Actions[0].Arguments["page"] );
        Equal( HeyPages.Compose, rows[7].Actions[0].Arguments["page"] );
        Equal( HeyPages.Home, rows[8].Actions[0].Arguments["page"] );
    }

    private static async Task HeyBoxListingFiltersLocallyAsync( )
    {
        HeyFake fake = new( );
        HeyClock clock = new( HeyNow );
        // A long typing pause proves a box listing never waits for one.
        HeyProvider provider = new( fake.Client( ), clock, TimeSpan.FromSeconds( 30 ) );
        using CancellationTokenSource quick = new( TimeSpan.FromSeconds( 5 ) );
        async Task<IReadOnlyList<SearchResult>> Browse( string query ) =>
            await provider.SearchAsync( QueryParser.Parse( query ), HeyRequest( ), quick.Token );

        var rows = await Browse( "hey: box:imbox" );
        Equal( "box|view|imbox|--json|--limit=50", string.Join( '|', fake.Calls.Single( ).Args ) );
        Equal( "hey:topic:9101,hey:topic:9103,hey:state:more-in-box", string.Join( ',', rows.Select( static row => row.Id ) ) );
        Equal( HeyProvider.UnreadIcon, rows[0].Icon );
        Equal( HeyProvider.ThreadIcon, rows[1].Icon );
        True( rows[0].Details.Any( static field => field.Label == "Status" && field.Value == "Unseen" ) );
        Equal( HeyPages.Home, rows[2].Actions.Single( ).Arguments["page"] );

        // The words filter the cached listing; nothing else runs.
        var filtered = await Browse( "hey: box:imbox noon" );
        Equal( "hey:topic:9101,hey:state:more-in-box", string.Join( ',', filtered.Select( static row => row.Id ) ) );
        Equal( "hey:state:empty-box", (await Browse( "hey: box:imbox nothing-like-this" )).Single( ).Id );
        Equal( 1, fake.Calls.Count );

        clock.Now = HeyNow + HeyProvider.BoxCacheLifetime + TimeSpan.FromSeconds( 1 );
        await Browse( "hey: box:imbox" );
        Equal( 2, fake.Calls.Count );

        var unknown = await Browse( "hey: box:inbox" );
        Equal( "hey:state:refinement", unknown.Single( ).Id );
        True( unknown[0].Subtitle!.Contains( "imbox, reply-later", StringComparison.Ordinal ) );
        var mixed = await Browse( "hey: box:imbox from:jane" );
        True( mixed.Single( ).Subtitle!.Contains( "in:imbox", StringComparison.Ordinal ) );
        Equal( 2, fake.Calls.Count );

        HeyFake empty = new( )
        {
            Respond = _ => HeyResult( """{"ok":true,"data":{"id":1,"kind":"imbox","name":"Imbox","postings":[]}}""" )
        };
        var emptyRows = await HeySearchAsync( HeyProviderFor( empty ), "hey: box:reply-later" );
        Equal( "hey:state:empty-box", emptyRows.Single( ).Id );
        Equal( "Nothing in Reply Later", emptyRows[0].Title );
        Equal( "box|view|laterbox|--json|--limit=50", string.Join( '|', empty.Calls.Single( ).Args ) );
    }

    private static async Task HeyFailuresOfferSearchAgainAsync( )
    {
        HeyFake offline = new( )
        {
            Respond = _ => HeyResult( """{"ok":false,"error":"offline","code":"network"}""", 1 )
        };
        var provider = HeyProviderFor( offline );

        SearchRequest scoped = new( )
        {
            Query = "invoice",
            Expression = new QueryExpression { IncludeFilterIds = [HeySearch.FilterId] },
            EnabledSearchSources = [SourceIds.Hey]
        };
        var row = (await provider.SearchAsync( QueryParser.Parse( scoped.Query, scoped.Expression ), scoped, default )).Single( );
        var retry = row.Actions.Single( );
        Equal( ActionKinds.ApplyQuery, retry.Kind );
        Equal( "invoice", retry.Arguments["query"] );
        Equal( HeySearch.FilterId, retry.Arguments["filterId"] );

        SearchRequest typed = new( )
        {
            Query = "@hey invoice",
            EnabledSearchSources = [SourceIds.Hey]
        };
        var typedRetry = (await provider.SearchAsync( QueryParser.Parse( typed.Query ), typed, default )).Single( ).Actions.Single( );
        Equal( "@hey invoice", typedRetry.Arguments["query"] );
        Equal( "", typedRetry.Arguments["filterId"] );

        // A refused search is not retried as is.
        HeyFake refused = new( )
        {
            Respond = _ => HeyResult( """{"ok":false,"error":"bad value","code":"usage"}""", 1 )
        };
        Equal( 0, (await HeySearchAsync( HeyProviderFor( refused ), "hey: invoice" )).Single( ).Actions.Count );
    }

    private static async Task HeyPagesOpenOnWebOrTuiAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-hey-pages-" ).FullName;
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            HeyFake fake = new( )
            {
                Respond = _ => HeyResult( """{"ok":false,"error":"no running HEY TUI","code":"unknown"}""", 1 )
            };
            LinuxActionExecutor executor = new( catalog, hey: fake.Client( ) );
            using HeyLaunchFixture launchers = new( HeyActionHandler.HeyWebAppLauncher, HeyActionHandler.TuiLaunchOrFocus, "uwsm-app" );
            var row = HeyPrivateRow( );
            CommandAction Page( string page ) => new( )
            {
                Id = HeyProvider.OpenPageActionId,
                Title = page,
                Kind = ActionKinds.HeyOpenPage,
                Arguments = new( StringComparer.Ordinal )
                {
                    ["page"] = page
                }
            };

            foreach ( var (page, url) in new[]
            {
                (HeyPages.Home, "https://app.hey.com"),
                (HeyPages.Screener, "https://app.hey.com/clearances"),
                (HeyPages.Compose, "https://app.hey.com/messages/new")
            } )
            {
                True( (await executor.ExecuteAsync( row, Page( page ), "", default )).Succeeded );
                Equal( url, launchers.Launched( HeyActionHandler.HeyWebAppLauncher )!.Single( ) );
            }

            Equal( 0, fake.Calls.Count );
            True( !(await executor.ExecuteAsync( row, Page( "settings" ), "", default )).Succeeded );

            var json = JsonSerializer.SerializeToNode( await catalog.GetPreferencesAsync( default ), CommandDeckJsonContext.Default.LauncherPreferences )!;
            json["heyOpenTarget"] = HeyOpenTargets.Terminal;
            await catalog.ReplacePreferencesAsync( json.Deserialize( CommandDeckJsonContext.Default.LauncherPreferences )!, default );

            True( (await executor.ExecuteAsync( row, Page( HeyPages.Screener ), "", default )).Succeeded );
            Equal( "tui|--instance|omarchy|--screener|--remote", string.Join( '|', fake.Calls.Single( ).Args ) );
            Equal( "--app-id=org.omarchy.hey|'/opt/hey/bin/hey'|tui|--instance|omarchy|--screener",
                string.Join( '|', launchers.Launched( HeyActionHandler.TuiLaunchOrFocus )! ) );

            // Home only raises or opens the TUI; there is nothing to hand over.
            True( (await executor.ExecuteAsync( row, Page( HeyPages.Home ), "", default )).Succeeded );
            Equal( 1, fake.Calls.Count );
            Equal( "--app-id=org.omarchy.hey|'/opt/hey/bin/hey'|tui|--instance|omarchy",
                string.Join( '|', launchers.Launched( HeyActionHandler.TuiLaunchOrFocus )! ) );

            // HEY's terminal app has no compose destination.
            True( (await executor.ExecuteAsync( row, Page( HeyPages.Compose ), "", default )).Succeeded );
            Equal( "https://app.hey.com/messages/new", launchers.Launched( HeyActionHandler.HeyWebAppLauncher )!.Single( ) );
            Equal( 1, fake.Calls.Count );

            // Query rows belong to the launcher and never to the assistant.
            CommandAction browse = new( )
            {
                Id = "browse",
                Title = "Browse",
                Kind = ActionKinds.ApplyQuery
            };
            True( !(await executor.ExecuteAsync( row, browse, "", default )).Succeeded );
            SearchResult publicRow = new( )
            {
                Id = "app:x",
                Title = "x",
                Kind = ResultKinds.Application,
                SourceId = SourceIds.Applications
            };
            Equal( AgentActionRisks.Denied, AgentLauncherTools.ClassifyRisk( publicRow, browse ) );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }
}
