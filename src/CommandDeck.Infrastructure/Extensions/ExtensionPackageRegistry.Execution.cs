using System.Collections.Concurrent;

namespace CommandDeck;

public sealed partial class ExtensionPackageRegistry
{
    // Mutations acquire the global registry gate, then execution gates in ordinal
    // key order. Executions take only their own gate and never acquire the global gate.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _executionGates = new( StringComparer.Ordinal );

    /// <summary>Orders maintenance with registry changes and effects, including disabled installations.</summary>
    public async ValueTask WithMaintenanceAsync( string key, long expectedRevision,
        Func<ExtensionInstallation, ValueTask> operation, CancellationToken cancellationToken )
    {
        await _gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            EnsureRevision( expectedRevision );
            var canonicalKey = Find( key ).Key;
            using var execution = await LockExecutionsAsync( canonicalKey, cancellationToken ).ConfigureAwait( false );
            await operation( Find( canonicalKey ) ).ConfigureAwait( false );
        }
        finally
        {
            _gate.Release( );
        }
    }

    private async ValueTask<IDisposable> LockExecutionsAsync( string? key, CancellationToken cancellationToken )
    {
        var keys = key is null ? Document.Installations.Select( installation => installation.Key ).Order( StringComparer.Ordinal ).ToArray( ) : [key];
        List<SemaphoreSlim> acquired = [];
        try
        {
            foreach ( var name in keys )
            {
                var gate = _executionGates.GetOrAdd( name, _ => new( 1, 1 ) );
                await gate.WaitAsync( cancellationToken ).ConfigureAwait( false );
                acquired.Add( gate );
            }
            return new ExecutionLease( acquired );
        }
        catch
        {
            new ExecutionLease( acquired ).Dispose( );
            throw;
        }
    }

    private sealed class ExecutionLease( List<SemaphoreSlim> gates ) : IDisposable
    {
        public void Dispose( )
        {
            for ( var index = gates.Count - 1; index >= 0; index-- )
            {
                gates[index].Release( );
            }
        }
    }
}
