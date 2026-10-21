namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FileAndConfigurationWorkflowsUseLiteralTypedActionsAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-file-workflow-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        var database = Path.Combine( root, "catalog.db" );
        var filePath = Path.Combine( root, "résumé notes.txt" );
        var unknownExtensionPath = Path.Combine( root, "mystery.commanddeck-unknown" );
        var configPath = Path.Combine( root, "settings.conf" );
        var capturePath = Path.Combine( root, "editor-argv.txt" );
        var editorPath = Path.Combine( root, "fake-editor" );
        var clipboardPath = Path.Combine( root, "clipboard.bin" );
        var clipboardExecutable = Path.Combine( root, "wl-copy" );
        var mimeExecutable = Path.Combine( root, "file" );
        var mimeCountPath = Path.Combine( root, "mime-count.txt" );
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        await File.WriteAllTextAsync( filePath, "notes" );
        await File.WriteAllTextAsync( unknownExtensionPath, "plain text with an unknown extension\n" );
        await File.WriteAllTextAsync( configPath, "theme = nord\n" );
        await File.WriteAllTextAsync( editorPath, $"""
            #!/bin/sh
            : > '{capturePath}'
            for argument in "$@"; do
              printf '%s\n' "$argument" >> '{capturePath}'
            done
            """.ReplaceLineEndings( "\n" ) );
        await File.WriteAllTextAsync( clipboardExecutable, $"""
            #!/bin/sh
            cat > '{clipboardPath}'
            """.ReplaceLineEndings( "\n" ) );
        await File.WriteAllTextAsync( mimeExecutable, $"""
            #!/bin/sh
            printf 'called\n' >> '{mimeCountPath}'
            while [ "$#" -gt 0 ]; do
              [ "$1" = "--" ] && shift && break
              shift
            done
            for path in "$@"; do
              printf '%s\n' 'text/plain'
            done
            """.ReplaceLineEndings( "\n" ) );
        if ( !OperatingSystem.IsLinux( ) )
        {
            Directory.Delete( root, recursive: true );
            throw new SpecSkippedException( "This fixture requires Linux." );
        }
        File.SetUnixFileMode(
            editorPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        File.SetUnixFileMode(
            clipboardExecutable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        File.SetUnixFileMode(
            mimeExecutable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        Environment.SetEnvironmentVariable( "PATH", root + Path.PathSeparator + originalPath );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync(
                FileWorkflowItems( filePath, unknownExtensionPath, configPath ), null,
                CancellationToken.None );

            // Open With is added to the rows a search shows, so every path below
            // goes through the runtime rather than the catalog provider.
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = database },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SearchCoordinator search = new(
                [new CatalogSearchProvider( catalog, catalog )],
                options.Search,
                catalog,
                catalog,
                catalog.UsageHistory );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, new AcceptanceExecutor( ), search );
            var fileResults = await runtime.SearchAsync(
                new SearchRequest { Query = "file:", Limit = 20 },
                CancellationToken.None );
            var file = fileResults.Single( value => value.Id == "file:notes" );
            Equal( new Uri( filePath ).AbsoluteUri,
                file.Actions.Single( value => value.Id == "copy-file-url" ).Arguments["text"] );
            var openWith = file.Actions.Single( value =>
                value.Id == "open-with:viewer.desktop" );
            Equal( ActionKinds.LaunchDesktop, openWith.Kind );
            Equal( filePath,
                LaunchArgumentCodec.Decode( openWith.Arguments["launchArguments"] ).Single( ) );
            var unknownExtension = fileResults.Single(
                value => value.Id == "file:unknown-extension" );
            True( unknownExtension.Actions.Any( value =>
                value.Id == "open-with:viewer.desktop" ) );

            await catalog.SetFavoriteAsync( file, true, CancellationToken.None );
            await catalog.SetAliasAsync( file, "notes", CancellationToken.None );
            var aliasFile = (await runtime.SearchAsync(
                new SearchRequest { Query = "notes", Limit = 20 }, CancellationToken.None ))
                .Single( value => value.Id == file.Id );
            Equal( "notes", aliasFile.Metadata["exactAlias"] );
            True( aliasFile.Actions.Any( value => value.Id == "open-with:viewer.desktop" ) );
            var homeFile = (await runtime.SearchAsync(
                new SearchRequest { Query = string.Empty, Limit = 20 }, CancellationToken.None ))
                .Single( value => value.Id == file.Id );
            True( homeFile.Actions.Any( value => value.Id == "open-with:viewer.desktop" ) );
            Equal( 1, (await File.ReadAllLinesAsync( mimeCountPath )).Length );

            await File.WriteAllTextAsync( mimeExecutable, """
                #!/bin/sh
                exec /usr/bin/yes text/plain
                """.ReplaceLineEndings( "\n" ) );
            File.SetUnixFileMode(
                mimeExecutable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            var floodPath = Path.Combine( root, "mime-flood.bin" );
            await File.WriteAllTextAsync( floodPath, "flood" );
            SharedMimeTypeResolver floodResolver = new( );
            var mimeFloodElapsed =
                System.Diagnostics.Stopwatch.StartNew( );
            var floodResult =
                await floodResolver.ResolveManyAsync( [floodPath], default );
            mimeFloodElapsed.Stop( );
            Equal<string?>( null, floodResult[floodPath] );
            True( mimeFloodElapsed.Elapsed < TimeSpan.FromSeconds( 2 ) );

            var config = (await catalog.SearchAsync(
                QueryParser.Parse( "cfg: theme" ), 20, CancellationToken.None )).Single( );
            Equal( "theme", config.Actions.Single( value => value.Id == "copy-key" )
                .Arguments["text"] );
            Equal( "nörd", config.Actions.Single( value => value.Id == "copy-value" )
                .Arguments["text"] );
            Equal( "theme = \"nörd\"", config.Actions.Single( value => value.Id == "copy-assignment" )
                .Arguments["text"] );

            var defaults = LauncherPreferences.CreateDefault( );
            await catalog.ReplacePreferencesAsync( new LauncherPreferences
            {
                Sources = defaults.Sources,
                PreferredEditor = EditorKinds.Graphical,
                GraphicalEditor = new EditorAdapter
                {
                    Executable = editorPath,
                    Arguments = ["{path}", "{line}", "{column}", "{key}"]
                },
                TerminalEditor = defaults.TerminalEditor
            }, CancellationToken.None );
            LinuxActionExecutor executor = new( catalog );
            var edited = await executor.ExecuteAsync(
                config,
                config.Actions.Single( value => value.Id == "edit" ),
                string.Empty,
                CancellationToken.None );
            True( edited.Succeeded );
            var editorArguments = await File.ReadAllLinesAsync( capturePath );
            True( new[] { configPath, "7", "3", "theme" }.SequenceEqual(
                editorArguments,
                StringComparer.Ordinal ) );

            foreach ( var actionId in new[] { "copy-key", "copy-value", "copy-assignment" } )
            {
                var copy = config.Actions.Single( value => value.Id == actionId );
                var copied = await executor.ExecuteAsync(
                    config, copy, string.Empty, CancellationToken.None );
                True( copied.Succeeded );
                var expected = System.Text.Encoding.UTF8.GetBytes( copy.Arguments["text"] );
                var actual = await File.ReadAllBytesAsync( clipboardPath );
                True( expected.SequenceEqual( actual ) );
            }

            Throws<InvalidOperationException>( ( ) => EditorAdapterValidator.Validate(
                new EditorAdapter
                {
                    Executable = "editor",
                    Arguments = ["{path}", "{shell}"]
                }, terminal: false ) );
            var assignment = StructuredConfigExtractor.Extract(
                "/tmp/settings.conf", "theme = nord" ).Single( );
            Equal( "theme = nord", assignment.Assignment );
            var json = StructuredConfigExtractor.Extract(
                "/tmp/settings.json", "{\"theme\":\"nord\"}" ).Single( );
            Equal<string?>( null, json.Assignment );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> FileWorkflowItems(
        string filePath,
        string unknownExtensionPath,
        string configPath )
    {
        yield return new IndexedItem
        {
            StableId = "app:viewer.desktop",
            Kind = ResultKinds.Application,
            Title = "Text Viewer",
            PayloadJson = PayloadWriter.Create(
                ("desktopFile", "/tmp/viewer.desktop"),
                ("desktopId", "viewer.desktop"),
                ("exec", "viewer %U"),
                ("mimeTypes", "text/plain"),
                ("terminal", "false") ),
            ProviderId = "applications",
            SourceId = SourceIds.Applications
        };
        yield return new IndexedItem
        {
            StableId = "file:notes",
            Kind = ResultKinds.File,
            Title = "résumé notes.txt",
            Path = filePath,
            ProviderId = "files",
            SourceId = SourceIds.Files
        };
        yield return new IndexedItem
        {
            StableId = "file:unknown-extension",
            Kind = ResultKinds.File,
            Title = "mystery.commanddeck-unknown",
            Path = unknownExtensionPath,
            ProviderId = "files",
            SourceId = SourceIds.Files
        };
        yield return new IndexedItem
        {
            StableId = "config:theme",
            Kind = ResultKinds.Configuration,
            Title = "theme",
            Path = configPath,
            PayloadJson = PayloadWriter.Create(
                ("keyLine", "7"),
                ("keyColumn", "3"),
                ("key", "theme"),
                ("value", "nörd"),
                ("assignment", "theme = \"nörd\"") ),
            ProviderId = "config",
            SourceId = SourceIds.Configuration
        };
        await Task.CompletedTask;
    }

    private static async Task VerifyExactEditorProtocolAsync(
        UnixProtocolClient client,
        string configPath,
        string capturePath,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 210,
            Operation = ProtocolOperations.Search,
            Query = "cfg: socket-editor-key",
            Limit = 20
        }, CancellationToken.None );
        var resultMessage = await ReadMessageAsync(
            client, 210, ProtocolMessageTypes.Result, cancellationToken );
        var result = resultMessage?.Result;
        if ( result is null )
        {
            throw new InvalidOperationException( "Configuration result missing." );
        }

        Equal( ResultKinds.Configuration, result.Kind );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 211,
            Operation = ProtocolOperations.Invoke,
            ResultId = result.Id,
            InvocationReference = result.InvocationReference,
            ActionId = "edit"
        }, CancellationToken.None );
        var invoked = await ReadMessageAsync(
            client, 211, ProtocolMessageTypes.ActionResult, cancellationToken );
        True( invoked?.ActionResult?.Succeeded == true );
        var arguments = await File.ReadAllLinesAsync( capturePath, cancellationToken );
        if ( arguments.Length != 4 || arguments[0] != configPath ||
            arguments[1] != result.Metadata["keyLine"] ||
            arguments[2] != result.Metadata["keyColumn"] ||
            arguments[3] != "socket-editor-key" )
        {
            throw new InvalidOperationException(
                $"Editor argv mismatch: metadata={result.Metadata["keyLine"]}:" +
                $"{result.Metadata["keyColumn"]}, argv={string.Join( '|', arguments )}." );
        }
    }
}
