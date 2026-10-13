using System.Collections.Concurrent;

namespace CommandDeck;

public sealed class AiProviderRegistry : IAsyncDisposable
{
    private readonly Dictionary<string, IAiProvider> _providers =
        new( StringComparer.OrdinalIgnoreCase );
    private readonly List<HttpClient> _clients = [];
    private readonly SemaphoreSlim _discoveryCapacity;
    private readonly bool _ownsDiscoveryCapacity;
    private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _discoveredModels =
        new( StringComparer.OrdinalIgnoreCase );
    private readonly AiOperationDeadlines _deadlines;
    private readonly object _lifetime = new( );
    private int _leases;
    private bool _retired;
    private bool _disposed;

    public AiProviderRegistry(
        AiOptions options,
        IAiHttpTransportFactory? transportFactory = null,
        IAiCredentialResolver? credentialResolver = null,
        AiOperationDeadlines? deadlines = null,
        SemaphoreSlim? discoveryCapacity = null )
    {
        ConfigurationValidator.Validate( new CommandDeckOptions { Ai = options } );
        Options = options;
        transportFactory ??= new AiHttpTransportFactory( );
        credentialResolver ??= new AiCredentialResolver( );
        _deadlines = deadlines ?? new AiOperationDeadlines( );
        ValidateDeadlines( _deadlines );
        // A shared semaphore lets the live registry and temporary draft-test
        // registries count against the same bounded discovery capacity.
        _ownsDiscoveryCapacity = discoveryCapacity is null;
        _discoveryCapacity = discoveryCapacity ?? new SemaphoreSlim( 2, 2 );
        foreach ( var provider in options.Providers.Where( static item => item.Enabled ) )
        {
            var client = transportFactory.Create( provider );
            _clients.Add( client );

            IAiProvider instance = provider.Kind.ToLowerInvariant( ) switch
            {
                "ollama" => new OllamaAiProvider( provider, client ),
                AiAdapterKinds.OpenAiChatCompletions =>
                    new OpenAiCompatibleAiProvider( provider, client, credentialResolver ),
                _ => new UnsupportedAiProvider( provider )
            };
            _providers[provider.Id] = instance;
        }
    }

    /// <summary>The validated definitions this registry was built from.</summary>
    public AiOptions Options { get; }

    public bool TryGet( string id, out IAiProvider? provider ) =>
        _providers.TryGetValue( id, out provider );

    /// <summary>
    /// Keeps the transports alive while an operation runs. A retired registry
    /// disposes its clients only after the last lease is released, so a live
    /// stream is never cut by a settings change that replaced it.
    /// </summary>
    public IDisposable AcquireLease( )
    {
        lock ( _lifetime )
        {
            ObjectDisposedException.ThrowIf( _disposed, this );
            _leases++;
        }
        return new Lease( this );
    }

    public bool IsRetired
    {
        get
        {
            lock ( _lifetime )
            {
                return _retired;
            }
        }
    }

    /// <summary>
    /// Marks the registry as replaced. Disposal happens now when nothing holds a
    /// lease, otherwise when the final lease is released.
    /// </summary>
    public void Retire( )
    {
        lock ( _lifetime )
        {
            _retired = true;
            if ( _leases > 0 )
            {
                return;
            }
        }
        DisposeTransports( );
    }

    private void ReleaseLease( )
    {
        lock ( _lifetime )
        {
            _leases--;
            if ( _leases > 0 || !_retired )
            {
                return;
            }
        }
        DisposeTransports( );
    }

    private void DisposeTransports( )
    {
        lock ( _lifetime )
        {
            if ( _disposed )
            {
                return;
            }
            _disposed = true;
        }
        foreach ( var client in _clients )
        {
            client.Dispose( );
        }
        if ( _ownsDiscoveryCapacity )
        {
            _discoveryCapacity.Dispose( );
        }
    }

    private sealed class Lease( AiProviderRegistry registry ) : IDisposable
    {
        private AiProviderRegistry? _registry = registry;

        public void Dispose( ) => Interlocked.Exchange( ref _registry, null )?.ReleaseLease( );
    }

    public AiProviderInventory GetInventory( ) => new( )
    {
        Providers = [.. _providers.Values.Select( static provider => provider.Describe( ) )]
    };

    public async ValueTask<AiModelInventory> DiscoverModelsAsync(
        string providerId,
        CancellationToken cancellationToken )
    {
        if ( !TryGet( providerId, out var provider ) || provider is null )
        {
            return new AiModelInventory
            {
                ProviderId = providerId,
                Error = new AiError
                {
                    Category = AiErrorCategories.Unavailable,
                    Summary = "The selected AI provider is unavailable.",
                    ProviderId = providerId
                }
            };
        }
        try
        {
            using var lease = AcquireLease( );
            var descriptor = provider.Describe( );
            var deadline = descriptor.Locality == AiLocality.Local
                ? _deadlines.LocalDiscovery
                : _deadlines.RemoteDiscovery;
            var started = TimeProvider.System.GetTimestamp( );
            using var operation =
                CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
            operation.CancelAfter( deadline );
            await _discoveryCapacity.WaitAsync( operation.Token ).ConfigureAwait( false );
            try
            {
                if ( TimeProvider.System.GetElapsedTime( started ) >= deadline )
                {
                    throw new OperationCanceledException( operation.Token );
                }
                operation.Token.ThrowIfCancellationRequested( );
                var models =
                    await provider.DiscoverModelsAsync( operation.Token ).ConfigureAwait( false );
                _discoveredModels[providerId] = models.Select( static model => model.Id )
                    .ToHashSet( StringComparer.Ordinal );
                return new AiModelInventory
                {
                    ProviderId = providerId,
                    Models = [.. models]
                };
            }
            finally
            {
                _discoveryCapacity.Release( );
            }
        }
        catch ( OperationCanceledException exception ) when ( !cancellationToken.IsCancellationRequested )
        {
            return DiscoveryFailure( providerId, exception );
        }
        catch ( Exception exception ) when ( exception is not OperationCanceledException )
        {
            return DiscoveryFailure( providerId, exception );
        }
    }

    private static void ValidateDeadlines( AiOperationDeadlines deadlines )
    {
        if ( deadlines.LocalDiscovery <= TimeSpan.Zero ||
            deadlines.RemoteDiscovery <= TimeSpan.Zero ||
            deadlines.OverallGeneration <= TimeSpan.Zero )
        {
            throw new ArgumentOutOfRangeException(
                nameof( deadlines ), "AI operation deadlines must be positive." );
        }
    }

    private static AiModelInventory DiscoveryFailure( string providerId, Exception exception ) => new( )
    {
        ProviderId = providerId,
        Error = new AiError
        {
            Category = AiErrorClassifier.Classify( exception ),
            Summary = exception is AiProviderException providerError
                ? providerError.SafeSummary
                : "The AI provider could not be refreshed.",
            ProviderId = providerId,
            RetryAfterSeconds = (exception as AiProviderException)?.RetryAfterSeconds
        }
    };

    public AiSelection ReconcileSelection( AiSelection stored )
    {
        if ( stored.ProviderId is null || stored.ModelId is null )
        {
            return stored;
        }

        if ( !TryGet( stored.ProviderId, out var provider ) || provider is null )
        {
            return new AiSelection
            {
                ProviderId = stored.ProviderId,
                ModelId = stored.ModelId,
                State = AiSelectionStates.Unavailable,
                Explanation = "The selected provider is unavailable; no fallback was chosen."
            };
        }
        if ( _discoveredModels.TryGetValue( stored.ProviderId, out var models ) )
        {
            var available = models.Contains( stored.ModelId );
            return new AiSelection
            {
                ProviderId = stored.ProviderId,
                ModelId = stored.ModelId,
                State = available ? AiSelectionStates.Ready : AiSelectionStates.Unavailable,
                Explanation = available ? null :
                    "The selected model was not returned by the last explicit refresh."
            };
        }
        var configured = string.Equals(
            provider.Describe( ).ConfiguredModel, stored.ModelId, StringComparison.Ordinal );
        return new AiSelection
        {
            ProviderId = stored.ProviderId,
            ModelId = stored.ModelId,
            State = configured ? AiSelectionStates.Ready : AiSelectionStates.Unknown,
            Explanation = configured ? null : "Refresh models to verify this saved selection."
        };
    }

    public ValueTask DisposeAsync( )
    {
        Retire( );
        return ValueTask.CompletedTask;
    }

}

public sealed class UnsupportedAiProvider : IAiProvider
{
    private readonly AiProviderOptions _options;

    public UnsupportedAiProvider( AiProviderOptions options )
    {
        _options = options;
    }

    public string Id => _options.Id;

    public AiProviderDescriptor Describe( ) => ProviderDescriptor.Create(
        _options,
        new AiProviderCapabilities
        {
            AdapterContractVersion = "unsupported"
        } );

    public ValueTask<IReadOnlyList<AiModelDescriptor>> DiscoverModelsAsync(
        CancellationToken cancellationToken ) =>
        ValueTask.FromException<IReadOnlyList<AiModelDescriptor>>(
            new NotSupportedException( "AI provider adapter is unsupported." ) );

    public AiPreparedProviderRequest PrepareRequest( AiPrompt prompt ) =>
        throw new NotSupportedException( "AI provider adapter is unsupported." );

    public async IAsyncEnumerable<AiProviderStreamEvent> StreamAsync(
        AiPreparedProviderRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken )
    {
        await Task.Yield( );
        cancellationToken.ThrowIfCancellationRequested( );
        throw new NotSupportedException( $"AI provider kind '{_options.Kind}' is not supported." );
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}

internal static class ProviderDescriptor
{
    public static AiProviderDescriptor Create(
        AiProviderOptions options,
        AiProviderCapabilities capabilities )
    {
        Uri uri = new( options.BaseUrl, UriKind.Absolute );
        var local = AiEndpointPolicy.IsLiteralLoopback( uri );
        return new AiProviderDescriptor
        {
            Id = options.Id,
            DisplayName = options.DisplayName ?? options.Id,
            AdapterKind = options.Kind,
            Locality = local ? AiLocality.Local : AiLocality.Remote,
            Destination = uri.GetLeftPart( UriPartial.Authority ) + uri.AbsolutePath.TrimEnd( '/' ),
            Enabled = options.Enabled,
            CredentialState = options.Credential.Kind == AiCredentialKinds.None
                ? "not-required"
                : $"{options.Credential.Kind}-reference",
            HealthState = AiHealthStates.Configured,
            ConfiguredModel = options.Model,
            Capabilities = capabilities
        };
    }
}

internal static class AiProviderMessages
{
    public static List<AiChatMessage> Build( AiPrompt prompt )
    {
        List<AiChatMessage> messages = [];
        if ( !string.IsNullOrWhiteSpace( prompt.SystemPrompt ) )
        {
            messages.Add( new AiChatMessage { Role = "system", Content = prompt.SystemPrompt } );
        }
        messages.Add( new AiChatMessage { Role = "user", Content = prompt.Text } );
        return messages;
    }
}
