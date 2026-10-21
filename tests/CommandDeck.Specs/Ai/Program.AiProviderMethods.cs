using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task AiProvidersDiscoverAndStreamThroughExplicitContractsAsync( )
    {
        RecordingAiHandler ollamaHandler = new( request => request.RequestUri!.AbsolutePath switch
        {
            "/api/tags" => Json( HttpStatusCode.OK,
                "{\"models\":[{\"model\":\"qwen3:8b\"},{\"name\":\"qwen3:8b\"}]}" ),
            "/api/chat" => Json( HttpStatusCode.OK,
                "{\"model\":\"qwen3:8b\",\"message\":{\"content\":\"local\"},\"done\":false}\n" +
                "{\"model\":\"qwen3:8b\",\"message\":{\"content\":\"\"},\"done\":true," +
                "\"done_reason\":\"stop\",\"prompt_eval_count\":4,\"eval_count\":1}\n" ),
            _ => Json( HttpStatusCode.NotFound, "{}" )
        } );
        AiProviderOptions ollamaOptions = new( )
        {
            Id = "ollama",
            DisplayName = "Ollama",
            Kind = AiAdapterKinds.Ollama,
            BaseUrl = "http://127.0.0.1:11434",
            Model = "qwen3:8b"
        };
        using var ollamaClient = CreateClient( ollamaHandler, ollamaOptions.BaseUrl );
        OllamaAiProvider ollama = new( ollamaOptions, ollamaClient );
        var localModels =
            await ollama.DiscoverModelsAsync( default );
        Equal( 1, localModels.Count );
        Equal( "qwen3:8b", localModels[0].Id );
        var local = await ReadProviderTextAsync( ollama, new AiPrompt
        {
            Text = "sentinel-local",
            SystemPrompt = "inert text only"
        } );
        Equal( "local", local );
        Equal( 2, ollamaHandler.Requests.Count );
        True( ollamaHandler.Requests[1].Body.Contains( "\"stream\":true", StringComparison.Ordinal ) );
        True( ollamaHandler.Requests[1].Body.Contains( "sentinel-local", StringComparison.Ordinal ) );

        TrackingCredentialResolver credentialResolver = new( "provider-secret" );
        RecordingAiHandler cloudHandler = new( request =>
        {
            if ( credentialResolver.LastLease?.IsDisposed != true )
            {
                throw new InvalidOperationException(
                    "Credential bytes remained leased through transport execution." );
            }
            return request.RequestUri!.AbsolutePath switch
            {
                "/v1/models" => Json( HttpStatusCode.OK,
                    "{\"data\":[{\"id\":\"gpt-safe\",\"owned_by\":\"test\"}]}" ),
                "/v1/chat/completions" => EventStream(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"cloud\"},\"finish_reason\":null}]}\n\n" +
                    "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":1,\"total_tokens\":3}}\n\n" +
                    "data: [DONE]\n\n" ),
                _ => Json( HttpStatusCode.NotFound, "{}" )
            };
        } );
        AiProviderOptions cloudOptions = new( )
        {
            Id = "cloud",
            DisplayName = "Cloud",
            Kind = AiAdapterKinds.OpenAiChatCompletions,
            BaseUrl = "https://api.example.test/v1",
            Model = "gpt-safe",
            AllowRemoteEndpoint = true,
            Credential = new AiCredentialOptions
            {
                Kind = AiCredentialKinds.Environment,
                Name = "TEST_ONLY"
            }
        };
        using var cloudClient = CreateClient( cloudHandler, cloudOptions.BaseUrl );
        OpenAiCompatibleAiProvider cloud = new(
            cloudOptions, cloudClient, credentialResolver );
        Equal( "environment-reference", cloud.Describe( ).CredentialState );
        var cloudModels = await cloud.DiscoverModelsAsync( default );
        Equal( "gpt-safe", cloudModels.Single( ).Id );
        var remote = await ReadProviderTextAsync( cloud, new AiPrompt
        {
            Text = "sentinel-cloud",
            SystemPrompt = "inert text only"
        } );
        Equal( "cloud", remote );
        Equal( "Bearer provider-secret", cloudHandler.Requests[0].Authorization );
        Equal( "Bearer provider-secret", cloudHandler.Requests[1].Authorization );
        True( cloudHandler.Requests[1].Body.Contains( "\"tool_choice\":\"none\"", StringComparison.Ordinal ) );
        True( !cloudHandler.Requests[1].Body.Contains( "provider-secret", StringComparison.Ordinal ) );
    }

    private static async Task AiProviderErrorsAreBoundedAndNormalizedAsync( )
    {
        Equal(
            AiErrorCategories.Unavailable,
            AiErrorClassifier.Classify( new InvalidOperationException(
                "busy credential digest stale redirect byte limit too large" ) ) );

        AiProviderOptions options = new( )
        {
            Id = "cloud",
            DisplayName = "Cloud",
            Kind = AiAdapterKinds.OpenAiChatCompletions,
            BaseUrl = "https://api.example.test/v1",
            Model = "model",
            AllowRemoteEndpoint = true
        };
        RecordingAiHandler redirect = new( _ =>
        {
            HttpResponseMessage response = new( HttpStatusCode.TemporaryRedirect );
            response.Headers.Location = new Uri( "https://redirect.example.test/models" );
            return response;
        } );
        await using ( AiProviderRegistry registry = new(
            new AiOptions { Enabled = true, Providers = [options] },
            new FixedTransportFactory( redirect ) ) )
        {
            var inventory = await registry.DiscoverModelsAsync( "cloud", default );
            Equal( AiErrorCategories.RedirectRejected, inventory.Error!.Category );
            Equal( 1, redirect.Requests.Count );
        }

        RecordingAiHandler rateLimited = new( _ =>
        {
            HttpResponseMessage response = new( HttpStatusCode.TooManyRequests );
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromSeconds( 7 ) );
            return response;
        } );
        await using ( AiProviderRegistry registry = new(
            new AiOptions { Enabled = true, Providers = [options] },
            new FixedTransportFactory( rateLimited ) ) )
        {
            var inventory = await registry.DiscoverModelsAsync( "cloud", default );
            Equal( AiErrorCategories.RateLimited, inventory.Error!.Category );
            Equal( 7, inventory.Error.RetryAfterSeconds );
        }

        var oversized = "{\"data\":[{\"id\":\"" +
                        new string( 'x', AiLimits.MaximumDiscoveryBytes ) + "\"}]}";
        RecordingAiHandler tooLarge = new( _ => Json( HttpStatusCode.OK, oversized ) );
        await using AiProviderRegistry oversizedRegistry = new(
            new AiOptions { Enabled = true, Providers = [options] },
            new FixedTransportFactory( tooLarge ) );
        var oversizedInventory = await oversizedRegistry.DiscoverModelsAsync(
            "cloud", default );
        Equal( AiErrorCategories.ResponseTooLarge, oversizedInventory.Error!.Category );

        await AssertDiscoveryCategoryAsync(
            options, _ => Json( HttpStatusCode.Unauthorized, "secret body" ),
            AiErrorCategories.Authentication );
        await AssertDiscoveryCategoryAsync(
            options, _ => Json( HttpStatusCode.Forbidden, "secret body" ),
            AiErrorCategories.Permission );
        await AssertDiscoveryCategoryAsync(
            options, _ => Json( HttpStatusCode.OK, "{malformed" ),
            AiErrorCategories.MalformedResponse );
        await AssertDiscoveryCategoryAsync(
            options, _ => throw new HttpRequestException( "offline" ),
            AiErrorCategories.Network );
        await AssertDiscoveryCategoryAsync(
            options, _ => throw new TaskCanceledException( "provider timeout" ),
            AiErrorCategories.Timeout );

        RecordingAiHandler noCredentialConnection = new( _ =>
            throw new InvalidOperationException( "transport must not be reached" ) );
        await using AiProviderRegistry missingCredentialRegistry = new(
            new AiOptions { Enabled = true, Providers = [withCredential( )] },
            new FixedTransportFactory( noCredentialConnection ),
            new MissingCredentialResolver( ) );
        var missingCredential = await missingCredentialRegistry.DiscoverModelsAsync(
            "cloud", default );
        Equal( AiErrorCategories.CredentialMissing, missingCredential.Error!.Category );
        Equal( 0, noCredentialConnection.Requests.Count );

        AiProviderOptions withCredential( ) => new( )
        {
            Id = options.Id,
            DisplayName = options.DisplayName,
            Kind = options.Kind,
            BaseUrl = options.BaseUrl,
            Model = options.Model,
            AllowRemoteEndpoint = options.AllowRemoteEndpoint,
            Credential = new AiCredentialOptions
            {
                Kind = AiCredentialKinds.Environment,
                Name = "MISSING_TEST_KEY"
            }
        };
    }

    private static async Task AssertDiscoveryCategoryAsync(
        AiProviderOptions options,
        Func<HttpRequestMessage, HttpResponseMessage> response,
        string expectedCategory )
    {
        RecordingAiHandler handler = new( response );
        await using AiProviderRegistry registry = new(
            new AiOptions { Enabled = true, Providers = [options] },
            new FixedTransportFactory( handler ) );
        var inventory = await registry.DiscoverModelsAsync( "cloud", default );
        Equal( expectedCategory, inventory.Error!.Category );
    }

    private static async Task AiGenerationBindsPreflightStreamAndOutputToClientAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-ai-generation-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            RecordingAiHandler handler = new( _ => Json( HttpStatusCode.OK,
                "{\"model\":\"qwen\",\"message\":{\"content\":\"one \"},\"done\":false}\n" +
                "{\"model\":\"qwen\",\"message\":{\"content\":\"two\"},\"done\":false}\n" +
                "{\"model\":\"qwen\",\"message\":{\"content\":\"\"},\"done\":true," +
                "\"done_reason\":\"stop\",\"prompt_eval_count\":2,\"eval_count\":2}\n" ) );
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
            AiPreflightService preflights = new(
                providers,
                new FilePreviewService(
                    new IndexingOptions { Roots = [] },
                    new PrivacyOptions { NeverIndex = [] } ),
                catalog.AiPreferences,
                ( _, _ ) => null );
            using AiGenerationCoordinator generations = new( preflights );
            const string owner = "test-generation-client";
            var preflight = await preflights.CreateAsync( owner, new AiDraft
            {
                ProviderId = "ollama",
                ModelId = "qwen",
                UserText = "Generate inert text",
                Attachments = []
            }, default );
            List<AiChunk> chunks = [];
            List<AiDone> terminals = [];
            await generations.RunAsync(
                owner, preflight.Id, preflight.PayloadDigest, preflight.Generation,
                chunk =>
                {
                    chunks.Add( chunk );
                    return ValueTask.CompletedTask;
                },
                done =>
                {
                    terminals.Add( done );
                    return ValueTask.CompletedTask;
                }, default );
            Equal( 2, chunks.Count );
            Equal( "one two", string.Concat( chunks.Select( static chunk => chunk.Text ) ) );
            Equal( 1, terminals.Count );
            Equal( AiGenerationOutcomes.Completed, terminals[0].Outcome );
            Equal( 4L, terminals[0].Usage!.TotalTokens );
            Equal(
                Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( handler.Requests.Single( ).Body ) ) )
                    .ToLowerInvariant( ),
                preflight.PayloadDigest );
            True( generations.TryGetOutput(
                owner, preflight.Generation, out var output ) );
            Equal( "one two", output );
            True( !generations.TryGetOutput(
                "other-client", preflight.Generation, out _ ) );
            await AssertAiOutputIdentityAsync(
                generations, owner, preflight.Generation, root );

            await AssertTerminalSinkAttemptedOnceAsync( generations, preflights, owner );
            generations.ClearClient( owner );
            True( !generations.TryGetOutput( owner, preflight.Generation, out _ ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AiGenerationCancellationHasOneTerminalAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-ai-cancel-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        try
        {
            var firstLine = Encoding.UTF8.GetBytes(
                "{\"model\":\"qwen\",\"message\":{\"content\":\"first\"},\"done\":false}\n" );
            RecordingAiHandler handler = new( _ => new HttpResponseMessage( HttpStatusCode.OK )
            {
                Content = new StreamContent( new CancellableAiStream( firstLine ) )
            } );
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
            AiPreflightService preflights = new(
                providers,
                new FilePreviewService(
                    new IndexingOptions { Roots = [] },
                    new PrivacyOptions { NeverIndex = [] } ),
                catalog.AiPreferences,
                ( _, _ ) => null );
            using AiGenerationCoordinator generations = new( preflights );
            const string owner = "test-cancel-client";
            var preflight = await preflights.CreateAsync( owner, new AiDraft
            {
                ProviderId = "ollama",
                ModelId = "qwen",
                UserText = "Cancel after one chunk",
                Attachments = []
            }, default );
            using CancellationTokenSource cancellation = new( TimeSpan.FromSeconds( 2 ) );
            List<AiDone> terminals = [];
            var elapsed = System.Diagnostics.Stopwatch.StartNew( );
            await generations.RunAsync(
                owner, preflight.Id, preflight.PayloadDigest, preflight.Generation,
                _ =>
                {
                    generations.ClearClient( owner );
                    return ValueTask.CompletedTask;
                },
                done =>
                {
                    terminals.Add( done );
                    return ValueTask.CompletedTask;
                }, cancellation.Token );
            elapsed.Stop( );
            Equal( 1, terminals.Count );
            Equal( AiGenerationOutcomes.Cancelled, terminals[0].Outcome );
            Equal( AiErrorCategories.Cancelled, terminals[0].Error!.Category );
            True( elapsed.Elapsed < TimeSpan.FromMilliseconds( 500 ) );
            True( !generations.TryGetOutput( owner, preflight.Generation, out _ ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static HttpClient CreateClient( HttpMessageHandler handler, string baseUrl ) => new( handler )
    {
        BaseAddress = new Uri( baseUrl.TrimEnd( '/' ) + "/" )
    };


    private static async Task<string> ReadProviderTextAsync(
        IAiProvider provider,
        AiPrompt prompt )
    {
        StringBuilder output = new( );
        var request = provider.PrepareRequest( prompt );
        await foreach ( var value in provider.StreamAsync( request, default ) )
        {
            if ( value.Text is not null )
            {
                output.Append( value.Text );
            }
        }
        return output.ToString( );
    }

    private static HttpResponseMessage Json( HttpStatusCode status, string body ) => new( status )
    {
        Content = new StringContent( body, Encoding.UTF8, "application/json" )
    };

    private static HttpResponseMessage EventStream( string body ) => new( HttpStatusCode.OK )
    {
        Content = new StringContent( body, Encoding.UTF8, "text/event-stream" )
    };

    private sealed record RecordedAiRequest( string Method, string Path, string Authorization, string Body );

    private sealed class RecordingAiHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory ) : HttpMessageHandler
    {
        public List<RecordedAiRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken )
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync( cancellationToken );
            Requests.Add( new RecordedAiRequest(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString( ) ?? string.Empty,
                body ) );
            return responseFactory( request );
        }
    }

    private sealed class FixedCredentialResolver( string value ) : IAiCredentialResolver
    {
        public ValueTask<AiCredentialLease?> ResolveAsync(
            AiCredentialOptions reference,
            CancellationToken cancellationToken ) =>
            ValueTask.FromResult<AiCredentialLease?>( new AiCredentialLease(
                Encoding.UTF8.GetBytes( value ) ) );
    }

    private sealed class MissingCredentialResolver : IAiCredentialResolver
    {
        public ValueTask<AiCredentialLease?> ResolveAsync(
            AiCredentialOptions reference,
            CancellationToken cancellationToken ) =>
            ValueTask.FromException<AiCredentialLease?>(
                new AiProviderException(
                    AiErrorCategories.CredentialMissing,
                    safeSummary: "The configured AI credential is unavailable." ) );
    }

    private sealed class CancellableAiStream( byte[] firstRead ) : Stream
    {
        private bool _sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException( );
        public override long Position
        {
            get => throw new NotSupportedException( );
            set => throw new NotSupportedException( );
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default )
        {
            if ( !_sent )
            {
                _sent = true;
                firstRead.AsSpan( ).CopyTo( buffer.Span );
                return ValueTask.FromResult( firstRead.Length );
            }
            return WaitForCancellationAsync( cancellationToken );
        }

        private static async ValueTask<int> WaitForCancellationAsync(
            CancellationToken cancellationToken )
        {
            await Task.Delay( Timeout.InfiniteTimeSpan, cancellationToken );
            return 0;
        }

        public override int Read( byte[] buffer, int offset, int count ) =>
            throw new NotSupportedException( );
        public override void Flush( ) => throw new NotSupportedException( );
        public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( );
        public override void SetLength( long value ) => throw new NotSupportedException( );
        public override void Write( byte[] buffer, int offset, int count ) =>
            throw new NotSupportedException( );
    }

    private sealed class FixedTransportFactory( HttpMessageHandler handler ) : IAiHttpTransportFactory
    {
        public HttpClient Create( AiProviderOptions options ) => CreateClient( handler, options.BaseUrl );
    }
}
