namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task AiFileAuthorityIsHandleFirstAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-ai-handle-{Guid.NewGuid( ):N}" );
        var allowed = Path.Combine( root, "allowed" );
        var outside = Path.Combine( root, "outside" );
        Directory.CreateDirectory( allowed );
        Directory.CreateDirectory( outside );
        var selected = Path.Combine( allowed, "selected.txt" );
        var moved = Path.Combine( allowed, "moved.txt" );
        var forbidden = Path.Combine( outside, "forbidden.txt" );
        await File.WriteAllTextAsync( selected, "authorized identity" );
        await File.WriteAllTextAsync( forbidden, "forbidden sentinel" );
        try
        {
            using var retained = SafeRegularFile.Open( selected );
            File.Move( selected, moved );
            File.CreateSymbolicLink( selected, forbidden );
            var bytes = new byte[64];
            var read = await retained.ReadPrefixAsync( bytes, default );
            Equal( "authorized identity", System.Text.Encoding.UTF8.GetString( bytes, 0, read ) );
            Throws<InvalidOperationException>( ( ) => SafeRegularFile.Open( selected ) );

            FilePreviewService previews = new(
                new IndexingOptions
                {
                    Roots =
                    [
                        new IndexRootOptions
                        {
                            Path = allowed,
                            IncludeContentInSearch = true,
                            FollowSymbolicLinks = true
                        }
                    ]
                },
                new PrivacyOptions { NeverIndex = [] } );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
            {
                _ = await previews.ReadAiAttachmentAsync( selected, default );
            } );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AiPreflightCapacityIsPerClientAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-ai-capacity-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions
            {
                DatabasePath = Path.Combine( root, "catalog.db" )
            } );
            await catalog.InitializeAsync( default );
            var options = OneLocalAiProvider( );
            await using AiProviderRegistry providers = new( options, new TestAiTransportFactory( ) );
            AiPreflightService service = new(
                providers,
                new FilePreviewService(
                    new IndexingOptions { Roots = [] },
                    new PrivacyOptions { NeverIndex = [] } ),
                catalog.AiPreferences,
                ( _, _ ) => null );
            List<AiPreflight> firstClient = [];
            for ( var index = 0; index < 16; index++ )
            {
                firstClient.Add( await service.CreateAsync(
                    "client-a", Draft( $"client a {index}" ), default ) );
            }
            var otherClient = await service.CreateAsync(
                "client-b", Draft( "client b retained" ), default );
            _ = await service.CreateAsync( "client-a", Draft( "client a overflow" ), default );

            await ThrowsAsync<InvalidOperationException>( async ( ) => await service.ConsumeAsync(
                "client-a",
                firstClient[0].Id,
                firstClient[0].PayloadDigest,
                firstClient[0].Generation,
                default ) );
            _ = await service.ConsumeAsync(
                "client-b",
                otherClient.Id,
                otherClient.PayloadDigest,
                otherClient.Generation,
                default );

            var invalidated = await service.CreateAsync(
                "client-c", Draft( "selection revision" ), default );
            await service.InvalidateAuthorityAsync( default );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await service.ConsumeAsync(
                "client-c",
                invalidated.Id,
                invalidated.PayloadDigest,
                invalidated.Generation,
                default ) );

            var leased = await service.CreateAsync(
                "client-d", Draft( "transport authority lease" ), default );
            var prepared = await service.ConsumeAsync(
                "client-d",
                leased.Id,
                leased.PayloadDigest,
                leased.Generation,
                default );
            var transportLease = await service.AcquireTransportLeaseAsync(
                prepared.AuthorityRevision, default );
            var mutationPersisted = false;
            var mutation = service.MutateAuthorityAsync(
                _ =>
                {
                    mutationPersisted = true;
                    return ValueTask.FromResult( true );
                }, default ).AsTask( );
            await Task.Yield( );
            True( !mutation.IsCompleted );
            True( !mutationPersisted );
            transportLease.Dispose( );
            True( await mutation );
            True( mutationPersisted );

            var cancellationPreflight = await service.CreateAsync(
                "client-e", Draft( "committed mutation cancellation" ), default );
            using CancellationTokenSource mutationCancellation = new( );
            _ = await service.MutateAuthorityAsync(
                _ =>
                {
                    mutationCancellation.Cancel( );
                    return ValueTask.FromResult( true );
                }, mutationCancellation.Token );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await service.ConsumeAsync(
                "client-e",
                cancellationPreflight.Id,
                cancellationPreflight.PayloadDigest,
                cancellationPreflight.Generation,
                default ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AiOperationsEnforceFixedDeadlinesAsync( )
    {
        AiOptions options = new( )
        {
            Enabled = true,
            Providers =
            [
                OneLocalAiProvider().Providers.Single(),
                new AiProviderOptions
                {
                    Id = "remote",
                    DisplayName = "Remote",
                    Kind = AiAdapterKinds.Ollama,
                    BaseUrl = "https://provider.example.test",
                    Model = "remote-model",
                    AllowRemoteEndpoint = true
                }
            ]
        };
        AiOperationDeadlines deadlines = new( )
        {
            LocalDiscovery = TimeSpan.FromMilliseconds( 25 ),
            RemoteDiscovery = TimeSpan.FromMilliseconds( 100 ),
            OverallGeneration = TimeSpan.FromMilliseconds( 35 )
        };
        await using ( AiProviderRegistry registry = new(
            options,
            new FixedTransportFactory( new NeverCompletingAiHandler( ) ),
            deadlines: deadlines ) )
        {
            var localElapsed = System.Diagnostics.Stopwatch.StartNew( );
            var local = await registry.DiscoverModelsAsync( "ollama", default );
            localElapsed.Stop( );
            var remoteElapsed = System.Diagnostics.Stopwatch.StartNew( );
            var remote = await registry.DiscoverModelsAsync( "remote", default );
            remoteElapsed.Stop( );
            Equal( AiErrorCategories.Timeout, local.Error!.Category );
            Equal( AiErrorCategories.Timeout, remote.Error!.Category );
            True( remoteElapsed.Elapsed > localElapsed.Elapsed + TimeSpan.FromMilliseconds( 35 ) );
            True( remoteElapsed.Elapsed < TimeSpan.FromMilliseconds( 500 ) );
        }

        AiOperationDeadlines queuedDeadlines = new( )
        {
            LocalDiscovery = TimeSpan.FromMilliseconds( 300 ),
            RemoteDiscovery = TimeSpan.FromMilliseconds( 300 ),
            OverallGeneration = TimeSpan.FromSeconds( 1 )
        };
        CountingNeverCompletingAiHandler queuedHandler = new( );
        await using ( AiProviderRegistry queuedRegistry = new(
            OneLocalAiProvider( ),
            new FixedTransportFactory( queuedHandler ),
            deadlines: queuedDeadlines ) )
        {
            var first = queuedRegistry.DiscoverModelsAsync(
                "ollama", default ).AsTask( );
            var second = queuedRegistry.DiscoverModelsAsync(
                "ollama", default ).AsTask( );
            await queuedHandler.TwoRequestsStarted.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );
            await Task.Delay( 100 );
            var queuedElapsed =
                System.Diagnostics.Stopwatch.StartNew( );
            var queued = await queuedRegistry.DiscoverModelsAsync( "ollama", default );
            queuedElapsed.Stop( );
            await Task.WhenAll( first, second );
            Equal( AiErrorCategories.Timeout, queued.Error!.Category );
            if ( queuedElapsed.Elapsed >= TimeSpan.FromMilliseconds( 400 ) )
            {
                throw new InvalidOperationException(
                    $"Queued discovery exceeded its operation budget: {queuedElapsed.Elapsed}." );
            }
            True( queuedHandler.Started is 2 or 3 );
        }

        var root = SpecTemporaryFiles.PathFor( $"commanddeck-ai-overall-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            var firstLine = System.Text.Encoding.UTF8.GetBytes(
                "{\"model\":\"qwen\",\"message\":{\"content\":\"first\"},\"done\":false}\n" );
            RecordingAiHandler handler = new( _ => new HttpResponseMessage( System.Net.HttpStatusCode.OK )
            {
                Content = new StreamContent( new CancellableAiStream( firstLine ) )
            } );
            await using SqliteCatalog catalog = new( new StorageOptions
            {
                DatabasePath = Path.Combine( root, "catalog.db" )
            } );
            await catalog.InitializeAsync( default );
            await using AiProviderRegistry providers = new(
                OneLocalAiProvider( ), new FixedTransportFactory( handler ) );
            AiPreflightService preflights = new(
                providers,
                new FilePreviewService(
                    new IndexingOptions { Roots = [] },
                    new PrivacyOptions { NeverIndex = [] } ),
                catalog.AiPreferences,
                ( _, _ ) => null );
            using AiGenerationCoordinator generations = new( preflights, deadlines );
            var preflight = await preflights.CreateAsync(
                "deadline-client", Draft( "overall deadline" ), default );
            List<AiDone> terminals = [];
            await generations.RunAsync(
                "deadline-client",
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
            Equal( AiGenerationOutcomes.Failed, terminals[0].Outcome );
            Equal( AiErrorCategories.Timeout, terminals[0].Error!.Category );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static AiOptions OneLocalAiProvider( ) => new( )
    {
        Enabled = true,
        Providers =
        [
            new AiProviderOptions
            {
                Id = "ollama",
                DisplayName = "Ollama",
                Kind = AiAdapterKinds.Ollama,
                BaseUrl = "http://127.0.0.1:11434",
                Model = "qwen"
            }
        ]
    };

    private static AiDraft Draft( string text ) => new( )
    {
        ProviderId = "ollama",
        ModelId = "qwen",
        UserText = text,
        Attachments = []
    };

    private static async Task AiAttachmentsAreReauthorizedBeforeTransmissionAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-ai-reauthorize-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        var file = Path.Combine( root, "attachment.txt" );
        await File.WriteAllTextAsync( file, "reviewed bytes" );
        try
        {
            RecordingAiHandler handler = new( _ => Json( System.Net.HttpStatusCode.OK,
                "{\"model\":\"qwen\",\"message\":{\"content\":\"sent\"},\"done\":false}\n" +
                "{\"model\":\"qwen\",\"message\":{\"content\":\"\"},\"done\":true}\n" ) );
            AiOptions options = new( )
            {
                Enabled = true,
                Providers =
                [
                    new AiProviderOptions
                    {
                        Id = "ollama",
                        DisplayName = "Ollama",
                        Kind = AiAdapterKinds.Ollama,
                        BaseUrl = "http://127.0.0.1:11434",
                        Model = "qwen"
                    }
                ]
            };
            await using SqliteCatalog catalog = new( new StorageOptions
            {
                DatabasePath = Path.Combine( root, "catalog.db" )
            } );
            await catalog.InitializeAsync( default );
            await using AiProviderRegistry providers = new(
                options, new FixedTransportFactory( handler ) );
            SearchResult cached = new( )
            {
                Id = "file:attachment",
                Kind = ResultKinds.File,
                Title = "attachment.txt",
                SourceId = SourceIds.Files,
                Metadata = new Dictionary<string, string> { ["path"] = file }
            };
            AiPreflightService preflights = new(
                providers,
                new FilePreviewService(
                    new IndexingOptions
                    {
                        Roots = [new IndexRootOptions { Path = root, IncludeContentInSearch = true }]
                    },
                    new PrivacyOptions { NeverIndex = [] } ),
                catalog.AiPreferences,
                ( _, id ) => id == cached.Id ? cached : null );
            using AiGenerationCoordinator generations = new( preflights );
            const string owner = "attachment-owner";
            var preflight = await preflights.CreateAsync( owner, new AiDraft
            {
                ProviderId = "ollama",
                ModelId = "qwen",
                UserText = "Review",
                Attachments =
                [
                    new AiAttachmentDraft
                    {
                        Kind = AiAttachmentKinds.File,
                        ResultId = cached.Id
                    }
                ]
            }, default );

            await File.WriteAllTextAsync( file, "changed after review" );
            List<AiDone> terminals = [];
            await generations.RunAsync(
                owner, preflight.Id, preflight.PayloadDigest, preflight.Generation,
                _ => ValueTask.CompletedTask,
                done =>
                {
                    terminals.Add( done );
                    return ValueTask.CompletedTask;
                }, default );

            Equal( 0, handler.Requests.Count );
            Equal( 1, terminals.Count );
            Equal( AiGenerationOutcomes.Failed, terminals[0].Outcome );
            Equal( AiErrorCategories.StalePreflight, terminals[0].Error!.Category );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private sealed class RecordingActionExecutor : IActionExecutor
    {
        public List<CommandAction> Actions { get; } = [];

        public ValueTask<ActionExecutionResult> ExecuteAsync(
            SearchResult result,
            CommandAction action,
            string query,
            CancellationToken cancellationToken )
        {
            Actions.Add( action );
            return ValueTask.FromResult( ActionExecutionResult.Success( ) );
        }
    }

    private sealed class NeverCompletingAiHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken )
        {
            await Task.Delay( Timeout.InfiniteTimeSpan, cancellationToken );
            throw new InvalidOperationException( "Unreachable after cancellation." );
        }
    }

    private sealed class CountingNeverCompletingAiHandler : HttpMessageHandler
    {
        private int _started;
        public int Started => Volatile.Read( ref _started );
        public TaskCompletionSource TwoRequestsStarted { get; } =
            new( TaskCreationOptions.RunContinuationsAsynchronously );

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken )
        {
            if ( Interlocked.Increment( ref _started ) == 2 )
            {
                TwoRequestsStarted.TrySetResult( );
            }
            await Task.Delay( Timeout.InfiniteTimeSpan, cancellationToken );
            throw new InvalidOperationException( "Unreachable after cancellation." );
        }
    }

}
