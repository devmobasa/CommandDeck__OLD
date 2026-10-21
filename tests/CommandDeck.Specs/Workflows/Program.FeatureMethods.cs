namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task WebFallbackDoesNotConnectBeforeInvocationAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-fallback-network-spec-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        System.Net.Sockets.TcpListener listener = new(
            System.Net.IPAddress.Loopback,
            0 );
        listener.Start( );
        try
        {
            var port = (( System.Net.IPEndPoint ) listener.LocalEndpoint).Port;
            var config = Path.Combine( root, "config.json" );
            var defaults = LauncherPreferences.CreateDefault( );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions
                {
                    DatabasePath = Path.Combine( root, "catalog.db" )
                },
                Indexing = new IndexingOptions
                {
                    Roots = [],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false
                },
                Quicklinks =
                [
                    new QuicklinkOptions
                    {
                        Id = "local-web",
                        Title = "Local web",
                        UrlTemplate = $"http://127.0.0.1:{port}/?q={{query}}"
                    }
                ],
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            await File.WriteAllTextAsync(
                config,
                System.Text.Json.JsonSerializer.Serialize(
                    options,
                    CommandDeckJsonContext.Default.CommandDeckOptions ) );
            await using var runtime = await CommandDeckRuntime.CreateAsync(
                config,
                CancellationToken.None );
            await runtime.Preferences.ReplacePreferencesAsync( new LauncherPreferences
            {
                Sources = defaults.Sources.Select( source => new SourcePreference
                {
                    SourceId = source.SourceId,
                    EnabledInSearch = source.SourceId == SourceIds.WindowsWorkspaces
                        ? false
                        : source.EnabledInSearch,
                    EnabledOnHome = source.EnabledOnHome,
                    Position = source.Position
                } ).ToList( ),
                WebFallbackQuicklinkId = "local-web"
            }, CancellationToken.None );

            var results = await runtime.SearchAsync(
                new SearchRequest { Query = "no local result", Limit = 10 },
                CancellationToken.None );
            Equal( "fallback", results.Single( ).ProviderId );
            True( !listener.Pending( ) );
        }
        finally
        {
            listener.Stop( );
            Directory.Delete( root, recursive: true );
        }
    }

    private static void FilterRegistryDrivesQueries( )
    {
        var raw = QueryParser.Parse( "text: notes" );
        Equal( "files", raw.IncludedFilters.Single( ).Id );
        Equal( SourceIds.Files, raw.IncludedFilters.Single( ).SourceId );
        Equal( ResultKinds.File, raw.IncludedFilters.Single( ).Kind );
        var explicitFilter = QueryParser.Parse( "notes", new QueryExpression
        {
            IncludeFilterIds = ["files"]
        } );
        Equal( "catalog", explicitFilter.ProviderFilter );
        Throws<InvalidOperationException>( ( ) => QueryParser.Parse( "app: brave", new QueryExpression
        {
            ExcludeFilterIds = ["applications"]
        } ) );
        Throws<InvalidOperationException>( ( ) => AliasValidator.NormalizeAndValidate( "app" ) );
    }

    private static void FilterTokensAndAliasesAllResolve( )
    {
        // The ergonomic plurals the README has always advertised.
        Equal( "workspaces", QueryParser.Parse( "ws: 3" ).IncludedFilters.Single( ).Id );
        Equal( "workspaces", QueryParser.Parse( "workspaces: 3" ).IncludedFilters.Single( ).Id );
        Equal( "files", QueryParser.Parse( "files: notes" ).IncludedFilters.Single( ).Id );

        // Every token the registry publishes must parse back to its own filter,
        // so adding an alias without wiring it up fails here rather than in the
        // launcher.
        HashSet<string> seen = new( StringComparer.OrdinalIgnoreCase );
        foreach ( var descriptor in SearchFilterRegistry.All )
        {
            foreach ( var token in descriptor.Aliases.Prepend( descriptor.Token ) )
            {
                True( token.EndsWith( ':' ) );
                True( seen.Add( token ) );
                Equal( descriptor.Id, QueryParser.Parse( $"{token} value" ).IncludedFilters.Single( ).Id );
                Equal( descriptor.Id, SearchFilterRegistry.FindByToken( token )?.Id );
                True( SearchFilterRegistry.IsReserved( token.TrimEnd( ':' ) ) );
            }
        }
    }

    private static void OmarchyCurrentMenuRoutesPreserveIds( )
    {
        const string defaults = """
            {
              // current dotted schema
              "apps": {"label":"Apps","provider":"apps"},
              "learn": {"label":"Learn"},
              "learn.docs": {"label":"Docs","action":"open-docs"},
              "learn.hidden": {"label":"Hidden default","action":"must-not-survive"},
              "system": {"label":"System"},
              "system.reboot": {"label":"Reboot","action":"reboot"},
              "system.shutdown": {"label":"Shutdown","action":"shutdown"},
            }
            """;
        const string extension = """
            {
              "learn.docs": {"label":"Custom Docs","action":"open-custom-docs"},
              "learn.hidden": {"label":"Disabled by replacement"},
              "trigger.custom": {"label":"Custom","action":"custom-action"}
            }
            """;
        var items = OmarchyMenuScanner.ParseDocuments(
        [
            new OmarchyMenuDocument("default.jsonc", defaults),
            new OmarchyMenuDocument("extension.jsonc", extension)
        ] );
        True( items.Any( item => item.StableId == "omarchy:learn.docs" &&
            item.Title == "Custom Docs" && item.PayloadJson?.Contains(
                "open-custom-docs",
                StringComparison.Ordinal ) == true ) );
        True( items.All( item => item.StableId != "omarchy:learn.hidden" ) );
        True( items.Any( item => item.StableId == "omarchy:trigger.custom" ) );
        True( items.Any( item => item.StableId == "omarchy-route:root" &&
            item.SourceId == SourceIds.OmarchyControl ) );
        True( items.Any( item => item.StableId == "omarchy-route:system" &&
            item.SourceId == SourceIds.OmarchySystem ) );
        True( items.All( item => !item.StableId.Contains( "system.reboot", StringComparison.Ordinal ) &&
            !item.StableId.Contains( "system.shutdown", StringComparison.Ordinal ) ) );
        True( items.All( item => item.StableId != "omarchy:apps" ) );
    }

    private static void ShortcutInspectionReportsEffectiveChords( )
    {
        const string binds = """
            [
              {"modmask":64,"key":"Q","dispatcher":"exec","arg":"omarchy-shell toggle local.commanddeck"},
              {"modmask":72,"key":"space","dispatcher":"exec","arg":"omarchy-menu toggle apps"},
              {"modmask":64,"key":"escape","dispatcher":"exec","arg":"omarchy-menu toggle system"},
              {"modmask":4,"key":"space","dispatcher":"__lua","arg":"39","description":"CommandDeck"},
              {"modmask":64,"key":"Q","dispatcher":"__lua","arg":"251","description":"Omarchy menu"}
            ]
            """;
        var shortcuts = HyprlandShortcutInspector.Parse( binds );
        True( shortcuts.Single( value => value.SurfaceId == "commanddeck" ).Chords
            .SequenceEqual( ["Ctrl+Space", "Super+Q"] ) );
        Equal( "Super+Alt+Space", shortcuts.Single( value => value.SurfaceId == "applications" ).Chords[0] );
        Equal( "Super+Escape", shortcuts.Single( value => value.SurfaceId == "system-menu" ).Chords[0] );
        Equal( "Super+Q", shortcuts.Single( value => value.SurfaceId == "omarchy-control" ).Chords[0] );
    }

    private static async Task ShortcutInspectionDistinguishesFailuresAsync( )
    {
        var unconfigured = HyprlandShortcutInspector.Parse( "[]" );
        True( unconfigured.All( value => value.Status == ShortcutStatuses.NotConfigured ) );
        var malformed = HyprlandShortcutInspector.Parse( "not-json" );
        True( malformed.All( value => value.Status == ShortcutStatuses.NotConfigured ) );
        True( malformed.All( value => value.Detail?.Contains( "malformed", StringComparison.Ordinal ) == true ) );
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var directory = SpecTemporaryFiles.PathFor( $"commanddeck-shortcut-spec-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( directory );
        var executable = Path.Combine( directory, "hyprctl" );
        var previousPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            Environment.SetEnvironmentVariable( "PATH", directory );
            var missing =
                await HyprlandShortcutInspector.InspectAsync( CancellationToken.None );
            True( missing.All( value => value.Status == ShortcutStatuses.NotConfigured ) );
            True( missing.All( value => value.Detail?.Contains( "PATH", StringComparison.Ordinal ) == true ) );

            await File.WriteAllTextAsync( executable, "#!/bin/sh\nexit 7\n" );
            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            var failed =
                await HyprlandShortcutInspector.InspectAsync( CancellationToken.None );
            True( failed.All( value => value.Status == ShortcutStatuses.NotConfigured ) );

            await File.WriteAllTextAsync( executable, "#!/bin/sh\n/usr/bin/sleep 2\nprintf '[]'\n" );
            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            var timedOut =
                await HyprlandShortcutInspector.InspectAsync( CancellationToken.None );
            True( timedOut.All( value => value.Status == ShortcutStatuses.NotConfigured ) );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", previousPath );
            Directory.Delete( directory, recursive: true );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> CustomizationItems( bool includeFile )
    {
        yield return new IndexedItem
        {
            StableId = "app:brave.desktop",
            Kind = ResultKinds.Application,
            Title = "Brave",
            PayloadJson = PayloadWriter.Create(
                ("desktopFile", "/usr/share/applications/brave.desktop"),
                ("desktopId", "brave.desktop"),
                ("exec", "brave %U") ),
            ProviderId = "applications",
            SourceId = SourceIds.Applications
        };
        if ( includeFile )
        {
            yield return new IndexedItem
            {
                StableId = "file:notes",
                Kind = ResultKinds.File,
                Title = "notes.txt",
                Path = "/tmp/notes.txt",
                ProviderId = "files",
                SourceId = SourceIds.Files
            };
        }

        await Task.CompletedTask;
    }

    private static async Task FilePreviewEnforcesBoundsAndPathPolicyAsync( )
    {
        var sandbox = SpecTemporaryFiles.PathFor( $"commanddeck-preview-spec-{Guid.NewGuid( ):N}" );
        var root = Path.Combine( sandbox, "root" );
        var outside = Path.Combine( sandbox, "outside" );
        Directory.CreateDirectory( root );
        Directory.CreateDirectory( outside );

        try
        {
            var linesPath = Path.Combine( root, "lines.md" );
            await File.WriteAllTextAsync(
                linesPath,
                string.Join( '\n', Enumerable.Range( 1, 250 ).Select( number => $"line {number}" ) ) );
            var service = PreviewService(
                root,
                followSymbolicLinks: false,
                neverIndex: [Path.Combine( root, "denied.txt" )] );

            var lines = await service.ReadAsync(
                linesPath,
                CancellationToken.None );
            True( lines.Succeeded );
            True( lines.Preview?.Truncated == true );
            Equal( "markdown", lines.Preview?.Language );
            Equal( 200, lines.Preview?.Text.Split( '\n' ).Length );

            var bytesPath = Path.Combine( root, "long.txt" );
            await File.WriteAllTextAsync(
                bytesPath,
                new string( 'x', ProtocolLimits.MaximumPreviewBytes + 100 ) );
            var bytes = await service.ReadAsync(
                bytesPath,
                CancellationToken.None );
            True( bytes.Preview?.Truncated == true );
            Equal( ProtocolLimits.MaximumPreviewBytes, bytes.Preview?.Text.Length );

            var deniedPath = Path.Combine( root, "denied.txt" );
            await File.WriteAllTextAsync( deniedPath, "must not be returned" );
            True( !(await service.ReadAsync( deniedPath, CancellationToken.None )).Succeeded );

            var binaryPath = Path.Combine( root, "binary.dat" );
            await File.WriteAllBytesAsync( binaryPath, [1, 0, 2] );
            var binary = await service.ReadAsync(
                binaryPath,
                CancellationToken.None );
            True( !binary.Succeeded );
            True( !binary.Error?.Contains( "must not", StringComparison.Ordinal ) == true );

            var outsidePath = Path.Combine( outside, "secret.txt" );
            await File.WriteAllTextAsync( outsidePath, "outside secret" );
            var linkPath = Path.Combine( root, "escape.txt" );
            File.CreateSymbolicLink( linkPath, outsidePath );
            var following = PreviewService(
                root,
                followSymbolicLinks: true,
                neverIndex: [] );
            var escaped = await following.ReadAsync(
                linkPath,
                CancellationToken.None );
            True( !escaped.Succeeded );
            True( !escaped.Error?.Contains( "outside secret", StringComparison.Ordinal ) == true );

            using CancellationTokenSource canceled = new( );
            canceled.Cancel( );
            await ThrowsAsync<OperationCanceledException>( async ( ) =>
                await service.ReadAsync( linesPath, canceled.Token ) );
        }
        finally
        {
            Directory.Delete( sandbox, recursive: true );
        }
    }

    private static FilePreviewService PreviewService(
        string root,
        bool followSymbolicLinks,
        List<string> neverIndex,
        ImagePreviewRenderer? images = null ) =>
        new(
            new IndexingOptions
            {
                Roots =
                [
                    new IndexRootOptions
                    {
                        Path = root,
                        IncludeHidden = false,
                        IncludeContentInSearch = true,
                        FollowSymbolicLinks = followSymbolicLinks
                    }
                ]
            },
            new PrivacyOptions { NeverIndex = neverIndex }, images );

    private static void StructuredConfigArraysUseSemanticElementNames( )
    {
        const string json = """
            {
              "entries": [
                {
                  "name": "outer",
                  "entries": [
                    { "name": "inner", "value": "named" },
                    { "name": "inner", "value": "duplicate" },
                    { "id": "by-id", "value": "identified" },
                    { "title": "By title", "value": "titled" },
                    { "value": "indexed" }
                  ]
                }
              ]
            }
            """;

        var entries = StructuredConfigExtractor.Extract(
            "/tmp/config.json",
            json );
        True( entries.Any( entry => entry.Key == "entries[outer].entries[inner].name" ) );
        True( entries.Any( entry => entry.Key == "entries[outer].entries[1].value" ) );
        True( entries.Any( entry => entry.Key == "entries[outer].entries[by-id].value" ) );
        True( entries.Any( entry => entry.Key == "entries[outer].entries[By title].value" ) );
        True( entries.Any( entry => entry.Key == "entries[outer].entries[4].value" ) );
        True( entries.All( entry => !entry.Key.Contains( "entries[0].entries", StringComparison.Ordinal ) ) );
    }

    private static async Task DetailFieldsRemainOrderedAndSerializeAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-detail-spec-{Guid.NewGuid( ):N}.db" );
        try
        {
            SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync( DetailFieldItems( ), null, CancellationToken.None );

            var results = await catalog.SearchAsync(
                QueryParser.Parse( string.Empty ),
                20,
                CancellationToken.None );
            AssertDetailLabels( results, ResultKinds.Application,
                "Desktop file", "Desktop ID", "Command", "Terminal" );
            AssertDetailLabels( results, ResultKinds.Configuration,
                "File", "Key location", "Value location", "Key", "Section", "Value" );
            AssertDetailLabels( results, ResultKinds.File, "Path" );
            AssertDetailLabels( results, ResultKinds.Omarchy, "Command", "Source" );
            AssertDetailLabels( results, ResultKinds.Quicklink, "URL", "Alias" );
            AssertDetailLabels( results, ResultKinds.CustomCommand,
                "Command", "Working directory", "Confirmation" );

            SearchCoordinator coordinator = new(
                [new CalculatorProvider( )],
                new SearchOptions( ) );
            var calculation = (await coordinator.SearchAsync(
                new SearchRequest { Query = "= 2 + 2", Limit = 5 },
                CancellationToken.None ))[0];
            Equal( "Expression", calculation.Details[0].Label );
            True( calculation.Details[0].Monospace );
            True( !calculation.Metadata.ContainsKey( "queryText" ) );

            ProtocolMessage message = new( )
            {
                RequestId = 1,
                Type = ProtocolMessageTypes.Result,
                Result = calculation
            };
            var json = System.Text.Json.JsonSerializer.Serialize(
                message,
                CommandDeckJsonContext.Default.ProtocolMessage );
            var roundTrip = System.Text.Json.JsonSerializer.Deserialize(
                json,
                CommandDeckJsonContext.Default.ProtocolMessage );
            Equal( "Expression", roundTrip?.Result?.Details[0].Label );
            Equal( "2 + 2", roundTrip?.Result?.Details[0].Value );
            True( roundTrip?.Result?.Details[0].Monospace == true );

            await catalog.DisposeAsync( );
        }
        finally
        {
            foreach ( var suffix in new[] { string.Empty, "-shm", "-wal" } )
            {
                File.Delete( database + suffix );
            }
        }
    }


    private static void ConfigurableSourcesShipLabelAndIcon( )
    {
        // The settings panel renders one row per configurable source. A source
        // without a label or icon is indistinguishable from every other row.
        foreach ( var descriptor in LauncherSourceRegistry.Configurable )
        {
            True( !string.IsNullOrWhiteSpace( descriptor.Label ) );
            True( !string.IsNullOrWhiteSpace( descriptor.Icon ) );
        }

        Equal(
            LauncherSourceRegistry.Configurable.Count,
            LauncherSourceRegistry.Configurable
                .Select( static descriptor => descriptor.Label )
                .Distinct( StringComparer.Ordinal )
                .Count( ) );
    }
}
