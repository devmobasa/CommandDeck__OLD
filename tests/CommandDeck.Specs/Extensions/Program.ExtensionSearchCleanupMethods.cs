namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionSearchStopsProcessBeforeDrainingPeerAsync( ) => await WithSearchRegistry( async ( registry, _, package ) =>
    {
        using UncooperativeExtensionStream input = new( );
        using MemoryStream output = new( );
        var process = new SearchCleanupProcess( input, output );
        ExtensionSearchWorker worker = new( registry, new SearchCleanupLauncher( process ) );
        using CancellationTokenSource cancellation = new( );
        var pending = worker.QueryAsync( new( package, package.Manifest.SearchProviders[0], new( "hang", 1, null ), false ), cancellation.Token );
        await input.Entered.Task.WaitAsync( TimeSpan.FromSeconds( 2 ) );

        try
        {
            cancellation.Cancel( );
            await ThrowsAsync<OperationCanceledException>( async ( ) => await pending.WaitAsync( TimeSpan.FromSeconds( 1 ) ) );
            Equal( 1, process.Disposals );
            True( input.Disposed );
        }
        finally
        {
            input.Release.TrySetResult( );
            try
            {
                await pending;
            }
            catch ( OperationCanceledException ) { }
        }
    } );

    private sealed class SearchCleanupLauncher( IExtensionProcess process ) : IExtensionLauncher
    {
        public ValueTask<IExtensionProcess> StartAsync( ExtensionLaunchRequest request, CancellationToken cancellationToken ) => ValueTask.FromResult( process );
    }

    private sealed class SearchCleanupProcess( UncooperativeExtensionStream input, Stream output ) : IExtensionProcess
    {
        public Stream Input => input;
        public Stream Output => output;
        public int Disposals { get; private set; }
        public string ReadDiagnostics( ) => "";

        public ValueTask DisposeAsync( )
        {
            Disposals++;
            input.Release.TrySetResult( );
            return ValueTask.CompletedTask;
        }
    }
}
