namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task QuicklinkAliasesRespectSourceEligibilityAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            await using SqliteCatalog store = new( new StorageOptions { DatabasePath = Path.Combine( root, "links.db" ) } );
            await store.ConfigureQuicklinksAsync( [TestQuicklink( )], default );
            await store.ReplaceAllAsync( FilterAliasItems( ), null, default );
            CatalogSearchProvider provider = new( store, store );
            foreach ( var text in new[] { "file: docs", "docs -link:", "cmd: docs" } )
            {
                var found = await provider.SearchAsync( QueryParser.Parse( text ), new SearchRequest { Query = text }, default );
                True( found.All( result => result.Kind != ResultKinds.Quicklink ) );
                if ( text != "cmd: docs" )
                {
                    True( found.Any( result => result.Kind == ResultKinds.File ) );
                }
            }
            var disabled = await provider.SearchAsync( QueryParser.Parse( "docs" ),
                new SearchRequest { Query = "docs", EnabledSearchSources = [SourceIds.Files] }, default );
            True( disabled.Any( result => result.Kind == ResultKinds.File ) );
            True( disabled.All( result => result.Kind != ResultKinds.Quicklink ) );
            var preferences = await store.GetPreferencesAsync( default );
            await store.ReplacePreferencesAsync( new LauncherPreferences
            {
                Sources = preferences.Sources.Select( source => new SourcePreference
                {
                    SourceId = source.SourceId,
                    EnabledInSearch = source.SourceId == SourceIds.Files,
                    EnabledOnHome = source.EnabledOnHome,
                    Position = source.Position
                } ).ToList( ),
                RegistryRevision = preferences.RegistryRevision,
                PreferenceRevision = preferences.PreferenceRevision
            }, default );
            var stored = await provider.SearchAsync( QueryParser.Parse( "docs" ), new SearchRequest { Query = "docs" }, default );
            True( stored.Any( result => result.Kind == ResultKinds.File ) );
            True( stored.All( result => result.Kind != ResultKinds.Quicklink ) );
        }
        finally { Directory.Delete( root, recursive: true ); }
    }

    private static async Task QuicklinkExportAndResetAsync( )
    {
        var root = CreateCliSpecRoot( );
        var database = Path.Combine( root, "links.db" );
        try
        {
            await using SqliteCatalog store = new( new StorageOptions { DatabasePath = database } );
            await store.ConfigureQuicklinksAsync( [TestQuicklink( "configured", alias: "configdocs" )], default );
            await store.ChangeQuicklinkAsync( "save", TestQuicklink( ), null, default );
            await store.ChangeQuicklinkAsync( "disable", null, "configured", default );
            await store.ReplacePreferencesAsync( CopyPreferencesWithFallback( await store.GetPreferencesAsync( default ), "docs" ), default );

            var backup = await store.ExportAsync( default );
            var json = System.Text.Json.JsonSerializer.Serialize( backup, CommandDeckJsonContext.Default.CustomizationExport );
            Equal( "https://example.test/?q={query}", backup.Quicklinks.UserQuicklinks.Single( ).Quicklink.UrlTemplate );
            Equal( "configured", backup.Quicklinks.DisabledConfiguredIds.Single( ) );

            await ExecuteCatalogSqlAsync( database, """
                CREATE TRIGGER reject_quicklink_reset BEFORE INSERT ON catalog_items
                WHEN new.kind='quicklink' BEGIN SELECT RAISE(ABORT,'publication rejected'); END;
                """ );
            await ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>( async ( ) => await store.ResetAsync( default ) );
            Equal( "docs", (await store.GetPreferencesAsync( default )).WebFallbackQuicklinkId );
            Equal( "quicklink:docs", (await store.FindAliasAsync( "docs", default ))?.Id );
            True( !(await store.GetQuicklinksAsync( default )).Quicklinks.Single( entry => entry.Source == "configured" ).Enabled );
            await ExecuteCatalogSqlAsync( database, "DROP TRIGGER reject_quicklink_reset;" );
            await store.ResetAsync( default );

            var reset = await store.GetQuicklinksAsync( default );
            Equal( 1, reset.Quicklinks.Count );
            True( reset.Quicklinks.Single( ).Enabled );
            True( await store.FindAliasAsync( "docs", default ) is null );
            Equal( "quicklink:configured", (await store.FindAliasAsync( "configdocs", default ))?.Id );

            var portable = System.Text.Json.JsonSerializer.Deserialize( json, CommandDeckJsonContext.Default.CustomizationExport );
            if ( portable is null )
            {
                throw new InvalidOperationException( "Missing portable backup." );
            }

            var restored = await store.RestoreQuicklinksAsync( portable.Quicklinks, portable.Preferences.WebFallbackQuicklinkId, default );
            Equal( "docs", restored.WebFallbackQuicklinkId );
            Equal( backup.Quicklinks.UserQuicklinks.Single( ).CreatedUnixMs,
                restored.Quicklinks.Single( entry => entry.Source == "user" ).CreatedUnixMs );
            True( !restored.Quicklinks.Single( entry => entry.Source == "configured" ).Enabled );
            Equal( "quicklink:docs", (await store.FindAliasAsync( "docs", default ))?.Id );
            True( await store.FindAliasAsync( "configdocs", default ) is null );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await store.RestoreQuicklinksAsync(
                new QuicklinkState { UserQuicklinks = [new QuicklinkEntry { Source = "user", Quicklink = TestQuicklink( "configured" ) }] }, null, default ) );
            Equal( "docs", (await store.GetPreferencesAsync( default )).WebFallbackQuicklinkId );
            await store.ReplaceAllAsync( FilterAliasItems( ), null, default );
            var file = (await store.SearchAsync( QueryParser.Parse( "file: roadmap" ), 20, default )).Single( );
            await store.SetAliasAsync( file, "blocked", default );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await store.RestoreQuicklinksAsync(
                new QuicklinkState { UserQuicklinks = [new QuicklinkEntry { Source = "user", Quicklink = TestQuicklink( "other", alias: "blocked" ) }] }, null, default ) );
            Equal( "quicklink:docs", (await store.FindAliasAsync( "docs", default ))?.Id );
            Equal( "docs", (await store.GetPreferencesAsync( default )).WebFallbackQuicklinkId );
            var unavailable = await store.RestoreQuicklinksAsync( portable.Quicklinks, "missing", default );
            True( unavailable.WebFallbackQuicklinkId is null );
            True( unavailable.Message?.Contains( "Web fallback reset to the default.", StringComparison.Ordinal ) == true );
            True( (await store.CheckIntegrityAsync( default )).Succeeded );
        }
        finally { Directory.Delete( root, recursive: true ); }
    }

    private static async Task QuicklinkAliasesShareNamespacesAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            await using SqliteCatalog store = new( new StorageOptions { DatabasePath = Path.Combine( root, "links.db" ) } );
            await store.ConfigureQuicklinksAsync( [TestQuicklink( "configured", alias: "configdocs" )], default );
            await store.ReplaceAllAsync( FilterAliasItems( ), null, default );
            var file = (await store.SearchAsync( QueryParser.Parse( "file: roadmap" ), 20, default )).Single( );
            await store.SetAliasAsync( file, "bookmark", default );
            await store.SetFilterAliasAsync( "filterdocs", "files", default );
            foreach ( var alias in new[] { "BOOKMARK", "FILTERDOCS", "configdocs", "app", "home", "settings" } )
            {
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await store.ChangeQuicklinkAsync( "save", TestQuicklink( alias: alias ), null, default ) );
                True( (await store.GetQuicklinksAsync( default )).Quicklinks.All( entry => entry.Source != "user" ) );
            }
            await store.ChangeQuicklinkAsync( "save", TestQuicklink( ), null, default );
            await store.ChangeQuicklinkAsync( "disable", null, "configured", default );
            foreach ( var alias in new[] { "DOCS", "CONFIGDOCS", "filterdocs" } )
            {
                await ThrowsAsync<InvalidOperationException>( async ( ) => await store.SetAliasAsync( file, alias, default ) );
            }
            foreach ( var alias in new[] { "DOCS", "CONFIGDOCS", "bookmark" } )
            {
                await ThrowsAsync<InvalidOperationException>( async ( ) => await store.SetFilterAliasAsync( alias, "files", default ) );
            }
            Equal( file.Id, (await store.FindAliasAsync( "bookmark", default ))?.Id );
            Equal( "quicklink:docs", (await store.FindAliasAsync( "docs", default ))?.Id );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await store.ConfigureQuicklinksAsync( [TestQuicklink( "configured", alias: "bookmark" )], default ) );
            Equal( "configdocs", (await store.GetQuicklinksAsync( default )).Quicklinks.Single( entry => entry.Source == "configured" ).Quicklink.Alias );
        }
        finally { Directory.Delete( root, recursive: true ); }
    }
}
