namespace CommandDeck;

public sealed class QueryHistoryDeleteInput
{
    public long Id { get; }
    public QueryHistoryDeleteInput( long id, bool confirmed )
    {
        if ( !confirmed )
        {
            throw new InvalidOperationException( "history.delete requires confirmed: true." );
        }
        if ( id <= 0 )
        {
            throw new InvalidOperationException( "history.delete requires a positive historyId." );
        }

        Id = id;
    }
}

public sealed class QueryHistoryResetInput
{
    public QueryHistoryResetInput( bool confirmed )
    {
        if ( !confirmed )
        {
            throw new InvalidOperationException( "history.reset requires confirmed: true." );
        }
    }
}

// This workflow knows only history persistence. Confirmation is decoded before
// mutation; transport framing and request ownership remain with the socket/CLI.
public sealed class QueryHistoryWorkflow( IQueryHistoryStore store )
{
    public ValueTask<QueryHistorySnapshot> GetAsync( int limit, CancellationToken cancellationToken ) =>
        store.GetQueryHistoryAsync( Math.Clamp( limit, 1, 256 ), cancellationToken );

    public ValueTask<bool> DeleteAsync( QueryHistoryDeleteInput input, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( input );
        return store.DeleteQueryHistoryAsync( input.Id, cancellationToken );
    }

    public ValueTask ResetAsync( QueryHistoryResetInput input, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( input );
        return store.ResetQueryHistoryAsync( cancellationToken );
    }

    public async ValueTask<QueryHistorySnapshot> DeleteAndReadAsync( QueryHistoryDeleteInput input, int limit, CancellationToken cancellationToken )
    {
        if ( !await DeleteAsync( input, cancellationToken ).ConfigureAwait( false ) )
        {
            throw new InvalidOperationException( $"Query history entry {input.Id} was not found." );
        }

        return await GetAsync( limit, cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<QueryHistorySnapshot> ResetAndReadAsync( QueryHistoryResetInput input, int limit, CancellationToken cancellationToken )
    {
        await ResetAsync( input, cancellationToken ).ConfigureAwait( false );
        return await GetAsync( limit, cancellationToken ).ConfigureAwait( false );
    }
}
