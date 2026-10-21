namespace CommandDeck.Specs;

public static partial class Program
{
    private const string BrowseDefaults = """
        {
          // Synthetic menu: dotted IDs imply the tree.
          "apps": {"label":"Apps","provider":"apps"},
          "apps.editor": {"label":"Editor","action":"synthetic-editor"},
          "style": {"label":"Style"},
          "style.theme": {"label":"Theme","aliases":["themes"]},
          "style.zeta": {"label":"Zeta","action":"synthetic-zeta"},
          "style.alpha": {"label":"Alpha","action":"synthetic-alpha"},
          "style.theme.dark": {"label":"Dark","action":"synthetic-dark"},
          "style.font": {"label":"Font","provider":"fonts"},
          "style.empty": {"label":"Empty"},
          "style.mid": {"label":"Mid","action":"synthetic-mid","aliases":["middle"]},
          "style.theme.light": {"label":"Light","action":"synthetic-light"},
          "setup": {"label":"Setup"},
          "setup.looks": {"label":"Looks","target":"style.theme"},
          "system": {"label":"System"},
          "system.power": {"label":"Power"},
          "system.power.off": {"label":"Off","action":"synthetic-off"},
        }
        """;

    private const string BrowseExtension = """
        {
          "items": {
            "style.zeta": {"label":"Zeta custom","action":"synthetic-zeta-custom"},
            "style.extra": {"label":"Extra","action":"synthetic-extra"},
            "style.moved": {"label":"Moved","parent":"style.theme","action":"synthetic-moved"}
          }
        }
        """;

    private static OmarchyMenu BrowseMenu( int bigMenuEntries = 0 )
    {
        var big = string.Join( ",\n", Enumerable.Range( 0, bigMenuEntries ).Select( static index =>
            $"\"big.item{index:D3}\": {{\"label\":\"Item {index:D3}\",\"action\":\"synthetic-{index}\"}}" ) );
        var bigDocument = bigMenuEntries == 0 ? "{}" : $"{{\"big\": {{\"label\":\"Big\"}},\n{big}}}";
        return OmarchyMenu.Parse(
        [
            new OmarchyMenuDocument( "default.jsonc", BrowseDefaults ),
            new OmarchyMenuDocument( "extension.jsonc", BrowseExtension ),
            new OmarchyMenuDocument( "big.jsonc", bigDocument )
        ] );
    }

    // Menu nodes with entries become submenu rows that open in the launcher;
    // empty, provider-filled, Apps and System menus do not, and the rows that
    // open Omarchy's native menu stay.
    private static async Task OmarchyMenuIndexesSubmenuRowsAsync( )
    {
        var rows = BrowseMenu( ).Rows.ToDictionary( static row => row.StableId, StringComparer.Ordinal );
        var style = rows["omarchy:style"];
        Equal( "Style", style.Title );
        Equal( "Omarchy", style.Subtitle );
        var theme = rows["omarchy:style.theme"];
        Equal( "Theme", theme.Title );
        Equal( "Style", theme.Subtitle );
        True( theme.PayloadJson!.Contains( "\"omarchyMenu\":\"style.theme\"", StringComparison.Ordinal ) );
        True( theme.PayloadJson.Contains( "\"omarchyMenuPath\":\"Style \\u203A Theme\"", StringComparison.Ordinal ) );
        True( theme.PayloadJson.Contains( "\"omarchyMenuItems\":\"3\"", StringComparison.Ordinal ) );
        True( theme.Body!.Contains( "themes", StringComparison.Ordinal ) );
        True( rows["omarchy:setup.looks"].PayloadJson!.Contains( "\"omarchyMenu\":\"style.theme\"", StringComparison.Ordinal ) );
        foreach ( var missing in new[] { "omarchy:style.font", "omarchy:style.empty", "omarchy:apps", "omarchy:apps.editor",
            "omarchy:system", "omarchy:system.power", "omarchy:system.power.off" } )
        {
            True( !rows.ContainsKey( missing ) );
        }

        True( rows.ContainsKey( "omarchy-route:root" ) && rows.ContainsKey( "omarchy-route:system" ) );
        Equal( "Zeta custom", rows["omarchy:style.zeta"].Title );

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-omarchy-rows-" ).FullName;
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( AsAsync( rows.Values ), null, default );
            var found = await new CatalogSearchProvider( catalog ).SearchAsync( QueryParser.Parse( "theme" ), new SearchRequest { Query = "theme" }, default );
            var submenu = found.Single( static value => value.Id == "omarchy:style.theme" );
            var open = submenu.Actions.Single( );
            Equal( ActionKinds.BrowseOmarchyMenu, open.Kind );
            Equal( "style.theme", open.Arguments[ResultMetadataKeys.OmarchyMenu] );
            Equal( "Style › Theme", open.Arguments[ResultMetadataKeys.OmarchyMenuPath] );
            True( submenu.CanCustomize && submenu.Details.Any( static detail => detail.Label == "Menu" && detail.Value == "Style › Theme" ) );
            var dark = (await new CatalogSearchProvider( catalog ).SearchAsync( QueryParser.Parse( "dark" ), new SearchRequest { Query = "dark" }, default ))
                .Single( static value => value.Id == "omarchy:style.theme.dark" );
            Equal( ActionKinds.RunTrustedShell, dark.Actions.Single( ).Kind );
            Equal( "style.theme", dark.Metadata[ResultMetadataKeys.OmarchyParent] );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    // A scoped search lists one submenu's direct entries in Omarchy's order,
    // whole, through its own provider and the catalog's stable IDs, actions,
    // bookmarks, aliases and hidden items. Typing filters within it.
    private static async Task OmarchyMenuBrowseListsChildrenInOmarchyOrderAsync( )
    {
        var menu = BrowseMenu( 70 );
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-omarchy-browse-" ).FullName;
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            await catalog.ReplaceAllAsync( AsAsync( menu.Rows ), null, default );
            OmarchyMenuProvider provider = new( _ => ValueTask.FromResult( menu ), catalog );
            CatalogSearchProvider catalogProvider = new( catalog, catalog.LauncherPreferences );
            SearchCoordinator coordinator = new( [catalogProvider, provider], new SearchOptions( ), catalog,
                catalog.LauncherPreferences, catalog.UsageHistory );
            async Task<IReadOnlyList<SearchResult>> BrowseAsync( string? menuId, string query = "", int limit = 50 ) =>
                await coordinator.SearchAsync( new SearchRequest { Query = query, OmarchyMenuId = menuId, Limit = limit }, default );
            string Ids( IEnumerable<SearchResult> results ) => string.Join( '|', results.Select( static value => value.Id ) );

            var style = await BrowseAsync( "style" );
            Equal( "omarchy:style.theme|omarchy:style.zeta|omarchy:style.alpha|omarchy:style.mid|omarchy:style.extra", Ids( style ) );
            True( style.All( static value => value.Section?.Title == "Style" && value.ProviderId == ProviderIds.Omarchy ) );
            Equal( "Zeta custom", style[1].Title );
            var catalogAlpha = (await catalogProvider.SearchAsync( QueryParser.Parse( "alpha" ), new SearchRequest { Query = "alpha" }, default ))
                .Single( static value => value.Id == "omarchy:style.alpha" );
            var scopedAlpha = style.Single( static value => value.Id == "omarchy:style.alpha" );
            Equal( catalogAlpha.Actions.Single( ).Kind, scopedAlpha.Actions.Single( ).Kind );
            Equal( catalogAlpha.Actions.Single( ).Arguments["command"], scopedAlpha.Actions.Single( ).Arguments["command"] );
            Equal( ActionKinds.BrowseOmarchyMenu, style[0].Actions.Single( ).Kind );

            Equal( "omarchy:style.alpha", Ids( await BrowseAsync( "style", "alp" ) ) );
            Equal( "omarchy:style.mid", Ids( await BrowseAsync( "style", "middle" ) ) );
            Equal( "omarchy:style.zeta", Ids( await BrowseAsync( "style", "-theme zeta" ) ) );
            Equal( "omarchy:style.alpha", Ids( await BrowseAsync( "style", "oma: alp" ) ) );
            Equal( 0, (await BrowseAsync( "style", "app: alp" )).Count );
            Equal( "omarchy:style.theme.dark|omarchy:style.theme.light|omarchy:style.moved", Ids( await BrowseAsync( "style.theme" ) ) );
            foreach ( var scope in new[] { "nope", "style.alpha", "setup.looks", "system", "system.power", "apps", "root", "style.empty" } )
            {
                Equal( 0, (await BrowseAsync( scope )).Count );
            }

            var big = await BrowseAsync( "big", limit: 200 );
            Equal( 70, big.Count );
            True( big.Select( static value => value.Title ).SequenceEqual( Enumerable.Range( 0, 70 ).Select( static index => $"Item {index:D3}" ) ) );
            Equal( 50, (await BrowseAsync( "big" )).Count );

            await catalog.UsageHistory.RecordInvocationAsync( "omarchy:style.extra", "run", null, default );
            await catalog.SetAliasAsync( style[1], "zz", default );
            await catalog.SetHiddenAsync( scopedAlpha, true, default );
            var personalized = await BrowseAsync( "style" );
            Equal( "omarchy:style.theme|omarchy:style.zeta|omarchy:style.mid|omarchy:style.extra", Ids( personalized ) );
            True( personalized[1].IsFavorite && personalized[1].Aliases.SequenceEqual( ["zz"] ) );
            Equal( "omarchy:style.zeta", Ids( await BrowseAsync( "style", "zz" ) ) );

            var preferences = await catalog.LauncherPreferences.GetPreferencesAsync( default );
            True( !ProviderEligibilityPolicy.Select( [provider], QueryParser.Parse( "alpha" ), preferences, null ).Any( ) );
            True( ProviderEligibilityPolicy.Select( [catalogProvider, provider], QueryParser.Parse( "" ), preferences, null, "style" )
                .SequenceEqual( [provider] ) );
            Equal( 0, (await provider.SearchAsync( QueryParser.Parse( "" ),
                new SearchRequest { OmarchyMenuId = "style", EnabledSearchSources = [SourceIds.Applications] }, default )).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    // The scoped rows carry the same live state as catalog rows, from one
    // bounded batch that never sees the typed text, and an unavailable entry
    // keeps its place while browsing.
    private static async Task OmarchyMenuBrowseMarksLiveStateAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) || !File.Exists( "/bin/bash" ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux bash." );
        }

        const string document = """
            {
              "toggle": {"label":"Toggle"},
              "toggle.first": {"label":"First mode","action":"first","when":"false"},
              "toggle.second": {"label":"Second mode","action":"second","checked":"[[ 3 -gt 2 ]]"},
              "toggle.third": {"label":"Third mode","action":"third"}
            }
            """;
        var menu = OmarchyMenu.Parse( [new OmarchyMenuDocument( "menu.jsonc", document )] );
        RecordingBashRunner runner = new( );
        using OmarchyMenuState state = new( runner.RunAsync, null, TimeSpan.FromSeconds( 10 ) );
        OmarchyMenuProvider provider = new( _ => ValueTask.FromResult( menu ), null, state );
        SearchCoordinator coordinator = new( [provider], new SearchOptions( ) );

        var rows = await coordinator.SearchAsync( new SearchRequest { OmarchyMenuId = "toggle" }, default );
        Equal( "omarchy:toggle.first|omarchy:toggle.second|omarchy:toggle.third", string.Join( '|', rows.Select( static value => value.Id ) ) );
        Equal( "true", rows[0].Metadata.GetValueOrDefault( ResultMetadataKeys.OmarchyUnavailable ) );
        Equal( "true", rows[1].Metadata.GetValueOrDefault( ResultMetadataKeys.OmarchyChecked ) );
        True( !rows[2].Metadata.ContainsKey( ResultMetadataKeys.OmarchyUnavailable ) );
        Equal( 1, runner.Calls.Count );

        var filtered = await coordinator.SearchAsync( new SearchRequest { Query = "mode", OmarchyMenuId = "toggle" }, default );
        Equal( "omarchy:toggle.first", filtered[^1].Id );
        True( runner.Calls.All( static call => !call.Arguments[^1].Contains( "mode", StringComparison.Ordinal ) ) );
    }

    private static void OmarchyMenuScopeIsValidated( )
    {
        foreach ( var invalid in new[] { "", "  ", "style\n", new string( 'a', 513 ) } )
        {
            Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new ProtocolRequest
            {
                RequestId = 1,
                Operation = ProtocolOperations.Search,
                OmarchyMenuId = invalid
            } ) );
        }

        Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            RequestId = 1,
            Operation = ProtocolOperations.Search,
            OmarchyMenuId = "style",
            HerdrWorkspaceId = "w1"
        } ) );
        Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            RequestId = 1,
            Operation = ProtocolOperations.Search,
            OmarchyMenuId = "style",
            BrowseDirectory = "/tmp"
        } ) );
        ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            RequestId = 1,
            Operation = ProtocolOperations.Search,
            OmarchyMenuId = "style.theme",
            Limit = 200
        } );
    }

    // Opening a submenu is the launcher's job; the daemon refuses to run it and
    // assistant tools cannot use it.
    private static async Task OmarchyMenuBrowseActionIsLauncherOwnedAsync( )
    {
        var submenu = SqliteCatalog.ProjectIndexedItem( BrowseMenu( ).Rows.Single( static row => row.StableId == "omarchy:style" ), 1, null );
        var open = submenu.Actions.Single( );
        var outcome = await new LinuxActionExecutor( ).ExecuteAsync( submenu, open, string.Empty, default );
        True( !outcome.Succeeded && outcome.Message!.Contains( "Omarchy menu browser", StringComparison.Ordinal ) );
        Equal( AgentActionRisks.Denied, AgentLauncherTools.ClassifyRisk( submenu, open ) );
    }

    // The browser re-reads the menu files only after one of them changes.
    private static async Task OmarchyMenuSourceReparsesChangedFilesAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-omarchy-source-" ).FullName;
        try
        {
            var path = Path.Combine( root, "omarchy-menu.jsonc" );
            await File.WriteAllTextAsync( path, """{"style": {"label":"Style"}, "style.a": {"label":"A","action":"a"}}""" );
            OmarchyMenuSource source = new( ( ) => [path, Path.Combine( root, "missing.jsonc" )] );
            var first = await source.LoadAsync( default );
            True( ReferenceEquals( first, await source.LoadAsync( default ) ) );
            Equal( 1, first.Children( "style" )!.Count );

            await File.WriteAllTextAsync( path, """{"style": {"label":"Style"}, "style.a": {"label":"A","action":"a"}, "style.b": {"label":"B","action":"b"}}""" );
            var second = await source.LoadAsync( default );
            Equal( 2, second.Children( "style" )!.Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
