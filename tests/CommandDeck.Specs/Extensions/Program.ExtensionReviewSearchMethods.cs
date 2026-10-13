using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static readonly string[] ExtensionSearchFailureCases = ["rpc", "invalid", "oversized"];

    private static async Task ExtensionSearchIsolatesFailedChildAsync( )
    {
        var manifest = SearchManifest( ) with
        {
            SearchProviders = [new( "healthy", "Healthy", "uuid", "cache", "healthy" ), new( "broken", "Broken", "uuid", "cache", "broken" )]
        };
        await WithSearchRegistry( async ( registry, _, package ) =>
        {
            foreach ( var failure in ExtensionSearchFailureCases )
            {
                HashSet<string> called = [];
                await using ExtensionSearchService service = new( registry, new FakeExtensionSearchWorker( ( _, provider, _, _, _ ) =>
                {
                    called.Add( provider.Id );
                    if ( provider.Id == "healthy" )
                    {
                        return Task.FromResult( new ExtensionSearchResponse( [new( "good", "Healthy result" )] ) );
                    }

                    return failure switch
                    {
                        "rpc" => Task.FromException<ExtensionSearchResponse>( new ExtensionRpcException( -32000, "Child handler failed" ) ),
                        "invalid" => Task.FromException<ExtensionSearchResponse>( new InvalidDataException( "Invalid child payload" ) ),
                        _ => Task.FromResult( new ExtensionSearchResponse( Enumerable.Range( 0, 33 ).Select( index => new ExtensionSearchItem( "bad" + index, "Bad" ) ).ToArray( ) ) )
                    };
                } ) );
                var prefix = registry.Snapshot.FindById( ExtensionStableIds.SourceId( package.Key ) )!.Token;
                var query = QueryParser.Parse( prefix + " meeting", registry: registry.Snapshot );
                var rows = await service.SearchAsync( query, SearchInput( service, package ), CancellationToken.None );
                Equal( 2, called.Count );
                Equal( "Healthy result", rows.Single( ).Title );
            }
        }, manifest );
    }
}
