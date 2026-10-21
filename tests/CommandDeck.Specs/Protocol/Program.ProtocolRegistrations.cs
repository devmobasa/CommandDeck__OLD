namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunProtocolRegistrationsAsync( )
    {
        await RunAsync( "regular file handles preserve offsets after nonblocking admission", RegularFileHandlesPreserveOffsetsAsync );
        await RunAsync(
            "file index source preserves traversal behavior",
            FileIndexSourcePreservesTraversalBehaviorAsync );
        await RunAsync(
            "file indexing enforces canonical root containment",
            FileIndexingEnforcesCanonicalRootContainmentAsync );
        await RunAsync(
            "incremental indexing keeps canonical identity through a symlink root",
            IncrementalIndexUsesCanonicalSymlinkRootAsync );
        await RunAsync(
            "canonical root policy rejects hidden aliases and honors nested roots",
            CanonicalRootPolicyIsConsistentAsync );

        await RunAsync(
            "file preview enforces bounds and path policy",
            FilePreviewEnforcesBoundsAndPathPolicyAsync );
        await RunAsync(
            "file previews navigate matching lines with bounded context",
            FilePreviewMatchesAsync );
        await RunAsync( "image previews render common raster formats through the authorized file service", ImagePreviewsRenderRasterFormatsAsync );
        await RunAsync( "image previews preserve privacy limits cancellation and private snapshot cleanup", ImagePreviewsEnforceBoundariesAsync );
        await RunAsync( "image previews preserve EXIF orientation and PNG WebP transparency", ImagePreviewsPreserveOrientationAndAlphaAsync );
        await RunAsync( "image previews confine decoder filesystem writes and network access", ImagePreviewsConfineDecoderAsync );
        await RunAsync( "socket connection waits for a delayed daemon startup", SocketConnectionWaitsForDelayedDaemonAsync );
        await RunAsync(
            "daemon and bridge restart soak converges without stale state",
            DaemonAndBridgeRestartSoakAsync );

        async Task UnixProtocolFramesCancellationAndFilePreviewsAsync( )
        {
            var root = Path.Combine(
                Path.GetTempPath( ),
                $"commanddeck-protocol-spec-{Guid.NewGuid( ):N}" );
            Directory.CreateDirectory( root );
            var database = Path.Combine( root, "catalog.db" );
            var config = Path.Combine( root, "config.json" );
            var socket = Path.Combine( root, "commanddeck.sock" );
            var files = Path.Combine( root, "files" );
            Directory.CreateDirectory( files );
            Directory.CreateDirectory( Path.Combine( files, "project-a" ) );
            Directory.CreateDirectory( Path.Combine( files, "project-b" ) );
            await File.WriteAllTextAsync(
                Path.Combine( files, "preview-note.md" ),
                "# Preview\n\nProtocol content" );
            await File.WriteAllBytesAsync( Path.Combine( files, "preview-image.png" ), Convert.FromBase64String( PreviewPngBase64 ) );
            await File.WriteAllTextAsync(
                Path.Combine( files, "project-a", "context-report.md" ),
                "context report alpha" );
            await File.WriteAllTextAsync(
                Path.Combine( files, "project-b", "context-report.md" ),
                "context report beta" );
            var editorCapture = Path.Combine( root, "socket-editor-argv.txt" );
            var editorExecutable = Path.Combine( root, "socket-editor" );
            var editorConfig = Path.Combine( files, "editor.conf" );
            await File.WriteAllTextAsync( editorConfig, "socket-editor-key = literal\n" );
            await File.WriteAllTextAsync( editorExecutable, $"""
                #!/bin/sh
                : > '{editorCapture}'
                for argument in "$@"; do
                  printf '%s\n' "$argument" >> '{editorCapture}'
                done
                """.ReplaceLineEndings( "\n" ) );
            if ( OperatingSystem.IsLinux( ) )
            {
                File.SetUnixFileMode(
                    editorExecutable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            }
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = database },
                Indexing = new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions
                        {
                            Path = files,
                            IncludeContentInSearch = true
                        }
                    ],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false
                },
                Privacy = new PrivacyOptions
                {
                    StoreUsageHistory = false,
                    StoreQueryHistory = true,
                    NeverIndex = []
                },
                Search = new SearchOptions
                {
                    Ranking = new RankingOptions
                    {
                        Context = new ContextRankingOptions { Enabled = false }
                    }
                },
                Quicklinks =
                [
                    new QuicklinkOptions
                    {
                        Id = "web",
                        Title = "Web Search",
                        Alias = "webdocs",
                        UrlTemplate = "https://example.test/search?q={query}"
                    }
                ],
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            await File.WriteAllTextAsync(
                config,
                System.Text.Json.JsonSerializer.Serialize(
                    options,
                    CommandDeckJsonContext.Default.CommandDeckOptions ) );

            using CancellationTokenSource serverCancellation = new( );
            try
            {
                await using var runtime = await CommandDeckRuntime.CreateAsync(
                    config,
                    CancellationToken.None );
                await runtime.Indexer.ReindexAsync( progress: null, CancellationToken.None );
                var editorPreferences = LauncherPreferences.CreateDefault( );
                await runtime.Preferences.ReplacePreferencesAsync( new LauncherPreferences
                {
                    Sources = editorPreferences.Sources,
                    PreferredEditor = EditorKinds.Graphical,
                    GraphicalEditor = new EditorAdapter
                    {
                        Executable = editorExecutable,
                        Arguments = ["{path}", "{line}", "{column}", "{key}"]
                    },
                    TerminalEditor = editorPreferences.TerminalEditor
                }, CancellationToken.None );
                UnixProtocolServer server = new( runtime, socket );
                var serverTask = server.RunAsync( serverCancellation.Token );
                await using var client = await ConnectWithRetryAsync( socket );

                await VerifyMultibyteFrameOverflowRecoveryAsync( client );
                await VerifyRuntimeStatusProtocolAsync( client, CancellationToken.None );
                await VerifyIndexControlProtocolAsync(
                    client,
                    StableId.Create( "index-root", Path.GetFullPath( files ) ),
                    Path.Combine( files, "preview-note.md" ),
                    CancellationToken.None );

                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 1,
                    Operation = ProtocolOperations.Search,
                    Query = "= 2 + 2"
                }, CancellationToken.None );
                await client.SendAsync( new ProtocolRequest
                {
                    RequestId = 2,
                    Operation = ProtocolOperations.Cancel,
                    TargetRequestId = 1
                }, CancellationToken.None );

                HashSet<long> completed = [];
                using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 10 ) );
                await foreach ( var line in client.ReadRawMessagesAsync( timeout.Token ) )
                {
                    var message = System.Text.Json.JsonSerializer.Deserialize(
                        line,
                        CommandDeckJsonContext.Default.ProtocolMessage );
                    if ( message?.Type == ProtocolMessageTypes.Done )
                    {
                        completed.Add( message.RequestId );
                    }

                    if ( completed.Contains( 1 ) && completed.Contains( 2 ) )
                    {
                        break;
                    }
                }

                True( completed.Contains( 1 ) );
                True( completed.Contains( 2 ) );

                await VerifyExactEditorProtocolAsync(
                    client,
                    editorConfig,
                    editorCapture,
                    timeout.Token );
                await runtime.QueryHistory.ResetAsync( new QueryHistoryResetInput( true ), CancellationToken.None );

                await VerifyPreviewProtocolAsync( client, socket, editorCapture, timeout.Token );
                var previewHistory = await runtime.QueryHistory.GetAsync( 20, CancellationToken.None );
                Equal( "preview-note", previewHistory.Entries.Single( ).Query );
                await runtime.QueryHistory.ResetAsync( new QueryHistoryResetInput( true ), CancellationToken.None );
                await VerifyPersonalizationProtocolAsync(
                    client,
                    runtime.Preferences,
                    database,
                    timeout.Token );
                await VerifyQueryHistoryProtocolAsync( client, runtime, timeout.Token );
                await VerifyQuicklinksProtocolAsync( client, timeout.Token );
                await VerifyFilterAliasProtocolAsync( client, timeout.Token );
                await VerifySavedSearchProtocolAsync( client, timeout.Token );
                await VerifySuggestionsProtocolAsync( client, timeout.Token );
                await VerifyDisabledContextProtocolAsync(
                    client,
                    Path.Combine( files, "project-b" ),
                    timeout.Token );
                await runtime.Catalog.ReplaceExclusionDiagnosticsAsync(
                [
                    new IndexExclusionDiagnostic
                    {
                        Root = files,
                        RelativePath = "excluded.tmp",
                        Reason = IndexExclusionReasons.RootGlob,
                        Rule = "*.tmp"
                    }
                ], timeout.Token );
                await VerifyCatalogOperationsProtocolAsync(
                    client,
                    runtime.Preferences,
                    timeout.Token );
                var runtimeCatalog = runtime.Catalog as SqliteCatalog;
                ArgumentNullException.ThrowIfNull( runtimeCatalog );

                await VerifyInspectionSocketBeyondSampleAsync(
                    client,
                    runtimeCatalog,
                    timeout.Token );

                var bridgeExecutable = Path.GetFullPath(
                    "src/CommandDeck.App/bin/Release/net11.0/linux-x64/commanddeck" );
                System.Diagnostics.ProcessStartInfo bridgeStart = new( )
                {
                    FileName = bridgeExecutable,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                bridgeStart.ArgumentList.Add( "bridge" );
                bridgeStart.ArgumentList.Add( "--socket" );
                bridgeStart.ArgumentList.Add( socket );
                using System.Diagnostics.Process bridge = new( )
                {
                    StartInfo = bridgeStart
                };
                True( bridge.Start( ) );
                try
                {
                    await bridge.StandardInput.WriteLineAsync(
                        "{\"version\":3,\"requestId\":3,\"operation\":\"ping\"}" );
                    await bridge.StandardInput.FlushAsync( );
                    using CancellationTokenSource bridgeTimeout =
                        new( TimeSpan.FromSeconds( 3 ) );
                    var pong = await bridge.StandardOutput.ReadLineAsync( bridgeTimeout.Token );
                    var done = await bridge.StandardOutput.ReadLineAsync( bridgeTimeout.Token );
                    True( pong?.Contains( "\"type\":\"pong\"", StringComparison.Ordinal ) == true );
                    True( done?.Contains( "\"type\":\"done\"", StringComparison.Ordinal ) == true );
                    bridge.StandardInput.Close( );
                    await bridge.WaitForExitAsync( bridgeTimeout.Token );
                    Equal( 0, bridge.ExitCode );
                }
                finally
                {
                    if ( !bridge.HasExited )
                    {
                        bridge.Kill( entireProcessTree: true );
                        await bridge.WaitForExitAsync( );
                    }
                }

                serverCancellation.Cancel( );
                await serverTask;
            }
            finally
            {
                serverCancellation.Cancel( );
                Directory.Delete( root, recursive: true );
            }
        }

        await RunAsync(
            "Unix socket protocol frames cancellation and file previews",
            UnixProtocolFramesCancellationAndFilePreviewsAsync );

    }
}
