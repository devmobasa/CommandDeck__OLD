namespace CommandDeck;

public sealed partial class CommandDeckRuntime
{
    public ValueTask<PackageSearchResult> SearchPackagesAsync( string query, int limit, CancellationToken cancellationToken ) =>
        Assistant.SearchPackagesAsync( query, limit, cancellationToken );

    public ValueTask<PackageTransactionPreview> PreviewPackagesAsync(
        string operation,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken ) =>
        Assistant.PreviewPackagesAsync( operation, names, cancellationToken );

    public ValueTask<PackageCommitResult> CommitPackagesAsync( string previewId, CancellationToken cancellationToken ) =>
        Assistant.CommitPackagesAsync( previewId, cancellationToken );

    public ValueTask<PackageCommitResult> CancelPackageOperationAsync( string operationId, CancellationToken cancellationToken ) =>
        Assistant.CancelPackageOperationAsync( operationId, cancellationToken );

}
