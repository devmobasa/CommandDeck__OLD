namespace CommandDeck.Specs;

public static partial class Program
{
    // The connection's AI barrier and the preflight authority gate are one
    // RevisionGate: ai.clear advances the revision and waits for the holder, an
    // entry that captured the old revision is turned away without holding the
    // gate, and a lease disposed twice releases the gate once.
    private static async Task AiClearIsPerClientBarrierAsync( )
    {
        using RevisionGate barrier = new( );
        static Exception Cleared( ) => new InvalidOperationException( "cleared" );
        var operationEpoch = barrier.Revision;
        var activeOperation = await barrier.EnterAsync( operationEpoch, Cleared, default );

        barrier.Advance( );
        var clearAcknowledged = false;
        var clear = Task.Run( async ( ) =>
        {
            using var lease = await barrier.EnterAsync( expectedRevision: null, Cleared, default );
            clearAcknowledged = true;
        } );
        await Task.Yield( );
        True( !clear.IsCompleted );
        True( !clearAcknowledged );

        var staleOperation = barrier.EnterAsync( operationEpoch, Cleared, default ).AsTask( );
        activeOperation.Dispose( );
        activeOperation.Dispose( );
        await clear;
        True( clearAcknowledged );
        await ThrowsAsync<InvalidOperationException>( async ( ) =>
        {
            using var _ = await staleOperation;
        } );

        var postClearEpoch = barrier.Revision;
        var postClear = await barrier.EnterAsync( postClearEpoch, Cleared, default );
        var waiting = barrier.EnterAsync( expectedRevision: null, Cleared, default ).AsTask( );
        await Task.Yield( );
        True( !waiting.IsCompleted, "a doubly disposed lease released the gate once" );
        postClear.Dispose( );
        (await waiting.WaitAsync( TimeSpan.FromSeconds( 2 ) )).Dispose( );
    }

    private static async Task AiCancellationOwnershipSerializesClearAndDisposalAsync( )
    {
        using CancellationTokenSource source = new( );
        using ActiveAiGenerationCancellation active = new( source );
        TaskCompletionSource callbackEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously );
        TaskCompletionSource releaseCallback = new(
            TaskCreationOptions.RunContinuationsAsynchronously );
        using var registration = source.Token.Register( ( ) =>
        {
            callbackEntered.SetResult( );
            releaseCallback.Task.GetAwaiter( ).GetResult( );
        } );

        var cancel = Task.Run( active.Cancel );
        await callbackEntered.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
        var dispose = Task.Run( active.Dispose );
        await Task.Yield( );
        True( !dispose.IsCompleted );
        releaseCallback.SetResult( );
        await cancel;
        await dispose;
        active.Cancel( );
    }

    private static Task AiCompletedOutputRetentionIsBoundedAsync( )
    {
        AiOutputCache cache = new( maximumEntries: 16 );
        Parallel.For( 0, 128, generation =>
            cache.Store( generation, $"client-{generation}", new string( 'x', 1024 ) ) );
        Equal( 16, cache.Count );
        return Task.CompletedTask;
    }

    private static async Task AiOutputStagingIsPrivateAtCreationAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-private-create-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        var path = Path.Combine( root, "staging.tmp" );
        try
        {
            await using var stream = SafeRegularFile.CreatePrivateWriteStream( path );
            Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode( path ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AssertAiOutputIdentityAsync(
        AiGenerationCoordinator generations,
        string owner,
        long generation,
        string root )
    {
        RecordingActionExecutor outputActions = new( );
        using var portal = await DesktopPortalFixture.StartAsync( );
        using AiOutputService outputs = new( generations, outputActions,
            opener: new DesktopPortalFileOpener( portal.Address ) );
        var savedPath = Path.Combine( root, "saved-output.txt" );
        var saved = await outputs.ExecuteAsync(
            owner, generation, "save", savedPath, null, default );
        if ( !saved.Succeeded )
        {
            throw new InvalidOperationException( "initial save failed" );
        }
        Equal( "one two", await File.ReadAllTextAsync( savedPath ) );

        List<string> overwriteTokens = [];
        for ( var index = 0; index < 20; index++ )
        {
            var challenge = await outputs.ExecuteAsync(
                owner, generation, "save", savedPath, null, default );
            if ( challenge.Succeeded )
            {
                throw new InvalidOperationException( "overwrite challenge succeeded" );
            }
            overwriteTokens.Add( challenge.ConfirmationToken! );
        }
        var evictedChallenge = await outputs.ExecuteAsync(
            owner, generation, "save", savedPath, overwriteTokens[0], default );
        if ( evictedChallenge.Succeeded )
        {
            throw new InvalidOperationException( "evicted token was accepted" );
        }
        if ( evictedChallenge.ConfirmationToken is null )
        {
            throw new InvalidOperationException( "evicted token did not produce a fresh challenge" );
        }

        var competing = await Task.WhenAll(
            overwriteTokens[^2..].Select( token => outputs.ExecuteAsync(
                owner, generation, "save", savedPath, token, default ).AsTask( ) ) );
        var successfulOverwrites = competing.Count( static result => result.Succeeded );
        if ( successfulOverwrites != 1 )
        {
            throw new InvalidOperationException(
                $"Expected one atomic overwrite, got {successfulOverwrites}: " +
                string.Join( " | ", competing.Select( static result => result.Message ) ) );
        }

        var opened = await outputs.ExecuteAsync(
            owner, generation, "open", null, null, default );
        if ( !opened.Succeeded )
        {
            throw new InvalidOperationException( "saved output did not open: " + opened.Message );
        }
        Equal( 1, portal.OpenCount );
        Equal( 0, outputActions.Actions.Count );
        Equal( "one two", portal.ReadAfterLaunch( ) );

        foreach ( var response in new uint[] { 1, 2 } )
        {
            portal.Response = response;
            var rejected = await outputs.ExecuteAsync( owner, generation, "open", null, null, default );
            True( !rejected.Succeeded, "portal cancellation and launch failure must not report success" );
        }

        var moved = Path.Combine( root, "moved-output.txt" );
        var unrelated = Path.Combine( root, "unrelated.txt" );
        File.Move( savedPath, moved );
        await File.WriteAllTextAsync( unrelated, "different file" );
        File.CreateSymbolicLink( savedPath, unrelated );
        var replaced = await outputs.ExecuteAsync(
            owner, generation, "open", null, null, default );
        if ( replaced.Succeeded )
        {
            throw new InvalidOperationException( "replacement path opened" );
        }
        if ( !replaced.Message!.Contains( "changed", StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( "replacement failure was not classified" );
        }
        Equal( 3, portal.OpenCount );
        Equal( "one two", portal.ReadAfterLaunch( ) );
        outputs.ClearClient( owner );
        Equal( "one two", portal.ReadAfterLaunch( ) );

        var rollbackTarget = Path.Combine( root, "rollback-output.txt" );
        await File.WriteAllTextAsync( rollbackTarget, "original user bytes" );
        using AiOutputService rollbackOutputs = new(
            generations,
            outputActions,
            atomicExchange: new FailRollbackAtomicExchange( ) );
        var rollbackChallenge = await rollbackOutputs.ExecuteAsync(
            owner, generation, "save", rollbackTarget, null, default );
        if ( rollbackChallenge.Succeeded )
        {
            throw new InvalidOperationException( "rollback target skipped overwrite confirmation" );
        }
        var rollbackFailure = await rollbackOutputs.ExecuteAsync(
            owner,
            generation,
            "save",
            rollbackTarget,
            rollbackChallenge.ConfirmationToken,
            default );
        if ( rollbackFailure.Succeeded )
        {
            throw new InvalidOperationException( "injected rollback failure reported success" );
        }
        var recoveryFiles = Directory.GetFiles( root, ".commanddeck-ai-*.tmp" );
        if ( !recoveryFiles.Any( path => File.ReadAllText( path ) == "original user bytes" ) )
        {
            throw new InvalidOperationException( "rollback cleanup deleted the displaced file" );
        }
    }

    private static async Task AssertTerminalSinkAttemptedOnceAsync(
        AiGenerationCoordinator generations,
        AiPreflightService preflights,
        string owner )
    {
        var preflight = await preflights.CreateAsync( owner, new AiDraft
        {
            ProviderId = "ollama",
            ModelId = "qwen",
            UserText = "Terminal sink failure",
            Attachments = []
        }, default );
        var terminalAttempts = 0;
        await ThrowsAsync<InvalidOperationException>( async ( ) =>
            await generations.RunAsync(
                owner,
                preflight.Id,
                preflight.PayloadDigest,
                preflight.Generation,
                _ => ValueTask.CompletedTask,
                _ =>
                {
                    terminalAttempts++;
                    throw new InvalidOperationException( "terminal sink failed" );
                },
                default ) );
        Equal( 1, terminalAttempts );
    }

    private static async Task AssertTemplateMutationInvalidatesPreflightAsync( string parentRoot )
    {
        var root = Path.Combine( parentRoot, "runtime-template-invalidation" );
        Directory.CreateDirectory( root );
        var database = Path.Combine( root, "catalog.db" );
        var config = Path.Combine( root, "config.jsonc" );
        var escapedDatabase = System.Text.Json.JsonSerializer.Serialize( database );
        await File.WriteAllTextAsync( config, $$"""
            {
              "storage": { "databasePath": {{escapedDatabase}} },
              "indexing": {
                "roots": [],
                "indexDesktopApplications": false,
                "indexOmarchyCommands": false,
                "enableIncrementalIndexing": false,
                "enableScheduledMaintenance": false
              },
              "ai": {
                "enabled": true,
                "providers": [{
                  "id": "ollama",
                  "displayName": "Ollama",
                  "kind": "ollama",
                  "baseUrl": "http://127.0.0.1:9",
                  "model": "qwen"
                }]
              }
            }
            """ );
        await using var runtime = await CommandDeckRuntime.CreateAsync( config, default );
        AiTemplate template = new( )
        {
            Id = "mutable-template",
            Name = "Mutable template",
            WorkflowKind = "custom",
            SystemTemplate = "Version one.",
            UserTemplate = "Review {{input}}"
        };
        _ = await runtime.SaveAiTemplateAsync( template, default );
        var preflight = await runtime.CreateAiPreflightAsync( "template-owner", new AiDraft
        {
            ProviderId = "ollama",
            ModelId = "qwen",
            TemplateId = template.Id,
            UserText = "authority",
            Attachments = []
        }, default );
        _ = await runtime.SaveAiTemplateAsync( new AiTemplate
        {
            Id = template.Id,
            Name = template.Name,
            WorkflowKind = template.WorkflowKind,
            SystemTemplate = "Version two.",
            UserTemplate = template.UserTemplate
        }, default );
        List<AiDone> terminals = [];
        await runtime.StreamAiAsync(
            "template-owner",
            preflight.Id,
            preflight.PayloadDigest,
            preflight.Generation,
            _ => ValueTask.CompletedTask,
            done =>
            {
                terminals.Add( done );
                return ValueTask.CompletedTask;
            },
            default );
        Equal( 1, terminals.Count );
        Equal( AiErrorCategories.StalePreflight, terminals[0].Error!.Category );

        var deletePreflight = await runtime.CreateAiPreflightAsync(
            "template-owner", new AiDraft
            {
                ProviderId = "ollama",
                ModelId = "qwen",
                TemplateId = template.Id,
                UserText = "delete authority",
                Attachments = []
            }, default );
        _ = await runtime.DeleteAiTemplateAsync( template.Id, default );
        terminals.Clear( );
        await runtime.StreamAiAsync(
            "template-owner",
            deletePreflight.Id,
            deletePreflight.PayloadDigest,
            deletePreflight.Generation,
            _ => ValueTask.CompletedTask,
            done =>
            {
                terminals.Add( done );
                return ValueTask.CompletedTask;
            },
            default );
        Equal( 1, terminals.Count );
        Equal( AiErrorCategories.StalePreflight, terminals[0].Error!.Category );
    }

    private sealed class FailRollbackAtomicExchange : IAtomicFileExchange
    {
        private int _calls;

        public void Exchange( string firstPath, string secondPath )
        {
            var call = Interlocked.Increment( ref _calls );
            if ( call >= 2 )
            {
                throw new InvalidOperationException( "injected rollback failure" );
            }
            var preserved = secondPath + ".injected-original";
            File.Move( secondPath, preserved );
            File.Copy( preserved, secondPath );
            SafeRegularFile.ExchangePaths( firstPath, secondPath );
        }
    }
}
