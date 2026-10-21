namespace CommandDeck.Specs;

public static partial class Program
{
    private static void AiContractsRejectInvalidAuthority( )
    {
        Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            Version = 1,
            RequestId = 1,
            Operation = ProtocolOperations.AiProvidersGet
        } ) );
        ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            RequestId = 2,
            Operation = ProtocolOperations.AiPreflight,
            AiDraft = new AiDraft
            {
                ProviderId = "ollama",
                ModelId = "qwen3:8b",
                UserText = "Explain this",
                Attachments = []
            }
        } );
        Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            RequestId = 3,
            Operation = ProtocolOperations.AiPreflight,
            AiDraft = new AiDraft
            {
                ProviderId = "ollama",
                ModelId = "qwen3:8b",
                UserText = "Explain this",
                Attachments = [null!]
            }
        } ) );
        Throws<InvalidOperationException>( ( ) => ProtocolRequestValidator.Validate( new ProtocolRequest
        {
            RequestId = 4,
            Operation = ProtocolOperations.AiStart,
            PreflightId = "preflight",
            PayloadDigest = "short",
            SessionId = "session-valid-0000000000000000",
            Generation = 1
        } ) );
        ProtocolMessage message = new( )
        {
            RequestId = 5,
            Type = ProtocolMessageTypes.AiChunk,
            SessionId = "session-valid-0000000000000000",
            AiChunk = new AiChunk
            {
                Generation = 1,
                Sequence = 1,
                Text = "hello",
                CumulativeOutputBytes = 5
            }
        };
        var json = System.Text.Json.JsonSerializer.Serialize(
            message,
            CommandDeckJsonContext.Default.ProtocolMessage );
        True( json.Contains( $"\"version\":{ProtocolLimits.CurrentVersion}", StringComparison.Ordinal ) );
        True( json.Contains( "\"aiChunk\"", StringComparison.Ordinal ) );
        True( json.Contains( "\"sessionId\":\"session-valid-0000000000000000\"", StringComparison.Ordinal ) );
    }

    private static void AiConfigurationIsValidatedBeforeTransport( )
    {
        CommandDeckOptions valid = new( )
        {
            Ai = new AiOptions
            {
                Enabled = true,
                Providers =
                [
                    new AiProviderOptions
                    {
                        Id = "local",
                        DisplayName = "Local",
                        Kind = AiAdapterKinds.Ollama,
                        BaseUrl = "http://127.0.0.1:11434",
                        Model = "qwen3:8b"
                    },
                    new AiProviderOptions
                    {
                        Id = "cloud",
                        DisplayName = "Cloud",
                        Kind = AiAdapterKinds.OpenAiChatCompletions,
                        BaseUrl = "https://api.example.test/v1",
                        Model = "example-model",
                        AllowRemoteEndpoint = true,
                        Credential = new AiCredentialOptions
                        {
                            Kind = AiCredentialKinds.Systemd,
                            Name = "commanddeck-cloud"
                        }
                    }
                ]
            }
        };
        ConfigurationValidator.Validate( valid );

        Throws<InvalidOperationException>( ( ) => ConfigurationValidator.Validate( new CommandDeckOptions
        {
            Ai = new AiOptions
            {
                Providers =
                [
                    new AiProviderOptions
                    {
                        Id = "remote",
                        Kind = AiAdapterKinds.Ollama,
                        BaseUrl = "http://example.test:11434",
                        Model = "model",
                        AllowRemoteEndpoint = true
                    }
                ]
            }
        } ) );
        Throws<InvalidOperationException>( ( ) => ConfigurationValidator.Validate( new CommandDeckOptions
        {
            Ai = new AiOptions
            {
                Enabled = true,
                Providers =
                [
                    new AiProviderOptions
                    {
                        Id = "ambiguous-localhost",
                        Kind = AiAdapterKinds.Ollama,
                        BaseUrl = "http://localhost:11434",
                        Model = "model"
                    }
                ]
            }
        } ) );
        Throws<InvalidOperationException>( ( ) => ConfigurationValidator.Validate( new CommandDeckOptions
        {
            Ai = new AiOptions
            {
                Providers = [valid.Ai.Providers[0], valid.Ai.Providers[0]]
            }
        } ) );
        Throws<InvalidOperationException>( ( ) => ConfigurationValidator.Validate( new CommandDeckOptions
        {
            Ai = new AiOptions
            {
                Enabled = true,
                Providers =
                [
                    new AiProviderOptions
                    {
                        Id = "non-normalized",
                        DisplayName = "Non-normalized",
                        Kind = AiAdapterKinds.OpenAiChatCompletions,
                        BaseUrl = "https://api.example.test/v1/../v2",
                        Model = "model",
                        AllowRemoteEndpoint = true
                    }
                ]
            }
        } ) );
        Throws<InvalidOperationException>( ( ) => ConfigurationValidator.Validate( new CommandDeckOptions
        {
            Ai = new AiOptions
            {
                Enabled = true,
                Providers =
                [
                    new AiProviderOptions
                    {
                        Id = "repeated-slash",
                        DisplayName = "Repeated slash",
                        Kind = AiAdapterKinds.OpenAiChatCompletions,
                        BaseUrl = "https://api.example.test/v1//",
                        Model = "model",
                        AllowRemoteEndpoint = true
                    }
                ]
            }
        } ) );
    }

    private static async Task AiCredentialsResolveOnlyFromBoundedNamedSourcesAsync( )
    {
        var root = SpecTemporaryFiles.PathFor( $"commanddeck-ai-credential-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        var oldDirectory = Environment.GetEnvironmentVariable( "CREDENTIALS_DIRECTORY" );
        var oldEnvironment = Environment.GetEnvironmentVariable( "COMMANDDECK_AI_TEST_KEY" );
        try
        {
            Environment.SetEnvironmentVariable( "COMMANDDECK_AI_TEST_KEY", "environment-secret\n" );
            AiCredentialResolver resolver = new( );
            using ( var lease = (await resolver.ResolveAsync(
                new AiCredentialOptions
                {
                    Kind = AiCredentialKinds.Environment,
                    Name = "COMMANDDECK_AI_TEST_KEY"
                }, default ))! )
            {
                Equal( "environment-secret", lease.ReadUtf8( ) );
            }

            await File.WriteAllTextAsync( Path.Combine( root, "cloud-key" ), "systemd-secret\r\n" );
            Environment.SetEnvironmentVariable( "CREDENTIALS_DIRECTORY", root );
            using var fileLease = (await resolver.ResolveAsync(
                new AiCredentialOptions
                {
                    Kind = AiCredentialKinds.Systemd,
                    Name = "cloud-key"
                }, default ))!;
            Equal( "systemd-secret", fileLease.ReadUtf8( ) );
            await File.WriteAllBytesAsync(
                Path.Combine( root, "invalid-utf8" ), [0xC3, 0x28] );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await resolver.ResolveAsync(
                new AiCredentialOptions
                {
                    Kind = AiCredentialKinds.Systemd,
                    Name = "invalid-utf8"
                }, default ) );
            var linkedDirectory = Path.Combine( Path.GetDirectoryName( root )!,
                $"commanddeck-ai-credential-link-{Guid.NewGuid( ):N}" );
            Directory.CreateSymbolicLink( linkedDirectory, root );
            Environment.SetEnvironmentVariable( "CREDENTIALS_DIRECTORY", linkedDirectory );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await resolver.ResolveAsync(
                new AiCredentialOptions
                {
                    Kind = AiCredentialKinds.Systemd,
                    Name = "cloud-key"
                }, default ) );
            Environment.SetEnvironmentVariable(
                "CREDENTIALS_DIRECTORY", linkedDirectory + Path.DirectorySeparatorChar );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await resolver.ResolveAsync(
                new AiCredentialOptions
                {
                    Kind = AiCredentialKinds.Systemd,
                    Name = "cloud-key"
                }, default ) );
            Directory.Delete( linkedDirectory );
            Environment.SetEnvironmentVariable( "CREDENTIALS_DIRECTORY", root );
            await ThrowsAsync<InvalidOperationException>( async ( ) => await resolver.ResolveAsync(
                new AiCredentialOptions
                {
                    Kind = AiCredentialKinds.Systemd,
                    Name = "../escape"
                }, default ) );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "CREDENTIALS_DIRECTORY", oldDirectory );
            Environment.SetEnvironmentVariable( "COMMANDDECK_AI_TEST_KEY", oldEnvironment );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task AiPreflightAttachesOnlyOwnedResultsAsync( )
    {
        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-ai-attachment-owner-" ).FullName;
        try
        {
            var documents = Path.Combine( root, "documents" );
            Directory.CreateDirectory( documents );
            await File.WriteAllTextAsync( Path.Combine( documents, "owner-note.txt" ), "client a sentinel" );
            var config = Path.Combine( root, "config.jsonc" );
            var escapedDatabase = System.Text.Json.JsonSerializer.Serialize( Path.Combine( root, "catalog.db" ) );
            var escapedDocuments = System.Text.Json.JsonSerializer.Serialize( documents );
            await File.WriteAllTextAsync( config, $$"""
                {
                  "storage": { "databasePath": {{escapedDatabase}} },
                  "search": { "providerTimeoutMilliseconds": 5000 },
                  "indexing": {
                    "roots": [{ "path": {{escapedDocuments}}, "includeContentInSearch": true }],
                    "indexDesktopApplications": false,
                    "indexOmarchyCommands": false,
                    "enableIncrementalIndexing": false,
                    "enableScheduledMaintenance": false
                  },
                  "privacy": { "neverIndex": [] },
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
            await runtime.Indexer.ReindexAsync( progress: null, default );
            var search = await runtime.SearchWithOutcomeAsync(
                new SearchRequest { Query = "file:owner-note", SearchOwner = "client-a" }, default );
            Equal( 0, search.IncompleteProviderCount );
            var files = search.Results.Where( value => value.Kind == ResultKinds.File ).ToArray( );
            Equal( 1, files.Length );
            var file = files[0];

            AiDraft Draft( string kind ) => new( )
            {
                ProviderId = "ollama",
                ModelId = "qwen",
                UserText = "summarize",
                Attachments = [new AiAttachmentDraft { Kind = kind, ResultId = file.Id }]
            };

            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await runtime.CreateAiPreflightAsync( "client-b", Draft( AiAttachmentKinds.File ), default ) );
            await ThrowsAsync<InvalidOperationException>( async ( ) =>
                await runtime.CreateAiPreflightAsync( "client-b", Draft( AiAttachmentKinds.Result ), default ) );

            var owned = await runtime.CreateAiPreflightAsync( "client-a", Draft( AiAttachmentKinds.File ), default );
            Equal( "client a sentinel", owned.Attachments.Single( ).Text );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
