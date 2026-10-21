using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async IAsyncEnumerable<IndexedItem> QuicklinkSentinelItems( )
    {
        yield return ApplicationItem( "Unrelated", "spec" );
        await Task.CompletedTask;
    }

    private static QuicklinkOptions TestQuicklink( string id = "docs", string title = "Search docs",
        string url = "https://example.test/?q={query}", string? alias = "docs" ) => new( )
        { Id = id, Title = title, UrlTemplate = url, Alias = alias };

    private static async Task QuicklinkStorageIsAtomicAndPersistentAsync( )
    {
        var root = CreateCliSpecRoot( );
        var token = CancellationToken.None;
        var database = Path.Combine( root, "links.db" );
        try
        {
            await using ( SqliteCatalog store = new( new StorageOptions { DatabasePath = database } ) )
            {
                await store.ConfigureQuicklinksAsync( [TestQuicklink( "configured", alias: "cfgdocs" )], token );
                await store.ReplaceAllAsync( QuicklinkSentinelItems( ), null, token );
                var saved = await store.ChangeQuicklinkAsync( "save", TestQuicklink( ), null, token );
                True( saved.Quicklinks.Single( entry => entry.Source == "user" ).CreatedUnixMs > 0 );
                Equal( "quicklink:docs", (await store.FindAliasAsync( "DOCS", token ))?.Id );
                Equal( 1, (await store.SearchAsync( QueryParser.Parse( "link: Search docs" ), 20, token ))
                    .Count( result => result.Id == "quicklink:docs" ) );
                var preferences = await store.GetPreferencesAsync( token );
                await store.ReplacePreferencesAsync( CopyPreferencesWithFallback( preferences, "docs" ), token );

                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await store.ChangeQuicklinkAsync( "save", TestQuicklink( "configured" ), null, token ) );
                await store.ChangeQuicklinkAsync( "disable", null, "configured", token );
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await store.ChangeQuicklinkAsync( "save", TestQuicklink( "other", alias: "CFGDOCS" ), null, token ) );
                Equal( "docs", (await store.GetPreferencesAsync( token )).WebFallbackQuicklinkId );

                await store.ChangeQuicklinkAsync( "save", TestQuicklink( title: "Updated docs", alias: "newdocs" ), null, token );
                True( await store.FindAliasAsync( "docs", token ) is null );
                Equal( "Updated docs", (await store.FindAliasAsync( "newdocs", token ))?.Title );
                await store.ChangeQuicklinkAsync( "enable", null, "configured", token );
                await ExecuteCatalogSqlAsync( database, """
                    CREATE TRIGGER reject_quicklink_publication BEFORE INSERT ON catalog_items
                    WHEN new.kind='quicklink' BEGIN SELECT RAISE(ABORT,'publication rejected'); END;
                    """ );
                await ThrowsAsync<SqliteException>( async ( ) =>
                    await store.ChangeQuicklinkAsync( "remove", null, "docs", token ) );
                Equal( "docs", (await store.GetPreferencesAsync( token )).WebFallbackQuicklinkId );
                Equal( "Updated docs", (await store.FindAliasAsync( "newdocs", token ))?.Title );
                True( (await store.GetQuicklinksAsync( token )).Quicklinks.Any( entry => entry.Quicklink.Id == "docs" ) );
                await ExecuteCatalogSqlAsync( database, "DROP TRIGGER reject_quicklink_publication;" );

                var removed = await store.ChangeQuicklinkAsync( "remove", null, "docs", token );
                True( removed.Message?.Contains( "Web fallback reset to the default.", StringComparison.Ordinal ) == true );
                True( removed.WebFallbackQuicklinkId is null );
                True( (await store.GetPreferencesAsync( token )).WebFallbackQuicklinkId is null );
                True( await store.FindAliasAsync( "newdocs", token ) is null );
                True( (await store.SearchAsync( QueryParser.Parse( "app: Unrelated" ), 20, token )).Count > 0 );
                await store.ChangeQuicklinkAsync( "save", TestQuicklink( ), null, token );
                await store.ChangeQuicklinkAsync( "disable", null, "configured", token );
            }

            await using SqliteCatalog reopened = new( new StorageOptions { DatabasePath = database } );
            await reopened.ConfigureQuicklinksAsync( [TestQuicklink( "configured", alias: "cfgdocs" )], token );
            var persisted = await reopened.GetQuicklinksAsync( token );
            True( !persisted.Quicklinks.Single( entry => entry.Source == "configured" ).Enabled );
            Equal( "Search docs", persisted.Quicklinks.Single( entry => entry.Source == "user" ).Quicklink.Title );
            await reopened.ChangeQuicklinkAsync( "enable", null, "configured", token );
            await reopened.ReplacePreferencesAsync( CopyPreferencesWithFallback( await reopened.GetPreferencesAsync( token ), "configured" ), token );
            var disabled = await reopened.ChangeQuicklinkAsync( "disable", null, "configured", token );
            True( disabled.WebFallbackQuicklinkId is null );
            True( disabled.Message?.Contains( "default", StringComparison.Ordinal ) == true );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await reopened.ReplacePreferencesAsync(
                CopyPreferencesWithFallback( await reopened.GetPreferencesAsync( token ), "configured" ), token ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static LauncherPreferences CopyPreferencesWithFallback( LauncherPreferences preferences, string? id )
    {
        return new LauncherPreferences
        {
            Sources = preferences.Sources,
            RegistryRevision = preferences.RegistryRevision,
            PreferenceRevision = preferences.PreferenceRevision,
            WebFallbackQuicklinkId = id
        };
    }

    private static async Task QuicklinkGenerationCannotRestoreStaleRowsAsync( )
    {
        var root = CreateCliSpecRoot( );
        var token = CancellationToken.None;
        try
        {
            await using SqliteCatalog store = new( new StorageOptions { DatabasePath = Path.Combine( root, "links.db" ) } );
            await store.ConfigureQuicklinksAsync( [TestQuicklink( )], token );
            TaskCompletionSource staged = new( TaskCreationOptions.RunContinuationsAsynchronously );
            TaskCompletionSource resume = new( TaskCreationOptions.RunContinuationsAsynchronously );
            async IAsyncEnumerable<IndexedItem> Scan( )
            {
                yield return ConfiguredItemSource.CreateQuicklinkItem( TestQuicklink( ) );
                staged.SetResult( );
                await resume.Task;
            }
            var rebuild = store.ReplaceAllAsync( Scan( ), null, token ).AsTask( );
            await staged.Task;
            await store.ChangeQuicklinkAsync( "disable", null, "docs", token );
            await store.ChangeQuicklinkAsync( "save", TestQuicklink( "new", "New search", alias: "new" ), null, token );
            resume.SetResult( );
            await rebuild;
            var results = await store.SearchAsync( QueryParser.Parse( "link:" ), 20, token );
            True( results.All( value => value.Id != "quicklink:docs" ) );
            True( results.Any( value => value.Id == "quicklink:new" ) );
            True( (await store.CheckIntegrityAsync( token )).Succeeded );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void QuicklinkValidationMatchesConfiguration( )
    {
        foreach ( var link in new[]
        {
            TestQuicklink( "bad id" ), TestQuicklink( title: "" ), TestQuicklink( title: new string( 'x', 65 ) ),
            TestQuicklink( url: "javascript:alert(1)" ), TestQuicklink( url: "https://user:pass@example.test" ),
            TestQuicklink( url: "https://example.test/{other}" ), TestQuicklink( url: new string( 'x', 2049 ) ),
            TestQuicklink( alias: "bad alias" ), TestQuicklink( alias: new string( 'x', 33 ) ),
            TestQuicklink( alias: "app" ), TestQuicklink( alias: "home" ), TestQuicklink( alias: "settings" )
        } )
        {
            string? message = null;
            try
            { QuicklinkValidator.Validate( link ); }
            catch ( InvalidOperationException exception ) { message = exception.Message; }
            True( message is not null );
            string? configuredMessage = null;
            try
            {
                ConfigurationValidator.Validate( new CommandDeckOptions { Quicklinks = [link] } );
            }
            catch ( InvalidOperationException exception )
            {
                configuredMessage = exception.Message;
            }
            Equal( message, configuredMessage );
        }
        var fixture = File.ReadAllText( Path.Combine( FindRepositoryRoot( ), "tests", "fixtures", "QuicklinkUrls.js" ) );
        using var cases = JsonDocument.Parse( fixture[fixture.IndexOf( '[' )..] );
        foreach ( var entry in cases.RootElement.EnumerateArray( ) )
        {
            string? error = null;
            var alias = entry.TryGetProperty( "alias", out var aliasValue ) ? aliasValue.GetString( ) : "docs";
            try
            { QuicklinkValidator.Validate( TestQuicklink( url: entry.GetProperty( "url" ).GetString( )!, alias: alias ) ); }
            catch ( InvalidOperationException exception ) { error = exception.Message; }
            var expected = entry.TryGetProperty( "error", out var errorValue ) ? errorValue.GetString( ) : "";
            Equal( expected, error ?? "" );
        }
        QuicklinkValidator.Validate( TestQuicklink( url: "https://example.test/{QUERY}?q={query}" ) );
        QuicklinkValidator.Validate( TestQuicklink( url: "https://example.test/static" ) );
        Equal( "https://example.test/?q=a%20b%26c", new UrlActionPayload( TestQuicklink( ).UrlTemplate ).Expand( "a b&c" ) );
    }
}
