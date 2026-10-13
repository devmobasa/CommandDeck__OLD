namespace CommandDeck;

public sealed partial class SearchCoordinator : IAsyncDisposable
{
    private readonly object _providerLifetimeGate = new( );
    private readonly HashSet<Task> _activeProviderRuns = [];
    private bool _stopping;
    private Task? _providerDrain;

    public ValueTask DisposeAsync( )
    {
        lock ( _providerLifetimeGate )
        {
            _stopping = true;
            _providerDrain ??= DrainProvidersAsync( [.. _activeProviderRuns] );
            return new ValueTask( _providerDrain );
        }
    }

    private async Task DrainProvidersAsync( Task[] tasks )
    {
        await Task.WhenAll( tasks ).ConfigureAwait( false );
        foreach ( var slots in _providerSlots.Values )
        {
            slots.Dispose( );
        }
    }

    private void TrackProvider( Task task )
    {
        _activeProviderRuns.Add( task );
        _ = task.ContinueWith( completed =>
        {
            lock ( _providerLifetimeGate )
            {
                _activeProviderRuns.Remove( completed );
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default );
    }
}
