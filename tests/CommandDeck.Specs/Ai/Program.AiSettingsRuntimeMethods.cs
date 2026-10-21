using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task AiSettingsRuntimeAppliesLiveMutationsAndPersistsAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-ai-settings-runtime-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        await using ManagedAiFixtureServer fixture = await ManagedAiFixtureServer.StartAsync( );
        try
        {
            var config = await WriteManagedSpecConfigAsync( root, new AiOptions
            {
                Enabled = false,
                Providers = [LocalProviderDefinition( "legacy", fixture.BaseUrl )]
            } );
            var managedPath = Path.Combine( root, AiProviderSettingsStore.FileName );
            await using ( var runtime = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None ) )
            {
                var initial = await runtime.GetAiSettingsAsync( default );
                Equal( AiProviderSettingsSources.LegacyConfiguration, initial.Source );
                True( !initial.Enabled && initial.Providers.Count == 1 && initial.Presets.Count == 3 );
                Equal( 0, runtime.GetAiProviders( ).Providers.Count );
                True( !File.Exists( managedPath ) );

                // Reserved IDs and conflicts are rejected by the daemon, not just the UI.
                await ThrowsAsync<InvalidOperationException>( async ( ) => await runtime.SaveAiProviderAsync(
                    LocalProviderDefinition( AgentBackendIds.Grok, fixture.BaseUrl ), true, 0, false, default ) );
                var conflict = await ThrowsAiAsync( async ( ) => await runtime.UpdateAiEnabledAsync( true, 5, default ) );
                Equal( AiErrorCategories.Conflict, conflict.Category );
                True( !File.Exists( managedPath ) );

                // Enabling seeds the managed document from the legacy set.
                var enabled = await runtime.UpdateAiEnabledAsync( true, 0, default );
                Equal( AiProviderSettingsSources.ManagedDocument, enabled.Source );
                Equal( 1L, enabled.Revision );
                True( enabled.Enabled && enabled.LegacyIgnored && enabled.Providers.Count == 1 );
                True( File.Exists( managedPath ) );
                Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite, UnixModeOf( managedPath ) );
                Equal( 1, runtime.GetAiProviders( ).Providers.Count );
                Equal( 0, fixture.TagRequests );

                // Save and use: provider committed, selection acknowledged, live inventory updated.
                var saved = await runtime.SaveAiProviderAsync(
                    LocalProviderDefinition( "added", fixture.BaseUrl ), true, 1, true, default );
                Equal( 2L, saved.Settings.Revision );
                True( saved.SelectionRequested && saved.SelectionApplied && saved.SelectionError is null );
                Equal( "added", saved.Selection?.ProviderId );
                Equal( 2, runtime.GetAiProviders( ).Providers.Count );
                var models = await runtime.DiscoverAiModelsAsync( "added", default );
                True( models.Error is null && models.Models.Count == 2 );
                Equal( 1, fixture.TagRequests );
                Equal( AiSelectionStates.Ready, (await runtime.GetAiSelectionAsync( default )).State );

                // Duplicate create and missing edit are categorised.
                var duplicate = await ThrowsAiAsync( async ( ) => await runtime.SaveAiProviderAsync(
                    LocalProviderDefinition( "ADDED", fixture.BaseUrl ), true, 2, false, default ) );
                Equal( AiErrorCategories.Conflict, duplicate.Category );
                var missing = await ThrowsAiAsync( async ( ) => await runtime.SaveAiProviderAsync(
                    LocalProviderDefinition( "ghost", fixture.BaseUrl ), false, 2, false, default ) );
                Equal( AiErrorCategories.NotFound, missing.Category );
                Equal( 2L, (await runtime.GetAiSettingsAsync( default )).Revision );

                // A definition change invalidates an outstanding preflight.
                var preflight = await runtime.CreateAiPreflightAsync( "client-a", new AiDraft
                {
                    ProviderId = "added",
                    ModelId = "m1",
                    UserText = "hello",
                    Attachments = []
                }, default );
                var edited = LocalProviderDefinition( "added", fixture.BaseUrl, model: "m2" );
                var editResult = await runtime.SaveAiProviderAsync( edited, false, 2, false, default );
                Equal( 3L, editResult.Settings.Revision );
                AiDone? terminal = null;
                await runtime.StreamAiAsync( "client-a", preflight.Id, preflight.PayloadDigest, preflight.Generation,
                    _ => ValueTask.CompletedTask, done =>
                    {
                        terminal = done;
                        return ValueTask.CompletedTask;
                    }, default );
                Equal( AiGenerationOutcomes.Failed, terminal?.Outcome );
                Equal( AiErrorCategories.StalePreflight, terminal?.Error?.Category );
                Equal( 0, fixture.ChatRequests );

                // Draft tests do not persist and honour the disabled state.
                var tested = await runtime.TestAiProviderDraftAsync( LocalProviderDefinition( "draft", fixture.BaseUrl ), 4, 3, default );
                Equal( AiProviderTestOutcomes.Models, tested.Outcome );
                Equal( 3L, (await runtime.GetAiSettingsAsync( default )).Revision );
                True( !(await runtime.GetAiSettingsAsync( default )).Providers.Any( static value => value.Id == "draft" ) );
                var staleTest = await ThrowsAiAsync( async ( ) => await runtime.TestAiProviderDraftAsync(
                    LocalProviderDefinition( "draft", fixture.BaseUrl ), 5, 1, default ) );
                Equal( AiErrorCategories.Conflict, staleTest.Category );

                // Removing the legacy provider and disabling direct AI.
                var removed = await runtime.DeleteAiProviderAsync( "legacy", 3, default );
                Equal( 4L, removed.Revision );
                Equal( 1, removed.Providers.Count );
                var notFound = await ThrowsAiAsync( async ( ) => await runtime.DeleteAiProviderAsync( "legacy", 4, default ) );
                Equal( AiErrorCategories.NotFound, notFound.Category );
                var disabled = await runtime.UpdateAiEnabledAsync( false, 4, default );
                True( !disabled.Enabled && disabled.Providers.Count == 1 );
                Equal( 0, runtime.GetAiProviders( ).Providers.Count );
                var disabledTest = await ThrowsAiAsync( async ( ) => await runtime.TestAiProviderDraftAsync(
                    LocalProviderDefinition( "draft", fixture.BaseUrl ), 6, 5, default ) );
                Equal( AiErrorCategories.Disabled, disabledTest.Category );

                // Save and use while disabled: provider saved, selection reported as not changed.
                var partial = await runtime.SaveAiProviderAsync(
                    LocalProviderDefinition( "offline", fixture.BaseUrl ), true, 5, true, default );
                Equal( 6L, partial.Settings.Revision );
                True( partial.SelectionRequested && !partial.SelectionApplied );
                Equal( AiErrorCategories.Disabled, partial.SelectionError?.Category );
                True( partial.Settings.Providers.Any( static value => value.Id == "offline" ) );
                Equal( "added", (await runtime.GetAiSelectionAsync( default )).ProviderId );

                // Removing the selected provider preserves the selection as unavailable.
                _ = await runtime.UpdateAiEnabledAsync( true, 6, default );
                _ = await runtime.DeleteAiProviderAsync( "added", 7, default );
                var orphaned = await runtime.GetAiSelectionAsync( default );
                Equal( "added", orphaned.ProviderId );
                Equal( AiSelectionStates.Unavailable, orphaned.State );

                // Search still works and never consults the fixture: one explicit
                // refresh and one explicit draft test are the only discovery calls.
                var results = await runtime.SearchAsync( new SearchRequest { Query = "ai: hello", Limit = 8 }, default );
                True( results.All( static value => value.Id != "ai:added" ) );
                Equal( 2, fixture.TagRequests );
            }

            // Restart: the managed document owns the set; the removed legacy provider does not return.
            await using ( var restarted = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None ) )
            {
                var settings = await restarted.GetAiSettingsAsync( default );
                Equal( AiProviderSettingsSources.ManagedDocument, settings.Source );
                Equal( 8L, settings.Revision );
                True( settings.Enabled && settings.LegacyIgnored );
                True( settings.Providers.Select( static value => value.Id ).SequenceEqual( ["offline"] ) );
                Equal( AiSelectionStates.Unavailable, (await restarted.GetAiSelectionAsync( default )).State );
            }

            // A damaged document at startup fails closed and stays editable only after repair.
            await File.WriteAllTextAsync( managedPath, "{ broken" );
            await using ( var damaged = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None ) )
            {
                var settings = await damaged.GetAiSettingsAsync( default );
                True( !settings.Enabled && settings.Error?.Category == AiErrorCategories.InvalidConfiguration );
                Equal( 0, settings.Providers.Count );
                Equal( 0, damaged.GetAiProviders( ).Providers.Count );
                var rejected = await ThrowsAiAsync( async ( ) => await damaged.UpdateAiEnabledAsync( true, null, default ) );
                Equal( AiErrorCategories.InvalidConfiguration, rejected.Category );
                var status = await damaged.GetRuntimeStatusAsync( default );
                True( status.Providers.Any( static value => value.Id == "ai" && value.State != RuntimeHealthStates.Available ) );
                _ = await damaged.SearchAsync( new SearchRequest { Query = "hello", Limit = 8 }, default );
            }
            Equal( "{ broken", await File.ReadAllTextAsync( managedPath ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AiSettingsRuntimeCancelsAffectedGenerationsAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-ai-settings-cancel-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        await using ManagedAiFixtureServer fixture = await ManagedAiFixtureServer.StartAsync( );
        try
        {
            var config = await WriteManagedSpecConfigAsync( root, new AiOptions
            {
                Enabled = true,
                Providers =
                [
                    LocalProviderDefinition( "one", fixture.BaseUrl ),
                    LocalProviderDefinition( "two", fixture.BaseUrl )
                ]
            } );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None );

            // A model change cancels the active stream on that provider within the bound.
            (var task, var done) = StartHeldGeneration( runtime, "client-a", "one" );
            await fixture.GenerationStarted.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            var started = TimeProvider.System.GetTimestamp( );
            var edited = await runtime.SaveAiProviderAsync(
                LocalProviderDefinition( "one", fixture.BaseUrl, model: "changed" ), false, 0, false, default );
            True( TimeProvider.System.GetElapsedTime( started ) < TimeSpan.FromSeconds( 5 ) );
            await task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( AiGenerationOutcomes.Cancelled, done.Value?.Outcome );
            Equal( 1L, edited.Settings.Revision );
            True( runtime.EffectiveAiSettings.Registry.TryGet( "one", out _ ) );

            // A display-name-only edit leaves an active stream running to completion.
            fixture.ResetGeneration( );
            (var unaffected, var unaffectedDone) = StartHeldGeneration( runtime, "client-b", "two" );
            await fixture.GenerationStarted.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            var renamed = await runtime.SaveAiProviderAsync(
                LocalProviderDefinition( "two", fixture.BaseUrl, displayName: "Renamed" ), false, 1, false, default );
            Equal( 2L, renamed.Settings.Revision );
            True( !unaffected.IsCompleted );
            fixture.ReleaseGeneration( );
            await unaffected.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( AiGenerationOutcomes.Completed, unaffectedDone.Value?.Outcome );

            // Master disable cancels every direct stream.
            fixture.ResetGeneration( );
            (var all, var allDone) = StartHeldGeneration( runtime, "client-c", "two" );
            await fixture.GenerationStarted.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            _ = await runtime.UpdateAiEnabledAsync( false, 2, default );
            await all.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            Equal( AiGenerationOutcomes.Cancelled, allDone.Value?.Outcome );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AiSettingsSocketAndCliUseTheDaemonAsync( )
    {
        var root = CreateCliSpecRoot( );
        await using ManagedAiFixtureServer fixture = await ManagedAiFixtureServer.StartAsync( );
        using CancellationTokenSource serverCancellation = new( );
        try
        {
            var config = await WriteManagedSpecConfigAsync( root, new AiOptions { Enabled = true, Providers = [] } );
            var socket = SpecTemporaryFiles.SocketPath( root, "commanddeck.sock" );
            await using var runtime = await CommandDeckRuntime.CreateAsync( config, CancellationToken.None );
            UnixProtocolServer server = new( runtime, socket );
            var serverTask = server.RunAsync( serverCancellation.Token );
            await using var client = await ConnectWithRetryAsync( socket );

            await client.SendAsync( new ProtocolRequest { RequestId = 1, Operation = ProtocolOperations.AiSettingsGet }, default );
            var settings = await ReadMessageAsync( client, 1, ProtocolMessageTypes.AiSettingsResult, default );
            True( settings?.AiSettings is { Enabled: true, Revision: 0 } );
            Equal( AiProviderSettingsSources.LegacyConfiguration, settings!.AiSettings!.Source );

            await client.SendAsync( new ProtocolRequest
            {
                RequestId = 2,
                Operation = ProtocolOperations.AiProvidersSave,
                AiProvider = LocalProviderDefinition( "socket", fixture.BaseUrl ),
                Create = true,
                ExpectedRevision = 9
            }, default );
            var conflict = await ReadMessageAsync( client, 2, ProtocolMessageTypes.Error, default );
            True( conflict?.Error?.StartsWith( AiErrorCategories.Conflict + ": ", StringComparison.Ordinal ) == true );

            await client.SendAsync( new ProtocolRequest
            {
                RequestId = 3,
                Operation = ProtocolOperations.AiProvidersSave,
                AiProvider = LocalProviderDefinition( "socket", fixture.BaseUrl ),
                Create = true,
                ExpectedRevision = 0,
                SelectAfterSave = true
            }, default );
            var saved = await ReadMessageAsync( client, 3, ProtocolMessageTypes.AiProviderSaveResult, default );
            True( saved?.AiProviderSave is { SelectionApplied: true } && saved.AiProviderSave.Settings.Revision == 1 );
            True( !JsonSerializer.Serialize( saved!.AiProviderSave!, CommandDeckJsonContext.Default.AiProviderSaveResult )
                .Contains( "apiKey", StringComparison.OrdinalIgnoreCase ) );

            await client.SendAsync( new ProtocolRequest
            {
                RequestId = 4,
                Operation = ProtocolOperations.AiProvidersTest,
                AiProvider = LocalProviderDefinition( "draft", fixture.BaseUrl ),
                DraftRevision = 2,
                ExpectedRevision = 1
            }, default );
            var tested = await ReadMessageAsync( client, 4, ProtocolMessageTypes.AiProviderTestResult, default );
            True( tested?.AiProviderTest is { Outcome: AiProviderTestOutcomes.Models, DraftRevision: 2 } );

            await client.SendAsync( new ProtocolRequest
            {
                RequestId = 5,
                Operation = ProtocolOperations.AiProvidersDelete,
                ProviderId = "socket",
                ExpectedRevision = 1
            }, default );
            var unconfirmed = await ReadMessageAsync( client, 5, ProtocolMessageTypes.Error, default );
            True( unconfirmed?.Error?.Contains( "confirmed", StringComparison.Ordinal ) == true );

            // The CLI path uses the daemon socket and prints the same documents.
            var show = await RunCommandDeckProcessAsync( "ai", "settings", "show", "--json", "--socket", socket );
            Equal( 0, show.ExitCode );
            var shown = JsonSerializer.Deserialize( show.StandardOutput, CommandDeckJsonContext.Default.AiProviderSettings );
            True( shown is { Revision: 1 } && shown.Providers.Count == 1 );
            var added = await RunCommandDeckProcessAsync(
                "ai", "providers", "add", "cli", "--preset", "ollama", "--base-url", fixture.BaseUrl.TrimEnd( '/' ),
                "--model", "m1", "--json", "--socket", socket );
            Equal( 0, added.ExitCode );
            var addResult = JsonSerializer.Deserialize( added.StandardOutput, CommandDeckJsonContext.Default.AiProviderSaveResult );
            True( addResult is { ProviderId: "cli" } && addResult.Settings.Revision == 2 );
            var reserved = await RunCommandDeckProcessAsync(
                "ai", "providers", "add", AgentBackendIds.Cursor, "--preset", "ollama", "--base-url", fixture.BaseUrl.TrimEnd( '/' ),
                "--model", "m1", "--socket", socket );
            True( reserved.ExitCode != 0 );
            var removeWithoutYes = await RunCommandDeckProcessAsync( "ai", "providers", "remove", "cli", "--socket", socket );
            Equal( 2, removeWithoutYes.ExitCode );
            var testCli = await RunCommandDeckProcessAsync( "ai", "providers", "test", "cli", "--socket", socket );
            Equal( 0, testCli.ExitCode );
            True( testCli.StandardOutput.Contains( "Outcome:     models", StringComparison.Ordinal ) );
            var disabledCli = await RunCommandDeckProcessAsync( "ai", "providers", "disable", "cli", "--socket", socket );
            Equal( 0, disabledCli.ExitCode );
            var removed = await RunCommandDeckProcessAsync( "ai", "providers", "remove", "cli", "--yes", "--json", "--socket", socket );
            Equal( 0, removed.ExitCode );
            var afterRemove = JsonSerializer.Deserialize( removed.StandardOutput, CommandDeckJsonContext.Default.AiProviderSettings );
            True( afterRemove is { Revision: 4 } && afterRemove.Providers.All( static value => value.Id != "cli" ) );
            var offline = await RunCommandDeckProcessAsync( "ai", "settings", "show", "--socket", SpecTemporaryFiles.SocketPath( root, "missing.sock" ) );
            Equal( 1, offline.ExitCode );
            True( offline.StandardError.Contains( "daemon", StringComparison.OrdinalIgnoreCase ) );

            serverCancellation.Cancel( );
            await serverTask;
        }
        finally
        {
            serverCancellation.Cancel( );
            Directory.Delete( root, recursive: true );
        }
    }
}
