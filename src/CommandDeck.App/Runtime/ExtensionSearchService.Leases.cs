using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

public sealed partial class ExtensionSearchService
{
    private sealed class SearchState : IDisposable
    {
        private readonly CancellationTokenSource _cancel = new( );
        public SearchState( long generation )
        {
            Generation = generation;
            Token = _cancel.Token;
        }
        public long Generation { get; }
        public CancellationToken Token { get; }
        public DateTimeOffset Expires { get; } = DateTimeOffset.UtcNow.AddMinutes( 2 );
        public Dictionary<string, ResultLease> Leases { get; } = new( StringComparer.Ordinal );
        public void Dispose( )
        {
            _cancel.Cancel( );
            _cancel.Dispose( );
            Leases.Clear( );
        }
    }
    public sealed record SearchActivation( string CommandId, string PackageKey, string Digest, long GrantRevision, Dictionary<string, JsonElement> Inputs );
    private sealed record ResultLease( string ResultId, SearchActivation Activation );
    private static SearchResult Lease( SearchState state, ExtensionInstallation package, ExtensionProtocol.ExtensionSearchProvider provider, ExtensionSearchItem item )
    {
        var providerId = ExtensionStableIds.SearchProviderId( package.Key, provider.Id );
        var id = ExtensionStableIds.SearchItemId( package.Key, provider.Id, item.Id );
        var handle = Guid.NewGuid( ).ToString( "N" );
        var inputs = new Dictionary<string, JsonElement>( StringComparer.Ordinal )
        {
            ["searchItemId"] = JsonSerializer.SerializeToElement( item.Id, CommandDeckJsonContext.Default.String ),
            ["searchContext"] = JsonSerializer.SerializeToElement( item.Context ?? "", CommandDeckJsonContext.Default.String )
        };
        state.Leases.Add( handle, new( id, new( ExtensionStableIds.CommandId( package.Key, provider.CommandId ), package.Key, package.Digest, package.GrantRevision, inputs ) ) );
        return new SearchResult
        {
            Id = id,
            Kind = ResultKinds.ExtensionCommand,
            Title = item.Title,
            Subtitle = item.Subtitle,
            SourceId = ExtensionStableIds.SourceId( package.Key ),
            ProviderId = ProviderIds.Extensions,
            Icon = ExtensionIcons.Addon,
            Score = 0.55,
            Actions = [new( ) { Id = "open", Title = "Open item", Kind = ActionKinds.ExtensionOpen }],
            Metadata = new( StringComparer.Ordinal ) { ["extensionSearchProvider"] = providerId, ["extensionSearchLease"] = handle }
        };
    }
    public void RetainDisplayed( SearchRequest request, IReadOnlyList<SearchResult> results )
    {
        lock ( _gate )
        {
            if ( request.SearchOwner is null || !_owners.TryGetValue( request.SearchOwner, out var state ) || state.Generation != request.SearchGeneration )
            {
                return;
            }
            var displayed = results.Select( result => result.Metadata.GetValueOrDefault( "extensionSearchLease" ) ).OfType<string>( ).ToHashSet( StringComparer.Ordinal );
            foreach ( var hidden in state.Leases.Keys.Except( displayed ).ToArray( ) )
            {
                state.Leases.Remove( hidden );
            }
        }
    }
    public SearchActivation Consume( string owner, string resultId, string? handle )
    {
        lock ( _gate )
        {
            if ( handle is null || !_owners.TryGetValue( owner, out var state ) || state.Expires < DateTimeOffset.UtcNow || state.Token.IsCancellationRequested ||
                !state.Leases.TryGetValue( handle, out var lease ) || lease.ResultId != resultId )
            {
                throw new InvalidOperationException( "This search result expired. Search again." );
            }
            state.Leases.Remove( handle );
            return lease.Activation;
        }
    }
    public void CloseOwner( string owner )
    {
        lock ( _gate )
        {
            if ( _owners.Remove( owner, out var state ) )
            {
                state.Dispose( );
            }
        }
    }
    public async ValueTask RevokeAsync( )
    {
        Task[] workers;
        lock ( _gate )
        {
            foreach ( var state in _owners.Values )
            {
                state.Dispose( );
            }
            _owners.Clear( );
            workers = _workers.ToArray( );
        }
        try
        {
            await Task.WhenAll( workers ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) { }
    }
    public async ValueTask DisposeAsync( )
    {
        _lifetime.Cancel( );
        await RevokeAsync( ).ConfigureAwait( false );
        _lifetime.Dispose( );
        _slots.Dispose( );
    }
}
