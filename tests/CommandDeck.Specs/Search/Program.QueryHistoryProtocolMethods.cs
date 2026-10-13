namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task VerifyQueryHistoryProtocolAsync(
        UnixProtocolClient client,
        CommandDeckRuntime runtime,
        CancellationToken cancellationToken )
    {
        await UsageHistoryOf( runtime ).RecordInvocationAsync(
            "app:history-protocol",
            "launch",
            "protocol query",
            cancellationToken );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 180,
            Operation = ProtocolOperations.QueryHistoryGet,
            Limit = 20
        }, CancellationToken.None );
        var initial = await ReadMessageAsync(
            client,
            180,
            ProtocolMessageTypes.QueryHistoryResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( initial?.QueryHistory );
        Equal( 1L, initial.QueryHistory.TotalEntries );
        Equal( "protocol query", initial.QueryHistory.Entries[0].Query );
        var historyId = initial.QueryHistory.Entries[0].Id;

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 181,
            Operation = ProtocolOperations.QueryHistoryDelete,
            HistoryId = historyId
        }, CancellationToken.None );
        var rejectedDelete = await ReadMessageAsync(
            client,
            181,
            ProtocolMessageTypes.Error,
            cancellationToken );
        True( rejectedDelete?.Error?.Contains( "confirmed", StringComparison.Ordinal ) == true );
        Equal(
            1L,
            (await runtime.QueryHistory.GetAsync( 20, cancellationToken )).TotalEntries );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 182,
            Operation = ProtocolOperations.QueryHistoryDelete,
            HistoryId = historyId,
            Confirmed = true,
            Limit = 20
        }, CancellationToken.None );
        var deleted = await ReadMessageAsync(
            client,
            182,
            ProtocolMessageTypes.QueryHistoryResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( deleted?.QueryHistory );
        Equal( 0L, deleted.QueryHistory.TotalEntries );

        await UsageHistoryOf( runtime ).RecordInvocationAsync(
            "app:history-reset",
            "launch",
            "reset query",
            cancellationToken );
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 183,
            Operation = ProtocolOperations.QueryHistoryReset
        }, CancellationToken.None );
        var rejectedReset = await ReadMessageAsync(
            client,
            183,
            ProtocolMessageTypes.Error,
            cancellationToken );
        True( rejectedReset?.Error?.Contains( "confirmed", StringComparison.Ordinal ) == true );
        Equal(
            1L,
            (await runtime.QueryHistory.GetAsync( 20, cancellationToken )).TotalEntries );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 184,
            Operation = ProtocolOperations.QueryHistoryReset,
            Confirmed = true,
            Limit = 20
        }, CancellationToken.None );
        var reset = await ReadMessageAsync(
            client,
            184,
            ProtocolMessageTypes.QueryHistoryResult,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( reset?.QueryHistory );
        Equal( 0L, reset.QueryHistory.TotalEntries );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 185,
            Operation = ProtocolOperations.QueryHistoryDelete,
            HistoryId = 0,
            Confirmed = true
        }, cancellationToken );
        // ReadMessageAsync collects the error and returns only after this request's done frame.
        var invalid = await ReadMessageAsync( client, 185, ProtocolMessageTypes.Error, cancellationToken );
        True( invalid?.Error?.Contains( "positive historyId", StringComparison.Ordinal ) == true );
    }

    // A runtime built by CommandDeckRuntime.CreateAsync keeps usage and query
    // history in its catalog's store; specs seed it there.
    private static IUsageHistoryStore UsageHistoryOf( CommandDeckRuntime runtime )
    {
        var catalog = runtime.Catalog as SqliteCatalog;
        ArgumentNullException.ThrowIfNull( catalog );

        return catalog.UsageHistory;
    }
}
