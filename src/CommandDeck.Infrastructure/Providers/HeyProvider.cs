using System.Globalization;

namespace CommandDeck;

/// <summary>
/// HEY mail for a <c>hey:</c> query. Runs no process unless the HEY source is
/// enabled and the query asks for HEY. An empty query offers HEY's boxes and
/// pages; <c>box:</c> lists one box; anything else searches. Rows keep HEY's own
/// order; every row is private (see <see cref="PrivateSources"/>), and listings
/// live only in a short in-memory cache so retyping or reopening is instant.
/// </summary>
public sealed partial class HeyProvider : ICommandProvider
{
    public static readonly TimeSpan DefaultTypingPause = TimeSpan.FromMilliseconds( 250 );
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds( 60 );
    // A box changes as mail arrives, so its listing is trusted for less time.
    public static readonly TimeSpan BoxCacheLifetime = TimeSpan.FromSeconds( 20 );
    public const int CacheEntries = 32;
    // One character matches nearly every thread and still costs a HEY request.
    public const int MinimumSearchLength = 2;

    private readonly HeyClient _client;
    private readonly TimeProvider _time;
    private readonly TimeSpan _typingPause;

    public HeyProvider( HeyClient client, TimeProvider? timeProvider = null, TimeSpan? typingPause = null )
    {
        ArgumentNullException.ThrowIfNull( client );

        _client = client;
        _time = timeProvider ?? TimeProvider.System;
        _typingPause = typingPause ?? DefaultTypingPause;
    }

    public string Id => ProviderIds.Hey;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( query );
        ArgumentNullException.ThrowIfNull( request );
        cancellationToken.ThrowIfCancellationRequested( );

        if ( request.EnabledSearchSources?.Contains( SourceIds.Hey, StringComparer.Ordinal ) != true )
        {
            return [];
        }

        if ( !HeySearch.IsRequested( query ) )
        {
            return HeySearch.OffersFallback( query ) && _client.IsInstalled ? [FallbackRow( query, request )] : [];
        }

        if ( !_client.IsInstalled )
        {
            return [InstallRow( )];
        }

        var completion = CompletionRows( request );
        if ( completion is not null )
        {
            return completion;
        }

        var parsed = HeyQuery.Parse( query );
        if ( parsed.Problem is not null )
        {
            return [StateRow( "refinement", "HEY can't run that search", parsed.Problem )];
        }

        if ( parsed.Box is not null )
        {
            var box = parsed.Box;
            var boxListing = await CachedAsync( "box\u001f" + box.CliName, BoxCacheLifetime, pause: false,
                token => _client.ListBoxAsync( box.CliName, HeyClient.MaximumThreads, token ), cancellationToken ).ConfigureAwait( false );
            return ProjectBox( boxListing, box, parsed.Words, request );
        }

        if ( parsed.Search.IsEmpty )
        {
            return HomeRows( );
        }

        var search = parsed.Search;
        if ( IsTooShort( search ) )
        {
            return [StateRow( "too-short", "Keep typing to search HEY", string.Create( CultureInfo.InvariantCulture,
                $"Type at least {MinimumSearchLength} characters, or refine with in:, has:, or date:." ) )];
        }

        var listing = await CachedAsync( string.Join( '\u001f', HeyClient.SearchArguments( search ) ?? [] ), CacheLifetime,
            pause: true, token => _client.SearchAsync( search, token ), cancellationToken ).ConfigureAwait( false );
        return Project( listing, request );
    }

    // An in:, has:, or date: value searches on its own; every typed value needs
    // two characters.
    private static bool IsTooShort( HeySearchQuery search ) =>
        search.Box is null && search.Date is null && search.Attachment is null &&
        new[] { search.Text, search.Exact, search.None, search.From, search.To, search.Subject, search.Label }
            .All( static value => (value?.Length ?? 0) < MinimumSearchLength );
}
