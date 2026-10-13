using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

/// <summary>A bounded batch provider. Search children receive one query and one read-only cache value, with no callable host methods.</summary>
public sealed partial class ExtensionSearchService : ICommandProvider, IAsyncDisposable
{
    private readonly ExtensionPackageRegistry _registry;
    private readonly IExtensionSearchWorker _worker;
    internal ExtensionSearchService( ExtensionPackageRegistry registry,
        IExtensionSearchWorker worker )
    {
        _registry = registry;
        _worker = worker;
    }
    private readonly SemaphoreSlim _slots = new( 2, 2 );
    private readonly object _gate = new( );
    private readonly Dictionary<string, SearchState> _owners = new( StringComparer.Ordinal );
    private readonly HashSet<Task> _workers = [];
    private readonly CancellationTokenSource _lifetime = new( );
    private long _generation;
    private int _rotation;
    public string Id => ProviderIds.Extensions;

    public long Begin( string? owner )
    {
        if ( owner is null )
        {
            return 0;
        }
        lock ( _gate )
        {
            if ( _owners.Remove( owner, out var old ) )
            {
                old.Dispose( );
            }
            foreach ( var stale in _owners.Where( pair => pair.Value.Expires < DateTimeOffset.UtcNow ).Select( pair => pair.Key ).ToArray( ) )
            {
                _owners[stale].Dispose( );
                _owners.Remove( stale );
            }
            if ( _owners.Count >= 32 || _lifetime.IsCancellationRequested )
            {
                return 0;
            }
            var state = new SearchState( ++_generation );
            _owners.Add( owner, state );
            return state.Generation;
        }
    }

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync( ParsedQuery query, SearchRequest request, CancellationToken cancellationToken )
    {
        SearchState? state;
        lock ( _gate )
        {
            state = request.SearchOwner is not null ? _owners.GetValueOrDefault( request.SearchOwner ) : null;
        }
        if ( state is null || state.Generation != request.SearchGeneration || query.Text.Length is < 1 or > 2048 || query.IsAiMode || query.IsCalculator || request.BrowseDirectory is not null )
        {
            return [];
        }
        var document = _registry.Document;
        if ( !document.Enabled || _registry.Error is not null )
        {
            return [];
        }
        var candidates = document.Installations.Where( package => package.Enabled && !package.Removed && request.EnabledSearchSources?.Contains( ExtensionStableIds.SourceId( package.Key ), StringComparer.Ordinal ) == true )
                    .SelectMany( package => package.Manifest.SearchProviders.Select( provider => (Package: package, Provider: provider) ) )
                    .Where( candidate => Eligible( query, candidate.Package, candidate.Provider ) ).ToArray( );
        if ( candidates.Length == 0 )
        {
            return [];
        }

        // Rotate before admission so a cold or hostile provider cannot permanently starve later packages.
        var start = ( int ) (( uint ) Interlocked.Increment( ref _rotation ) % ( uint ) candidates.Length);
        var selected = candidates.Skip( start ).Concat( candidates.Take( start ) ).Take( 2 ).ToArray( );
        List<Task<IReadOnlyList<SearchResult>>> work = [];
        foreach ( var candidate in selected )
        {
            var task = RunProviderAsync( state, candidate.Package, candidate.Provider, query, cancellationToken );
            lock ( _gate )
            {
                _workers.Add( task );
            }
            work.Add( task );
        }
        try
        {
            return (await Task.WhenAll( work ).ConfigureAwait( false )).SelectMany( rows => rows ).Take( request.Limit ).ToArray( );
        }
        finally
        {
            lock ( _gate )
            {
                foreach ( var task in work )
                {
                    _workers.Remove( task );
                }
            }
        }
    }

    private static bool Eligible( ParsedQuery query, ExtensionInstallation package, ExtensionProtocol.ExtensionSearchProvider provider )
    {
        var source = ExtensionStableIds.SourceId( package.Key );
        var id = ExtensionStableIds.SearchProviderId( package.Key, provider.Id );
        if ( query.ExcludedFilters.Any( filter => filter.Id == source || filter.Id == id ) )
        {
            return false;
        }
        if ( query.IncludedFilters.Count > 0 )
        {
            return query.IncludedFilters.Any( filter => filter.Id == source || filter.Id == id );
        }
        return !query.HasExplicitFilter && package.GlobalSearchEnabled && package.Origin != ExtensionOrigins.Development;
    }

    private async Task<IReadOnlyList<SearchResult>> RunProviderAsync( SearchState state, ExtensionInstallation package,
        ExtensionProtocol.ExtensionSearchProvider provider, ParsedQuery query, CancellationToken caller )
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource( caller, state.Token, _lifetime.Token );
        operation.CancelAfter( TimeSpan.FromMilliseconds( query.IncludedFilters.Count > 0 ? 1500 : 300 ) );
        var token = operation.Token;
        var admitted = false;
        try
        {
            // A replacement query waits for cancelled children to finish cleanup within its budget.
            // Dropping it immediately here would leave the final typed query permanently empty.
            await _slots.WaitAsync( token ).ConfigureAwait( false );
            admitted = true;
            var cached = _registry.DataStore.CacheValue( package.Key, provider.CacheKey );
            var current = _registry.Find( package.Key );
            if ( !Current( package, current ) || !Eligible( query, current, provider ) )
            {
                throw new InvalidOperationException( "Search authority changed." );
            }
            var input = new ExtensionSearchRequest( query.Text, ExtensionSearchValidation.MaximumItems, cached.Value );
            var response = await _worker.QueryAsync( new( package, provider, input, query.IncludedFilters.Count == 0 ), token ).ConfigureAwait( false );
            ExtensionSearchValidation.Validate( response );
            token.ThrowIfCancellationRequested( );
            return await _registry.WithExecutionAsync<IReadOnlyList<SearchResult>>( package.Key, current =>
            {
                if ( !Current( package, current ) || _registry.DataStore.CacheValue( package.Key, provider.CacheKey ).Revision != cached.Revision || !Eligible( query, current, provider ) )
                {
                    return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
                }
                lock ( _gate )
                {
                    token.ThrowIfCancellationRequested( );
                    return ValueTask.FromResult<IReadOnlyList<SearchResult>>( response.Items.Select( item => Lease( state, package, provider, item ) ).ToArray( ) );
                }
            }, token ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is OperationCanceledException or IOException or InvalidDataException or ExtensionRpcException or InvalidOperationException or JsonException or PlatformNotSupportedException )
        {
            return [];
        } // Never expose query-bearing child diagnostics to logs or another command.
        finally
        {
            if ( admitted )
            {
                _slots.Release( );
            }
        }
    }

    private static bool Current( ExtensionInstallation expected, ExtensionInstallation current ) =>
        expected.Digest == current.Digest && expected.GrantRevision == current.GrantRevision;
}

internal sealed class ExtensionSearchWorker( ExtensionPackageRegistry registry, IExtensionLauncher supervisor ) : IExtensionSearchWorker
{
    public async Task<ExtensionSearchResponse> QueryAsync( ExtensionSearchQuery query, CancellationToken token )
    {
        var (package, provider, input, global) = query;
        var target = ExtensionRuntimeSupport.Require( package.Manifest );

        var process = await supervisor.StartAsync( new( registry.ArtifactDirectory( package ), target.Entrypoint, "search:" + package.Key, ExtensionLaunchProfile.Search ), token ).ConfigureAwait( false );
        ExtensionPeer? peer = null;
        try
        {
            peer = new ExtensionPeer( process.Input, process.Output, true, [], ( _, _ ) => throw new InvalidDataException( "Search cannot call host methods." ) );
            peer.Start( );
            var handshake = await peer.CallAsync( ExtensionMethods.Initialize, JsonSerializer.SerializeToElement(
                new InitializeRequest( new( ), Guid.NewGuid( ).ToString( "N" ), ExtensionSessionProtocol.ExecutionEpoch, provider.Id, global ? ExtensionPurposes.GlobalSearch : ExtensionPurposes.ScopedSearch, [ExtensionFeatures.Search], new( ) ),
                ExtensionJsonContext.Default.InitializeRequest ), token ).ConfigureAwait( false );
            var initialized = handshake.Deserialize( ExtensionJsonContext.Default.InitializeResult );
            if ( initialized?.Protocol is not { Major: 1, Minor: 0 } || initialized.Commands is null ||
                !initialized.Commands.Order( StringComparer.Ordinal ).SequenceEqual( package.Manifest.Commands.Select( command => command.Id ).Order( StringComparer.Ordinal ) ) )
            {
                throw new InvalidDataException( "Search handshake does not match the installed package." );
            }

            peer.Notify( ExtensionMethods.Initialized );
            var result = await peer.CallAsync( ExtensionMethods.SearchQuery, JsonSerializer.SerializeToElement( input, ExtensionSearchJsonContext.Default.ExtensionSearchRequest ), token ).ConfigureAwait( false );
            var response = result.Deserialize( ExtensionSearchJsonContext.Default.ExtensionSearchResponse );
            if ( response is null )
            {
                throw new InvalidDataException( "Missing search results." );
            }

            ExtensionSearchValidation.Validate( response );
            return response;
        }
        finally
        {
            // Stop descendants before draining protocol reads that ignore stream cancellation.
            try
            {
                await process.DisposeAsync( ).ConfigureAwait( false );
            }
            finally
            {
                if ( peer is not null )
                {
                    await peer.DisposeAsync( ).ConfigureAwait( false );
                }
            }
        }
    }
}
