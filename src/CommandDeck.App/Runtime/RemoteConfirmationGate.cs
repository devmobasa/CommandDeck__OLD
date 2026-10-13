namespace CommandDeck;

/// <summary>Owns one confirmation and its decision. Cancellation retires it before another can be published.</summary>
internal sealed class RemoteConfirmationGate( ExtensionCommandSession session )
{
    private readonly object _gate = new( );
    private ExtensionRemoteConfirmation? _confirmation;
    private TaskCompletionSource<bool>? _decision;
    public ExtensionRemoteConfirmation? Current
    {
        get
        {
            lock ( _gate )
            { return _confirmation; }
        }
    }

    public async ValueTask<bool> ConfirmAsync( ExtensionRemoteConfirmation confirmation, CancellationToken token )
    {
        session.Authority.RequireUserActivatedInvocation( );
        if ( session.CurrentView is null || session.Command.Mode != CommandDeck.ExtensionProtocol.ExtensionCommandModes.View )
        {
            throw new InvalidOperationException( "Activate a command action before requesting a remote write." );
        }

        var decision = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
        lock ( _gate )
        {
            if ( _decision is not null )
            {
                throw new InvalidOperationException( "A remote confirmation is already pending." );
            }
            _confirmation = confirmation;
            _decision = decision;
        }
        try
        {
            await session.PublishStatusAsync( ExtensionSessionStatuses.Confirmation, null, token ).ConfigureAwait( false );
            return await decision.Task.WaitAsync( token ).ConfigureAwait( false );
        }
        finally
        {
            lock ( _gate )
            {
                _decision = null;
                _confirmation = null;
            }
            if ( !session.Lifetime.IsCancellationRequested )
            {
                await session.PublishStatusAsync( ExtensionSessionStatuses.Working, null, session.Lifetime ).ConfigureAwait( false );
            }
        }
    }

    public void Decide( ExtensionSessionInput input )
    {
        session.Authority.Present( input.Sequence, input.Revision );
        lock ( _gate )
        {
            session.Lifetime.ThrowIfCancellationRequested( );
            if ( _decision is null || input.ConfirmationId != _confirmation?.Id || input.Revision != session.CurrentView?.Revision )
            {
                throw new InvalidOperationException( "The remote confirmation is stale." );
            }
            if ( !_decision.TrySetResult( input.Confirmed ) )
            {
                throw new InvalidOperationException( "This confirmation was already answered." );
            }
        }
    }
}
