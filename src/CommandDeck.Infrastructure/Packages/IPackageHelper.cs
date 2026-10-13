namespace CommandDeck;

/// <summary>
/// The daemon's side of the privileged package helper. <see cref="PackageHelperClient"/>
/// reaches the installed helper over the system bus; specs substitute a fake.
/// </summary>
public interface IPackageHelper : IAsyncDisposable
{
    ValueTask<AgentHelperStatus> GetStatusAsync( CancellationToken cancellationToken );

    ValueTask<string> CommitAsync(
        string operation,
        IReadOnlyList<string> names,
        string databaseIdentity,
        string transactionDigest,
        CancellationToken cancellationToken );

    ValueTask<PackageCommitResult> GetOperationAsync( string operationId, CancellationToken cancellationToken );

    ValueTask<bool> CancelAsync( string operationId, CancellationToken cancellationToken );
}
