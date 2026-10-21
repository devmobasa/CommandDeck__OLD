using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RuntimeResourcesUnwindAfterFailureAsync( )
    {
        List<int> order = [];
        RuntimeResources resources = new( );
        resources.Add( ( ) => { order.Add( 1 ); return ValueTask.CompletedTask; } );
        resources.Add( ( ) => { order.Add( 2 ); throw new IOException( "synthetic cleanup failure" ); } );
        resources.Add( async ( ) => { await Task.Yield( ); order.Add( 3 ); } );
        await ThrowsAsync<AggregateException>( async ( ) => await resources.DisposeAsync( ) );
        Equal( "3,2,1", string.Join( ',', order ) );
        await resources.DisposeAsync( );
        Equal( 3, order.Count );

        var directory = SpecTemporaryFiles.PathFor( $"commanddeck-startup-rollback-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( directory );
        ScriptedAgentBackend backend = new( );
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( directory, "catalog.db" ) },
                Indexing = new IndexingOptions { Roots = [], IndexDesktopApplications = false, IndexOmarchyCommands = false },
                Agent = new AgentOptions { RuntimeDirectory = Path.Combine( directory, "runtime" ), StateDirectory = Path.Combine( directory, "state" ) }
            };
            var config = Path.Combine( directory, "config.json" );
            await File.WriteAllTextAsync( config, JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );
            RuntimeConstructionOptions failing = new( )
            {
                AgentBackendFactory = ( _, _ ) => backend,
                CliAgentBackendFactory = ( _, _ ) => throw new InvalidOperationException( "injected after indexer and backend construction" )
            };
            await ThrowsAsync<InvalidOperationException>( async ( ) => await CommandDeckRuntime.CreateAsync( config, failing, default ) );
            Equal( 1, backend.DisposeCount );

            RuntimeConstructionOptions recoveredOptions = new( )
            {
                AgentBackendFactory = ( _, _ ) => backend,
                CliAgentBackendFactory = ( _, _ ) => null
            };
            await using ( var recovered = await CommandDeckRuntime.CreateAsync( config, recoveredOptions, default ) )
            {
                await recovered.Indexer.ReindexAsync( null, default );
            }
            Equal( 2, backend.DisposeCount );
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }
}
