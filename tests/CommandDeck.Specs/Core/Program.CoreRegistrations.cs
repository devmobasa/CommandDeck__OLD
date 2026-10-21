namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunCoreRegistrationsAsync( )
    {
        Run( "quicklinks configuration and management share validation and placeholder rules", QuicklinkValidationMatchesConfiguration );
        await RunAsync( "display sizing CLI chooses its profile before creating a fresh catalog",
            DisplaySizingCliMigratesBeforeCatalogCreationAsync );
        await RunAsync( "display sizing migration distinguishes fresh installs from existing default users",
            DisplaySizingMigrationPreservesExistingUsersAsync );
        await RunAsync( "workspace provider preserves identity and orders recent use occupied workspaces and numeric IDs", WorkspacePreviewProviderIdentityAsync );
        await RunAsync( "Hyprland workspace and window actions use Lua dispatch acknowledgements", HyprlandActionDispatchAsync );
        Run( "window placement geometry matches the golden table", WindowPlacementGeometryMatchesGoldenTable );
        await RunAsync( "window commands bind the window focused before the launcher opened", WindowCommandsBindTheWindowFocusedBeforeTheLauncherAsync );
        await RunAsync( "window commands dispatch only to the bound window address", WindowCommandsDispatchToTheBoundAddressAsync );
        await RunAsync( "workspace preview preferences persist export reset and reject invalid writes", WorkspacePreviewPreferencesAsync );
        await RunAsync( "spec selection lists filters and reports failures without running unrelated fixtures", SpecSelectionReportsAsync );
        Run( "result cache bounds bursts refreshes and deterministic expiry", ResultCacheBoundsAndExpiry );
        Run( "detached undo cache bounds expiry and stable-ID isolation", DetachedUndoCacheBoundsAndExpiry );
        await RunAsync( "invocation snapshot rejects a replaced action target", InvocationSnapshotRejectsReplacedActionAsync );
        await RunAsync( "preview matching line inherits the originating query privacy and text",
            PreviewMatchingLineInheritsQueryPrivacyAsync );
        await RunAsync( "file selection admits only the selecting client's result snapshots",
            FileSelectionAdmitsOnlyOwnedSnapshotsAsync );
        await RunAsync( "invocation undo reference survives the refresh that replaces its row", InvocationUndoSurvivesTheRefreshAsync );
        await RunAsync( "runtime startup rollback releases owners and continues after cleanup failures", RuntimeResourcesUnwindAfterFailureAsync );
        await RunAsync( "provider capacity counts late work without starving a fast provider", ProviderCapacityRetainsLateWorkAsync );
        await RunAsync( "cancelled catalog search interrupts SQLite and frees its provider slot",
            CancelledCatalogSearchReleasesProviderSlotsAsync );
        await RunAsync( "search outcome distinguishes failed providers from empty results",
            SearchOutcomeDistinguishesFailedProviderAsync );
        await RunAsync( "search plan freezes the folder request during provider work",
            SearchPlanFreezesFolderRequestAsync );
        await RunAsync( "search plan freezes catalog sources while preferences change during provider work",
            SearchPlanFreezesCatalogSourcesAsync );
        await RunAsync( "catalog source snapshot distinguishes omitted and empty source sets",
            CatalogSourceSnapshotDistinguishesOmittedAndEmptyAsync );
        await RunAsync( "search socket reports incomplete execution without diagnostics",
            SearchSocketReportsIncompleteWithoutDiagnosticsAsync );
        await RunAsync( "Home provider deadlines retain partial results and late-work capacity", HomeProviderDeadlineRetainsCapacityAsync );
        await RunAsync( "Home runs its catalog and live sections concurrently", HomeRunsLiveSectionsConcurrentlyAsync );
        await RunAsync( "server bounds clients and drains shutdown with a stalled reader", ServerBoundsAndDrainsConnectionsAsync );
        await RunAsync( "duplicate active request closes its client connection",
            DuplicateActiveRequestClosesSocketAsync );
        await RunAsync( "unknown operations are rejected without running a neighbour", UnknownOperationsRunNoNeighbourAsync );
        await RunAsync( "every routed operation reaches the handler that serves it", EveryRoutedOperationReachesItsHandlerAsync );
        await RunAsync( "process runner reaps descendants retaining output after parent exit",
            ProcessRunnerReapsOutputDescendantAsync );
        await RunAsync( "general process runner bounds captured output",
            ProcessRunnerBoundsDefaultOutputAsync );
        await RunAsync( "process runner reports error output overflow apart from output",
            ProcessRunnerReportsStandardErrorOverflowSeparatelyAsync );
        await RunAsync( "agent tool routing checks live turn and trust admission", ToolRoutingUsesLiveAdmissionAsync );
        await RunAsync( "editor payload validates positions before process execution", EditorPayloadRejectsInvalidArgumentsAsync );
        Run(
            "architecture boundaries forbid reversed dependencies and ambient capabilities",
            ArchitectureBoundariesAreEnforced );
        Run(
            "generated current protocol contract matches Core QML and documentation",
            GeneratedProtocolContractMatchesCurrentTree );
        Run( "protocol routes cover every operation once with its admission",
            ProtocolRoutesCoverEveryOperationOnce );
        Run( "QML constants match Core and every protocol reference resolves",
            QmlConstantsMatchCoreAndReferencesResolve );
        Run( "quality gate keeps hosted latency observational while enforcing stable budgets",
            QualityGateKeepsHostedLatencyObservational );
        Run(
            "generated permission and configuration references match authoritative code",
            GeneratedReferenceTablesMatchAuthoritativeCode );
        Run(
            "optional tool catalog prints an exact pacman command and never executes it",
            OptionalToolCatalogPrintsExactPacmanCommand );
        await RunAsync(
            "config init writes a schema hint",
            ConfigInitWritesSchemaHintAsync );
        await RunAiRegistrationsAsync( );

        Run( "query parser: config", ( ) =>
        {
            var query = QueryParser.Parse( "cfg: gaps_out" );
            Equal( "catalog", query.ProviderFilter );
            Equal( "gaps_out", query.Text );
        } );

        Run( "query algebra composes filters fields exclusions and quoted text", ( ) =>
            QueryAlgebraParsesDeterministically( ) );
        Run( "query algebra rejects malformed and conflicting expressions", ( ) =>
            QueryAlgebraRejectsMalformedExpressions( ) );
        await RunAsync( "assistant search preserves literal prompt punctuation and whitespace",
            AssistantSearchPreservesLiteralPromptAsync );
        await RunAsync(
            "query algebra applies filter union field intersection and exclusion",
            QueryAlgebraEvaluatesAcrossProvidersAsync );
        await RunAsync(
            "search returns at the provider deadline and reports late providers once",
            SearchReportsProviderDeadlinesAsync );
        await RunAsync(
            "search skips the application fast path when filters would discard it",
            SearchSkipsApplicationFastPathWhenFilteredOutAsync );
        await RunAsync(
            "search keeps the exact-alias copy of a duplicate on a tied score",
            SearchKeepsExactAliasCopyOnTiedScoreAsync );
        Run( "search logging picks levels by outcome and duration", SearchLoggingChoosesLevels );

        Run( "query parser: calculator", ( ) =>
        {
            var query = QueryParser.Parse( "= 2 * (3 + 4)" );
            True( query.IsCalculator );
            Equal( "2 * (3 + 4)", query.Text );
        } );

        Run( "calculator precedence", ( ) =>
        {
            True( Calculator.TryEvaluate( "2 + 3 * 4", out var value, out _ ) );
            Equal( 14d, value );
        } );

        Run( "fuzzy prefix outranks subsequence", ( ) =>
        {
            var prefix = FuzzyMatcher.Score( "fire", "Firefox" );
            var subsequence = FuzzyMatcher.Score( "fire", "fxxixxrxxe" );
            True( prefix > subsequence );
            True( prefix > FuzzyMatcher.Score( "fire", "firefox-notes.md" ) );
            True( prefix > FuzzyMatcher.Score( "fire", "Fast Indexing Runtime Engine" ) );
        } );

        Run( "fuzzy match tiers remain monotone", ( ) =>
        {
            var exact = FuzzyMatcher.Score( "fire", "fire" );
            var prefix = FuzzyMatcher.Score( "fire", "Firefox" );
            var boundary = FuzzyMatcher.Score( "fire", "Open Firefox" );
            var acronym = FuzzyMatcher.Score( "fire", "Fast Index Runtime Engine" );
            var typo = FuzzyMatcher.Score( "fire", "fier" );
            var subsequence = FuzzyMatcher.Score( "fire", "fxxixxrxxe" );

            if ( !(exact > prefix && prefix > boundary && boundary > acronym &&
                  acronym > typo && typo > subsequence) )
            {
                throw new InvalidOperationException(
                    $"Unexpected tiers: exact={exact:F3}, prefix={prefix:F3}, " +
                    $"boundary={boundary:F3}, acronym={acronym:F3}, " +
                    $"typo={typo:F3}, subsequence={subsequence:F3}." );
            }
        } );

        Run( "fuzzy matching tolerates a transposed-letter typo", ( ) =>
        {
            var typo = FuzzyMatcher.Score( "friefox", "Firefox" );
            var unrelated = FuzzyMatcher.Score( "friefox", "Chromium" );

            True( typo >= 0.55 );
            Equal( 0d, unrelated );
            Equal(
                new MatchedRange( 0, "Firefox".Length ),
                FuzzyMatcher.FindRanges( "friefox", "Firefox" ).Single( ) );
        } );

        Run( "fuzzy matching recognizes word and camel-case acronyms", ( ) =>
        {
            var words = FuzzyMatcher.Score( "vsc", "Visual Studio Code" );
            var camelCase = FuzzyMatcher.Score( "csc", "CommandSearchController" );

            True( words >= 0.66 );
            True( camelCase >= 0.66 );
            True( FuzzyMatcher.FindRanges( "vsc", "Visual Studio Code" ).SequenceEqual(
            [
                new MatchedRange(0, 1),
                new MatchedRange(7, 1),
                new MatchedRange(14, 1)
            ] ) );
        } );

        Run( "fuzzy matching normalizes Unicode diacritics", ( ) =>
        {
            Equal( 1d, FuzzyMatcher.Score( "cafe", "Café" ) );
            Equal(
                new MatchedRange( 0, "Café".Length ),
                FuzzyMatcher.FindRanges( "cafe", "Café" ).Single( ) );
            Equal( 1d, FuzzyMatcher.Score( "cafe", "Cafe\u0301" ) );
            Equal(
                new MatchedRange( 0, "Cafe\u0301".Length ),
                FuzzyMatcher.FindRanges( "cafe", "Cafe\u0301" ).Single( ) );
            True( FuzzyMatcher.Score( "क", "का" ) < 1d );
        } );

        Run( "fuzzy typo tolerance is bounded for long protocol input", ( ) =>
        {
            string query = new( 'a', ProtocolLimits.MaximumQueryCharacters );
            string candidate = new( 'b', ProtocolLimits.MaximumQueryCharacters );
            var stopwatch = System.Diagnostics.Stopwatch.StartNew( );
            for ( var iteration = 0; iteration < 10; iteration++ )
            {
                Equal( 0d, FuzzyMatcher.Score( query, candidate ) );
            }

            stopwatch.Stop( );
            True( stopwatch.Elapsed < TimeSpan.FromMilliseconds( 250 ) );
            Equal( 0d, FuzzyMatcher.Score( new string( 'a', 64 ), new string( 'a', 63 ) + "b" ) );
        } );

        await RunAsync(
            "versioned ranking corpus meets quality thresholds",
            VersionedRankingCorpusMeetsQualityThresholdsAsync );
        await RunAsync(
            "relevance corpus reports required forbidden window and typing failures",
            RelevanceCorpusReportsSearchFailuresAsync );
        await RunAsync(
            "relevance corpus rejects malformed fixtures",
            RelevanceCorpusRejectsMalformedFixturesAsync );
        await RunAsync(
            "catalog supplements skip sources without catalog rows and keep the same order",
            CatalogSupplementsSkipSourcesWithoutRowsAsync );
        await RunAsync(
            "relevance corpus order is identical without queries for empty sources",
            RelevanceCorpusIsIdenticalWithoutEmptySourceQueriesAsync );
        await RunAsync(
            "ranked catalog search returns the rows of the single-statement query",
            RankedCatalogSearchMatchesTheSingleStatementQueryAsync );
        await RunAsync(
            "ranked catalog query seeks catalog items only for the Home-only scope",
            RankedCatalogQuerySeeksItemsOnlyForTheHomeScopeAsync );
        await RunAsync(
            "catalog ranking exposes contributions fields caps and first-page fairness",
            CatalogRankingExposesContributionsFieldsCapsAndFairnessAsync );

        Run( "frecency policy uses an explicit clock", ( ) =>
        {
            DateTimeOffset now = new( 2026, 8, 30, 12, 0, 0, TimeSpan.Zero );
            var current = FrecencyPolicy.Score( 8, now.ToUnixTimeMilliseconds( ), now );
            var old = FrecencyPolicy.Score(
                8,
                now.AddDays( -42 ).ToUnixTimeMilliseconds( ),
                now );
            True( current > old );
            Equal( 0d, FrecencyPolicy.RecencyBoost( 0, now ) );
        } );

        Run( "path expansion", ( ) =>
        {
            var expanded = PathExpansion.Expand( "~/example" );
            True( Path.IsPathFullyQualified( expanded ) );
        } );

        Run(
            "structured config arrays use semantic element names",
            StructuredConfigArraysUseSemanticElementNames );
        await RunAsync(
            "JSONC locations preserve duplicate keys and exact UTF-8 token spans",
            JsoncLocationsAndDuplicateKeysAreExactAsync );
        Run(
            "typed structured config parsers respect their own grammars",
            TypedStructuredConfigParsersRespectTheirGrammars );

        Run( "desktop entry command expands file arguments without a shell", ( ) =>
        {
            var command = DesktopEntryCommand.Create(
                "zed --foreground %U",
                "Zed",
                "zed",
                "/usr/share/applications/dev.zed.Zed.desktop",
                ["~/Desktop/test.txt"] );

            Equal( "zed", command.Executable );
            Equal( 2, command.Arguments.Count );
            Equal( "--foreground", command.Arguments[0] );
            Equal( PathExpansion.Expand( "~/Desktop/test.txt" ), command.Arguments[1] );
        } );

        Run( "desktop entry command expands embedded field codes", ( ) =>
        {
            var command = DesktopEntryCommand.Create(
                "launcher --name=%c --desktop=%k file=%f literal=%%",
                "Example App",
                "example-icon",
                "/usr/share/applications/example.desktop",
                ["~/Desktop/test.txt"] );

            Equal( "launcher", command.Executable );
            Equal( 4, command.Arguments.Count );
            Equal( "--name=Example App", command.Arguments[0] );
            Equal(
                "--desktop=/usr/share/applications/example.desktop",
                command.Arguments[1] );
            Equal( $"file={PathExpansion.Expand( "~/Desktop/test.txt" )}", command.Arguments[2] );
            Equal( "literal=%", command.Arguments[3] );

            Throws<InvalidOperationException>( ( ) => DesktopEntryCommand.Create(
                "launcher files=%F",
                "Example App",
                "example-icon",
                "/usr/share/applications/example.desktop",
                ["one.txt", "two.txt"] ) );
        } );

        Run( "protocol rejects oversized search queries", ( ) =>
        {
            ProtocolRequest request = new( )
            {
                RequestId = 42,
                Operation = ProtocolOperations.Search,
                Query = new string( 'x', ProtocolLimits.MaximumQueryCharacters + 1 )
            };

            Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( request ) );
        } );

        Run( "favorite.set requires an explicit enabled value", ( ) =>
        {
            var request = System.Text.Json.JsonSerializer.Deserialize(
                """{"version":3,"requestId":43,"operation":"favorite.set","resultId":"file:notes"}""",
                CommandDeckJsonContext.Default.ProtocolRequest )!;

            Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( request ) );
        } );

        Run( "protocol wire limits count UTF-8 bytes", ( ) =>
        {
            Equal( 4, ProtocolJson.Utf8ByteCount( "éé" ) );
        } );

        Run( "web fallback depends on executable results", ( ) =>
        {
            SearchResult status = new( )
            {
                Id = "status:offline",
                Kind = ResultKinds.Status,
                Title = "Provider unavailable",
                SourceId = SourceIds.Status
            };
            SearchResult actionable = new( status )
            {
                Id = "quicklink:search",
                Kind = ResultKinds.Quicklink,
                Actions =
                [
                    new CommandAction
                    {
                        Id = "open",
                        Title = "Open",
                        Kind = ActionKinds.OpenUrl
                    }
                ]
            };

            True( !SearchResultCapabilities.IsExecutable( status ) );
            True( SearchResultCapabilities.IsExecutable( actionable ) );
        } );

        Run( "preferences text output escapes bookmark terminal controls", ( ) =>
        {
            var preferences = LauncherPreferences.CreateDefault( );
            PreferencesSnapshot snapshot = new( )
            {
                Preferences = new LauncherPreferences
                {
                    Sources = preferences.Sources,
                    WebFallbackQuicklinkId = "web\n\u001b[31m"
                },
                Customizations =
                [
                    new CustomizationSummary
                    {
                        Id = "customization:file:notes",
                        StableId = "file:notes",
                        Title = "Notes\n\u001b[31mOwned\r",
                        SourceId = SourceIds.Files,
                        Position = 1,
                        IsAvailable = true,
                        IsFavorite = true,
                        Aliases = ["note", "scratch"]
                    }
                ]
            };

            var lines = PreferencesTextFormatter.Format( snapshot );
            var output = string.Join( '\n', lines );
            True( output.Contains( "Notes\\n\\u001B[31mOwned\\r", StringComparison.Ordinal ) );
            True( output.Contains( "Web fallback: web\\n\\u001B[31m", StringComparison.Ordinal ) );
            True( output.Contains( "note, scratch", StringComparison.Ordinal ) );
            True( !output.Contains( '\u001b' ) );
            Equal( lines.Count - 1, output.Count( character => character == '\n' ) );
        } );

        await RunAsync( "configuration rejects terminal-unsafe quicklink IDs", async ( ) =>
        {
            var path = Path.Combine(
                Path.GetTempPath( ),
                $"commanddeck-unsafe-quicklink-{Guid.NewGuid( ):N}.json" );
            try
            {
                CommandDeckOptions options = new( )
                {
                    Quicklinks =
                    [
                        new QuicklinkOptions
                        {
                            Id = "web\n\u001b[31m",
                            Title = "Unsafe",
                            UrlTemplate = "https://example.test/?q={query}"
                        }
                    ]
                };
                await File.WriteAllTextAsync(
                    path,
                    System.Text.Json.JsonSerializer.Serialize(
                        options,
                        CommandDeckJsonContext.Default.CommandDeckOptions ) );
                await ThrowsAsync<InvalidOperationException>( async ( ) =>
                    await ConfigurationLoader.LoadAsync( path, CancellationToken.None ) );
            }
            finally
            {
                File.Delete( path );
            }
        } );

        Run( "ordinary multi-word search is not application invocation", ( ) =>
        {
            True( !ApplicationInvocation.TryParse(
                QueryParser.Parse( "zed settings" ),
                out _ ) );
        } );

        Run( "application provider prefix remains a filter for names", ( ) =>
        {
            True( !ApplicationInvocation.TryParse(
                QueryParser.Parse( "app: visual studio" ),
                out _ ) );
            True( ApplicationInvocation.TryParse(
                QueryParser.Parse( "app: zed ~/Desktop/test.txt" ),
                out _ ) );
            True( ApplicationInvocation.TryParse(
                QueryParser.Parse( "zed ~/Desktop/test.txt", new QueryExpression
                {
                    IncludeFilterIds = ["applications"]
                } ),
                out _ ) );
        } );

        Run( "filter registry drives raw and explicit filters", FilterRegistryDrivesQueries );

        Run(
            "every filter token and alias resolves to its own filter",
            FilterTokensAndAliasesAllResolve );

        await RunAsync( "bounded JSONL reader counts UTF-8 bytes and recovers the next frame", async ( ) =>
        {
            var frames = System.Text.Encoding.UTF8.GetBytes( "ééé\nok\n" );
            BoundedLineReader reader = new( new MemoryStream( frames ), 5 );
            await ThrowsAsync<PayloadTooLargeException>(
                async ( ) => await reader.ReadLineAsync( CancellationToken.None ) );
            Equal( "ok", await reader.ReadLineAsync( CancellationToken.None ) );
        } );

        await RunAsync( "application action treats shell metacharacters literally", async ( ) =>
        {
            var marker = Path.Combine(
                Path.GetTempPath( ),
                $"commanddeck-shell-marker-{Guid.NewGuid( ):N}" );
            SearchResult result = new( )
            {
                Id = "app:literal.desktop",
                Kind = ResultKinds.Application,
                Title = "Literal",
                SourceId = SourceIds.Applications,
                Actions = []
            };
            CommandAction action = new( )
            {
                Id = "launch",
                Title = "Open with Arguments",
                Kind = ActionKinds.LaunchDesktop,
                Arguments = new Dictionary<string, string>( StringComparer.Ordinal )
                {
                    ["desktopFile"] = "/tmp/literal.desktop",
                    ["desktopId"] = "literal.desktop",
                    ["applicationName"] = "Literal",
                    ["exec"] = "/usr/bin/test -n %f",
                    ["launchArguments"] = LaunchArgumentCodec.Encode( [$";touch {marker}"] )
                }
            };

            var outcome = await new LinuxActionExecutor( ).ExecuteAsync(
                result,
                action,
                query: string.Empty,
                CancellationToken.None );
            True( outcome.Succeeded );
            True( !File.Exists( marker ) );
        } );

    }
}
