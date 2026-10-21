namespace CommandDeck.Specs;

public static partial class Program
{
    private static SearchResult[] HeyFallbackApplications( int count ) => [.. Enumerable.Range( 1, count ).Select( static index => new SearchResult
    {
        Id = $"app:firefox-{index}",
        Kind = ResultKinds.Application,
        Title = $"Firefox profile {index}",
        SourceId = SourceIds.Applications,
        ProviderId = ProviderIds.Applications,
        Score = 0.9 - (index * 0.01),
        Actions = [new( ) { Id = "launch", Title = "Launch", Kind = ActionKinds.LaunchDesktop }]
    } )];

    // Plain words get one trailing row that moves them into hey:. It runs
    // nothing, takes no Top Match or first-page slot, and never reaches
    // suggestions or the assistant.
    private static async Task HeyFallbackTrailsRankedResultsAsync( )
    {
        HeyFake fake = new( );
        var provider = HeyProviderFor( fake );
        SearchCoordinator search = new( [new StaticSearchProvider( HeyFallbackApplications( 12 ) ), provider], new SearchOptions( ) );

        var rows = await search.SearchAsync( new SearchRequest { Query = "firefox" }, CancellationToken.None );
        Equal( 13, rows.Count );
        Equal( 12, rows.Count( static row => row.SourceId == SourceIds.Applications ) );
        var fallback = rows[^1];
        Equal( "hey:fallback", fallback.Id );
        Equal( "Search HEY for “firefox”", fallback.Title );
        Equal( "HEY", fallback.Section?.Title );
        True( !fallback.CanCustomize && PrivateSources.IsPrivate( fallback ) && ResultPlacements.IsTrailing( fallback ) );
        Equal( ActionKinds.ApplyQuery, fallback.Actions.Single( ).Kind );
        Equal( "firefox", fallback.Actions[0].Arguments["query"] );
        Equal( HeySearch.FilterId, fallback.Actions[0].Arguments["filterId"] );
        True( rows.Take( 12 ).All( static row => row.SourceId == SourceIds.Applications ) );

        // Even alone it stays a trailing row, not a Top Match.
        var alone = await new SearchCoordinator( [provider], new SearchOptions( ) )
            .SearchAsync( new SearchRequest { Query = "quarterly planning" }, CancellationToken.None );
        Equal( "hey-fallback", alone.Single( ).Section?.Id );

        foreach ( var query in new[] { "fi", "app: firefox", "> firefox", "= 2+2", "-hey: firefox", "title:firefox" } )
        {
            var none = await search.SearchAsync( new SearchRequest { Query = query }, CancellationToken.None );
            True( none.All( static row => row.Id != "hey:fallback" ), $"no fallback for {query}" );
        }

        foreach ( var purpose in new[] { SearchPurpose.Suggestion, SearchPurpose.AssistantTool } )
        {
            var hidden = await search.SearchAsync( new SearchRequest { Query = "firefox" }, purpose, CancellationToken.None );
            True( hidden.All( static row => row.SourceId != SourceIds.Hey ), "private rows never leave the interactive launcher" );
        }

        var notInstalled = await new SearchCoordinator( [HeyProviderFor( fake, installed: false )], new SearchOptions( ) )
            .SearchAsync( new SearchRequest { Query = "firefox" }, CancellationToken.None );
        Equal( 0, notInstalled.Count );
        Equal( 0, fake.Calls.Count );

        // A disabled HEY source stops the fallback too.
        var preferences = LauncherPreferences.CreateDefault( );
        var disabled = new LauncherPreferences
        {
            Sources = [.. preferences.Sources.Select( static source => new SourcePreference
            {
                SourceId = source.SourceId,
                EnabledInSearch = source.SourceId != SourceIds.Hey && source.EnabledInSearch,
                EnabledOnHome = source.EnabledOnHome,
                Position = source.Position
            } )]
        };
        True( ProviderEligibilityPolicy.Select( [provider], QueryParser.Parse( "firefox" ), preferences, null ).Any( ) );
        True( !ProviderEligibilityPolicy.Select( [provider], QueryParser.Parse( "firefox" ), disabled, null ).Any( ) );
    }

    // The web fallback still appears when nothing else can act, ahead of the
    // HEY row, in the real runtime.
    private static async Task HeyFallbackKeepsTheWebFallbackFirstAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-hey-fallback-" ).FullName;
        try
        {
            var hey = Path.Combine( root, "hey" );
            await File.WriteAllTextAsync( hey, "#!/bin/sh\nexit 0\n" );
            File.SetUnixFileMode( hey, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( HeyExecutable.OverrideVariable, hey );

            var config = Path.Combine( root, "config.json" );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Indexing = new IndexingOptions { Roots = [], IndexDesktopApplications = false, IndexOmarchyCommands = false },
                Quicklinks = [new QuicklinkOptions { Id = "local-web", Title = "Local web", UrlTemplate = "http://127.0.0.1:9/?q={query}" }],
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            await File.WriteAllTextAsync( config, System.Text.Json.JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None );
            var defaults = LauncherPreferences.CreateDefault( );
            await runtime.Preferences.ReplacePreferencesAsync( new LauncherPreferences
            {
                Sources = [.. defaults.Sources.Select( static source => new SourcePreference
                {
                    SourceId = source.SourceId,
                    EnabledInSearch = source.SourceId != SourceIds.WindowsWorkspaces && source.EnabledInSearch,
                    EnabledOnHome = source.EnabledOnHome,
                    Position = source.Position
                } )],
                WebFallbackQuicklinkId = "local-web"
            }, CancellationToken.None );

            var rows = await runtime.SearchAsync( new SearchRequest { Query = "no local result", Limit = 10 }, CancellationToken.None );
            Equal( "fallback,hey", string.Join( ',', rows.Select( static row => row.ProviderId ) ) );
            Equal( "hey:fallback", rows[1].Id );
        }
        finally
        {
            Environment.SetEnvironmentVariable( HeyExecutable.OverrideVariable, HeyExecutable.OverrideOff );
            Directory.Delete( root, recursive: true );
        }
    }
}
