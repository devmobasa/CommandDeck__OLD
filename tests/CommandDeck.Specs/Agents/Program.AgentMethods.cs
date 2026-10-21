using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void AgentContractsEnforceTrustAndPreferenceBounds( )
    {
        True( AgentTrustLevels.IsValid( 0 ) && AgentTrustLevels.IsValid( 6 ) && !AgentTrustLevels.IsValid( 7 ) );
        Equal( "Unrestricted", AgentTrustLevels.Name( AgentTrustLevels.Unrestricted ) );
        Equal( "Approved context", AgentTrustLevels.Name( AgentTrustLevels.Default ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.ValidatePreferences( new AgentPreferences { TrustLevel = 9 } ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.ValidatePreferences( new AgentPreferences { ConversationTtlMinutes = 999_999 } ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.ValidatePreferences( new AgentPreferences { TurnTimeoutMinutes = 1 } ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.ValidatePreferences( new AgentPreferences { ContextPolicy = "everything" } ) );
        AgentContractValidator.ValidatePreferences( AgentPreferences.CreateDefault( ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.ValidatePromptText( "   " ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.ValidatePromptText( new string( 'x', AgentLimits.MaximumPromptBytes + 1 ) ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.ValidateArgv( [] ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.ValidateArgv( ["ls", "a" + ( char ) 0 + "b"] ) );
        var names = AgentContractValidator.NormalizePackageNames( ["ripgrep", "fd", "ripgrep"] );
        Equal( "fd|ripgrep", string.Join( '|', names ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.NormalizePackageNames( ["--flag"] ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.NormalizePackageNames( ["a/b"] ) );
        Throws<InvalidOperationException>( ( ) => AgentContractValidator.NormalizePackageNames(
            Enumerable.Range( 0, 17 ).Select( index => "pkg" + index ).ToArray( ) ) );
        var unrestricted = new AgentPreferences { Enabled = true, WebSearchEnabled = true, AurEnabled = true }
            .WithTrust( AgentTrustLevels.Unrestricted, AgentTrustDurations.UntilDowngrade, null );
        Equal( AgentTrustLevels.Unrestricted, unrestricted.TrustLevel );
        var cleared = unrestricted.WithAuthorityCleared( );
        Equal( AgentTrustLevels.Default, cleared.TrustLevel );
        True( !cleared.WebSearchEnabled && !cleared.AurEnabled && cleared.TrustDuration is null && cleared.Enabled );
    }

    private static void AgentRuntimeHomeAndSandboxArgumentsStayClosed( )
    {
        var configuration = CodexRuntimeHome.BuildConfiguration( AgentTrustLevels.ApprovedContext, webSearch: false );
        True( configuration.Contains( "persistence = \"none\"", StringComparison.Ordinal ) );
        True( configuration.Contains( "web_search = \"disabled\"", StringComparison.Ordinal ) );
        True( configuration.Contains( "[analytics]\nenabled = false", StringComparison.Ordinal ) );
        True( configuration.Contains( "shell_tool = false", StringComparison.Ordinal ) );
        True( configuration.Contains( "browser_use = false", StringComparison.Ordinal ) );
        var unrestricted = CodexRuntimeHome.BuildConfiguration( AgentTrustLevels.Unrestricted, webSearch: true );
        True( !unrestricted.Contains( "[features]", StringComparison.Ordinal ) );
        True( unrestricted.Contains( "web_search = \"live\"", StringComparison.Ordinal ) );
        var commandTrust = CodexRuntimeHome.BuildConfiguration( AgentTrustLevels.Packages, webSearch: true );
        True( commandTrust.Contains( "web_search = \"disabled\"", StringComparison.Ordinal ) );

        CodexLaunchRequest request = new( )
        {
            BinaryPath = "/opt/codex/bin/codex",
            RuntimeHome = "/run/user/1000/commanddeck/codex/gen",
            Generation = "gen",
            TrustLevel = AgentTrustLevels.ApprovedContext,
            WorkingDirectory = "/run/user/1000/commanddeck/codex/gen"
        };
        var arguments = BubblewrapCodexLauncher.BuildArguments( request );
        var joined = string.Join( ' ', arguments );
        True( joined.Contains( "--unshare-all --share-net --die-with-parent --new-session", StringComparison.Ordinal ) );
        True( joined.Contains( "--tmpfs /home", StringComparison.Ordinal ) );
        True( joined.Contains( "--tmpfs /run", StringComparison.Ordinal ) );
        True( joined.Contains( "--bind /run/user/1000/commanddeck/codex/gen /run/user/1000/commanddeck/codex/gen", StringComparison.Ordinal ) );
        True( joined.Contains( "--ro-bind /opt/codex/bin/codex /opt/commanddeck/codex", StringComparison.Ordinal ) );
        True( joined.Contains( "--clearenv", StringComparison.Ordinal ) );
        // Code Mode is left enabled (the gpt-5.6-* models require it); its tool
        // calls stay mediated through item/tool/call. The launch is unchanged.
        True( joined.EndsWith( "-- /opt/commanddeck/codex app-server --stdio --strict-config", StringComparison.Ordinal ) );
        foreach ( var prohibited in CodexArgv.ProhibitedFlags )
        {
            True( !joined.Contains( prohibited, StringComparison.OrdinalIgnoreCase ) );
        }
        Throws<InvalidOperationException>( ( ) => CodexArgv.AssertSafe( ["app-server", "--yolo"] ) );
        Throws<InvalidOperationException>( ( ) => CodexArgv.AssertSafe( ["--dangerously-bypass-approvals-and-sandbox"] ) );
        CodexAppServerContract.Validate( );
        Equal( "0.152.1", CodexAppServerContract.ReviewedVersion );
        Equal( 64, CodexAppServerContract.ReviewedSchemaDigest.Length );
        var transient = TransientUnitCodexLauncher.BuildArguments( new CodexLaunchRequest
        {
            BinaryPath = "/opt/codex/bin/codex",
            RuntimeHome = "/run/user/1000/commanddeck/codex/l6",
            Generation = "l6",
            TrustLevel = AgentTrustLevels.Unrestricted,
            WorkingDirectory = "/home/user",
            SocketPath = "/run/user/1000/commanddeck/codex/l6/app-server.sock"
        } );
        var transientJoined = string.Join( ' ', transient );
        True( transientJoined.StartsWith( "--user --unit=commanddeck-codex-l6 --collect --quiet", StringComparison.Ordinal ) );
        True( transientJoined.Contains( "--listen unix:///run/user/1000/commanddeck/codex/l6/app-server.sock", StringComparison.Ordinal ) );
        True( !transientJoined.Contains( "yolo", StringComparison.OrdinalIgnoreCase ) );
    }

    private static async Task AgentUnifiedRoutingUsesOnlyTheSelectedBackendAsync( )
    {
        AiOptions options = new( )
        {
            Enabled = true,
            Providers =
            [
                new AiProviderOptions { Id = "local-one", Kind = "ollama", Model = "one", BaseUrl = "http://127.0.0.1:11434", Enabled = true },
                new AiProviderOptions { Id = "cloud-two", Kind = "openai", Model = "two", BaseUrl = "https://example.invalid/v1", Enabled = true }
            ]
        };
        AiCommandProvider answerOnly = new( options, ( ) => "cloud-two" );
        AgentCommandProvider codexNotSelected = new( ( ) => new AgentRowState(
            Enabled: true, SelectedBackendId: null, Authenticated: true, Conversation: null, ModelId: null ) );
        SearchRequest request = new( )
        {
            Query = "ai: hello",
            Limit = 8
        };
        var ai = QueryParser.Parse( request.Query );
        Equal( "assistant", ai.ProviderFilter );
        var answerRows = await answerOnly.SearchAsync( ai, request, default );
        Equal( 1, answerRows.Count );
        Equal( "ai:cloud-two", answerRows[0].Id );
        Equal( 0, (await codexNotSelected.SearchAsync( ai, request, default )).Count );

        var explicitCodex = QueryParser.Parse( "codex: hello" );
        Equal( 0, (await answerOnly.SearchAsync( explicitCodex, request, default )).Count );
        Equal( "agent:codex", (await codexNotSelected.SearchAsync( explicitCodex, request, default )).Single( ).Id );
        var spaceCodex = QueryParser.Parse( "codex hello" );
        Equal( "assistant", spaceCodex.ProviderFilter );
        Equal( "hello", spaceCodex.Text );
        Equal( "agent:codex", (await codexNotSelected.SearchAsync( spaceCodex, request, default )).Single( ).Id );

        AgentCommandProvider codexSelected = new( ( ) => new AgentRowState(
            Enabled: true, SelectedBackendId: AgentBackendIds.Codex, Authenticated: true, Conversation: null, ModelId: "selected" ) );
        var selectedRow = (await codexSelected.SearchAsync( ai, request, default )).Single( );
        Equal( "agent:codex", selectedRow.Id );
        Equal( "Ask AI: hello", selectedRow.Title );
        True( selectedRow.Subtitle?.Contains( "Codex", StringComparison.Ordinal ) == true, "the subtitle still identifies the selected backend" );
        Equal( AgentBackendIds.Codex, selectedRow.Actions.Single( ).Arguments["backendId"] );
        Equal( "Ask AI", (await codexSelected.SearchAsync( QueryParser.Parse( "ai:" ), request, default )).Single( ).Title );
        Equal( 0, (await new AiCommandProvider( options, ( ) => null ).SearchAsync( ai, request, default )).Count );
        Equal( 0, (await codexSelected.SearchAsync( QueryParser.Parse( "ordinary search" ), request, default )).Count );
    }

    private static void UnknownPrefixesAndPromptsAreNotSearchErrors( )
    {
        var typing = QueryParser.Parse( "a:" );
        True( typing.Expression.IncludeFilterIds.Count == 0 && typing.Expression.Clauses.Count == 1 && typing.Expression.Clauses[0].Text == "a",
            "an unknown prefix typed with a trailing colon is plain text" );
        var note = QueryParser.Parse( "note: buy milk" );
        True( note.Expression.Clauses.Select( static clause => clause.Text ).SequenceEqual( ["note", "buy", "milk"] ),
            "unknown prefixes never raise an error" );
        var field = QueryParser.Parse( "title:" );
        True( field.Expression.Clauses.Count == 0, "an incomplete field clause is ignored while typing" );
        var app = QueryParser.Parse( "app:" );
        True( app.HasExplicitFilter, "known filters still resolve" );
        ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            RequestId = 1,
            Operation = ProtocolOperations.AgentTurnStart,
            Query = "Reply with exactly the single word: pong"
        } );
        ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            RequestId = 2,
            Operation = ProtocolOperations.PackageSearch,
            Query = "editor: vim",
            Limit = 8
        } );
    }

    private static async Task<string> WriteFakeCodexAsync( string root, string version )
    {
        var path = Path.Combine( root, $"codex-{version}" );
        await File.WriteAllTextAsync( path, $"#!/bin/sh\necho 'codex-cli {version}'\n" );
        if ( OperatingSystem.IsLinux( ) )
        {
            File.SetUnixFileMode( path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        }
        return path;
    }

    private static async Task CodexVersionRangeAdmitsTheZeroLineButRejectsAMajorBumpAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-codex-version-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            // The default range spans the 0.x line above the reviewed version:
            // a routine Codex update runs, but is marked unreviewed.
            var admitted = await CodexBinaryLocator.LocateAsync(
                new AgentOptions { CodexBinaryPath = await WriteFakeCodexAsync( root, "0.153.0" ) }, default );
            True( admitted.Version == "0.153.0" && !admitted.Reviewed,
                "the default range admits a newer 0.x version but marks it unreviewed" );

            // The reviewed version is admitted and marked reviewed.
            var reviewed = await CodexBinaryLocator.LocateAsync(
                new AgentOptions { CodexBinaryPath = await WriteFakeCodexAsync( root, "0.152.1" ) }, default );
            True( reviewed.Reviewed, "the reviewed version is marked reviewed" );

            // A 1.0 major bump falls outside the default range and is rejected
            // so its App Server schema can be reviewed first.
            await ThrowsAsync<AiOperationException>( async ( ) => await CodexBinaryLocator.LocateAsync(
                new AgentOptions { CodexBinaryPath = await WriteFakeCodexAsync( root, "1.0.0" ) }, default ) );

            // A version below the floor is rejected: it predates the protocol.
            await ThrowsAsync<AiOperationException>( async ( ) => await CodexBinaryLocator.LocateAsync(
                new AgentOptions { CodexBinaryPath = await WriteFakeCodexAsync( root, "0.151.9" ) }, default ) );

            // An operator can still tighten the ceiling by configuration.
            await ThrowsAsync<AiOperationException>( async ( ) => await CodexBinaryLocator.LocateAsync(
                new AgentOptions
                {
                    CodexBinaryPath = await WriteFakeCodexAsync( root, "0.153.0" ),
                    MaximumCodexMinorVersion = 152,
                    MaximumCodexPatchVersion = 1
                }, default ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static void AgentCredentialStoreOwnsAPrivateFile( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-agent-cred-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            CodexCredentialStore store = new( Path.Combine( root, "state" ) );
            True( !store.Exists && store.Read( ) is null );
            Throws<InvalidOperationException>( ( ) => store.Write( "not json"u8 ) );
            Throws<InvalidOperationException>( ( ) => store.Write( "{\"unrelated\":1}"u8 ) );
            store.Write( """{"tokens":{"access_token":"a","refresh_token":"b","account_id":"c"}}"""u8 );
            True( store.Exists );
            if ( OperatingSystem.IsLinux( ) )
            {
                var mode = File.GetUnixFileMode( store.FilePath );
                Equal( UnixFileMode.None, mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite) );
            }
            var read = store.Read( );
            True( read is not null && Encoding.UTF8.GetString( read ).Contains( "refresh_token", StringComparison.Ordinal ) );
            using ( var home = CodexRuntimeHome.Create( Path.Combine( root, "runtime" ), AgentTrustLevels.ApprovedContext, webSearch: false ) )
            {
                home.ImportCredential( read );
                CodexCredentialStore.Clear( read );
                True( read!.All( static value => value == 0 ) );
                True( File.Exists( home.AuthPath ) );
                True( File.Exists( Path.Combine( home.Path, "config.toml" ) ) );
                var exported = home.ExportCredential( );
                True( exported is not null && exported.Length == read!.Length );
                CodexCredentialStore.Clear( exported );
                True( exported!.All( static value => value == 0 ) );
                var path = home.Path;
                home.Wipe( );
                True( !Directory.Exists( path ) );
            }
            store.Delete( );
            True( !store.Exists );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentContextBrokerEnforcesLevelsAndProtectedPathsAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-agent-ctx-{Guid.NewGuid( ):N}" );
        var home = Path.Combine( root, "home" );
        Directory.CreateDirectory( Path.Combine( home, "notes" ) );
        Directory.CreateDirectory( Path.Combine( home, ".ssh" ) );
        Directory.CreateDirectory( Path.Combine( root, "outside" ) );
        await File.WriteAllTextAsync( Path.Combine( home, "notes", "todo.md" ), "buy milk\nfix bug\n" );
        await File.WriteAllTextAsync( Path.Combine( home, ".ssh", "id_ed25519" ), "secret" );
        await File.WriteAllTextAsync( Path.Combine( home, "notes", "secrets.json" ), "{}" );
        await File.WriteAllBytesAsync( Path.Combine( home, "notes", "binary.bin" ), [1, 0, 2, 0] );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            AgentContextBroker broker = new( new AgentAuthority( catalog.AgentPreferences ), home );
            AgentPreferences level1 = new( )
            {
                TrustLevel = AgentTrustLevels.Launcher
            };
            AgentPreferences level2 = new( )
            {
                TrustLevel = AgentTrustLevels.ApprovedContext
            };
            AgentPreferences level3 = new( )
            {
                TrustLevel = AgentTrustLevels.ReadEntireHome
            };

            var denied = await broker.AuthorizeAsync( "notes/todo.md", AgentContextOperations.ReadFile, level1, default );
            True( !denied.Allowed && !denied.RequiresDecision );
            var outside = await broker.AuthorizeAsync( Path.Combine( root, "outside" ), AgentContextOperations.ListDirectory, level3, default );
            True( !outside.Allowed && !outside.RequiresDecision );
            var protectedFile = await broker.AuthorizeAsync( "~/.ssh/id_ed25519", AgentContextOperations.ReadFile, level3, default );
            True( !protectedFile.Allowed && protectedFile.RequiresDecision && protectedFile.Protected && protectedFile.ProposedFolder is null );
            AgentPreferences unrestricted = new( )
            {
                TrustLevel = AgentTrustLevels.Unrestricted
            };
            var protectedUnrestricted = await broker.AuthorizeAsync( "~/.ssh/id_ed25519", AgentContextOperations.ReadFile, unrestricted, default );
            True( protectedUnrestricted.Allowed && !protectedUnrestricted.RequiresDecision && protectedUnrestricted.Protected,
                "unrestricted trust reads protected paths without a prompt" );
            True( broker.IsProtected( Path.Combine( home, "notes", "secrets.json" ) ) );
            var pending = await broker.AuthorizeAsync( "~/notes/todo.md", AgentContextOperations.ReadFile, level2, default );
            True( !pending.Allowed && pending.RequiresDecision && pending.ProposedFolder == Path.Combine( home, "notes" ) );
            var entireHome = await broker.AuthorizeAsync( "notes/todo.md", AgentContextOperations.ReadFile, level3, default );
            True( entireHome.Allowed );

            var grant = await broker.GrantFolderAsync( Path.Combine( home, "notes" ), default );
            Equal( AgentGrantStates.Active, grant.State );
            var granted = await broker.AuthorizeAsync( "~/notes/todo.md", AgentContextOperations.ReadFile, level2, default );
            True( granted.Allowed );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await broker.GrantFolderAsync( Path.Combine( home, ".ssh" ), default ) );

            var entries = broker.ListDirectory( Path.Combine( home, "notes" ), 16 );
            True( entries.Any( entry => entry.Name == "todo.md" ) );
            True( !entries.Any( entry => entry.Name == "secrets.json" ) );
            var homeEntries = broker.ListDirectory( home, 16 );
            True( !homeEntries.Any( entry => entry.Name == ".ssh" ) );
            var text = broker.ReadTextFile( Path.Combine( home, "notes", "todo.md" ), 64, out var truncated );
            True( text.StartsWith( "buy milk", StringComparison.Ordinal ) && !truncated );
            Throws<InvalidOperationException>( ( ) => broker.ReadTextFile( Path.Combine( home, "notes", "binary.bin" ), 64, out _ ) );
            var matches = broker.SearchText( Path.Combine( home, "notes" ), "BUG", 8, 4096, default );
            Equal( 1, matches.Count );
            Equal( 2, matches[0].Line );

            Directory.Move( Path.Combine( home, "notes" ), Path.Combine( home, "notes-moved" ) );
            Directory.CreateDirectory( Path.Combine( home, "notes" ) );
            True( !await broker.IsGrantedAsync( Path.Combine( home, "notes" ), default ) );
            var grants = await catalog.AgentPreferences.GetAgentGrantsAsync( default );
            Equal( AgentGrantStates.Stale, grants[0].State );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentContextBrokerRejectsIntermediateSymlinkEscapesAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-agent-ctx-link-{Guid.NewGuid( ):N}" );
        var home = Path.Combine( root, "home" );
        var safe = Path.Combine( home, "safe" );
        var protectedDirectory = Path.Combine( home, ".ssh" );
        Directory.CreateDirectory( safe );
        Directory.CreateDirectory( protectedDirectory );
        await File.WriteAllTextAsync( Path.Combine( safe, "note.txt" ), "approved" );
        await File.WriteAllTextAsync( Path.Combine( protectedDirectory, "note.txt" ), "secret" );
        Directory.CreateSymbolicLink( Path.Combine( safe, "redirect" ), protectedDirectory );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            AgentContextBroker broker = new( new AgentAuthority( catalog.AgentPreferences ), home );
            AgentPreferences preferences = new( )
            {
                TrustLevel = AgentTrustLevels.ReadEntireHome
            };
            var redirected = Path.Combine( safe, "redirect", "note.txt" );
            var authorized = await broker.AuthorizeAsync(
                redirected, AgentContextOperations.ReadFile, preferences, default );
            True( authorized.Allowed );
            Throws<InvalidOperationException>( ( ) => broker.ReadTextFile( authorized.CanonicalPath, 64, out _ ) );
            Throws<InvalidOperationException>( ( ) => broker.ListDirectory( Path.Combine( safe, "redirect" ), 8 ) );
            Throws<InvalidOperationException>( ( ) => broker.SearchText(
                Path.Combine( safe, "redirect" ), "secret", 8, 1024, default ) );

            var beforeSwap = await broker.AuthorizeAsync(
                Path.Combine( safe, "note.txt" ), AgentContextOperations.ReadFile, preferences, default );
            Directory.Move( safe, Path.Combine( home, "safe-original" ) );
            Directory.CreateSymbolicLink( safe, protectedDirectory );
            Throws<InvalidOperationException>( ( ) => broker.ReadTextFile( beforeSwap.CanonicalPath, 64, out _ ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentCommandToolPlansLiteralArgvAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-agent-cmd-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            AgentCommandTool tool = new( root );
            var plan = tool.Plan( ["/usr/bin/printf", "hello %s\n", "world"], null );
            Equal( "/usr/bin/printf", plan.Executable );
            Equal( root, plan.WorkingDirectory );
            True( plan.ExecutableIdentity.Length > 0 && plan.Digest.Length == 64 );
            Equal( AgentCommandTool.FixedPath, plan.Environment["PATH"] );
            Equal( "C.UTF-8", plan.Environment["LANG"] );
            Equal( "C.UTF-8", plan.Environment["LC_ALL"] );
            var changedEnvironmentDigest = AgentCommandTool.ComputeDigest(
                plan.ExecutableIdentity,
                plan.Argv,
                plan.WorkingDirectory,
                plan.EnvironmentProfile,
                new Dictionary<string, string>( plan.Environment, StringComparer.Ordinal )
                {
                    ["PATH"] = "/tmp/user-writable:/usr/bin:/bin"
                } );
            True( changedEnvironmentDigest != plan.Digest );
            var same = tool.Plan( ["printf", "hello %s\n", "world"], root );
            Equal( plan.Digest, same.Digest );
            var different = tool.Plan( ["printf", "hello %s\n", "there"], root );
            True( different.Digest != plan.Digest );
            Throws<InvalidOperationException>( ( ) => tool.Plan( ["definitely-missing-binary-xyz"], null ) );
            Throws<InvalidOperationException>( ( ) => tool.Plan( ["./relative"], null ) );
            Throws<InvalidOperationException>( ( ) => tool.Plan( ["printf"], Path.Combine( root, "missing" ) ) );
            var outcome = await AgentCommandTool.ExecuteAsync( plan, default );
            Equal( 0, outcome.ExitCode );
            True( outcome.Output.StartsWith( "hello world", StringComparison.Ordinal ) );
            var escaped = AgentCommandTool.EscapeTerminalControls( "a" + ( char ) 27 + "b\n" );
            Equal( "a" + ( char ) 0xFFFD + "b\n", escaped );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AgentTrustServiceBindsChallengesAndLeasesAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-agent-trust-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            AdjustableTimeProvider clock = new( new DateTimeOffset( 2026, 9, 2, 12, 0, 0, TimeSpan.Zero ) );
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) }, clock );
            await catalog.InitializeAsync( default );
            AgentAuthority authority = new( catalog.AgentPreferences, clock );
            AgentTrustService trust = new( authority, clock );
            var current = await catalog.AgentPreferences.SetAgentPreferencesAsync( new AgentPreferences { Enabled = true }, default );
            var challenge = trust.CreateChallenge( "client-a", AgentTrustLevels.Unrestricted, current );
            True( challenge.RequiresPhrase && challenge.RequiresDuration && challenge.Warnings.Count >= 8 );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await trust.ApplyAsync( "client-b", challenge.Id, AgentTrustLevels.Unrestricted, AgentLimits.UnrestrictedPhrase, AgentTrustDurations.OneHour, default ) );
            var second = trust.CreateChallenge( "client-a", AgentTrustLevels.Unrestricted, current );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await trust.ApplyAsync( "client-a", second.Id, AgentTrustLevels.Unrestricted, "allow unrestricted agent", AgentTrustDurations.OneHour, default ) );
            var third = trust.CreateChallenge( "client-a", AgentTrustLevels.Unrestricted, current );
            var applied = await trust.ApplyAsync(
                "client-a", third.Id, AgentTrustLevels.Unrestricted, AgentLimits.UnrestrictedPhrase, AgentTrustDurations.OneHour, default );
            Equal( AgentTrustLevels.Unrestricted, applied.TrustLevel );
            True( applied.TrustLeaseExpiresUnixMilliseconds == clock.GetUtcNow( ).AddMinutes( 60 ).ToUnixTimeMilliseconds( ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await trust.ApplyAsync( "client-a", third.Id, AgentTrustLevels.Unrestricted, AgentLimits.UnrestrictedPhrase, AgentTrustDurations.OneHour, default ) );
            var stillValid = (await authority.LoadAsync( default )).Snapshot.Preferences;
            Equal( AgentTrustLevels.Unrestricted, stillValid.TrustLevel );
            clock.Advance( TimeSpan.FromMinutes( 61 ) );
            var expired = (await authority.LoadAsync( default )).Snapshot.Preferences;
            Equal( AgentTrustLevels.Default, expired.TrustLevel );
            var stale = trust.CreateChallenge( "client-a", AgentTrustLevels.Packages, expired );
            clock.Advance( TimeSpan.FromSeconds( AgentLimits.ChallengeLifetimeSeconds + 1 ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await trust.ApplyAsync( "client-a", stale.Id, AgentTrustLevels.Packages, null, null, default ) );
            var described = AgentTrustService.Describe( expired );
            Equal( "Approved context", described.LevelName );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
