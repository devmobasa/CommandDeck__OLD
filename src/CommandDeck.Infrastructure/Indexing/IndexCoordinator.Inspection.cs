namespace CommandDeck;

public sealed partial class IndexCoordinator
{
    public ValueTask<CatalogInspection> InspectAsync( CancellationToken cancellationToken ) =>
        InspectAsync( 0, 50, cancellationToken );

    public async ValueTask<CatalogInspection> InspectAsync(
        int workItemOffset,
        int workItemLimit,
        CancellationToken cancellationToken )
    {
        var boundedOffset = Math.Max( 0, workItemOffset );
        var boundedLimit = Math.Clamp( workItemLimit, 1, 256 );
        var live = _incremental.GetWorkItemsSnapshot( );
        if ( live.Complete && live.Items.Count == 0 )
        {
            return await RunExclusiveAsync(
                token => _catalogOperations.InspectAsync( boundedOffset, boundedLimit, token ),
                cancellationToken ).ConfigureAwait( false );
        }

        var stored = await RunExclusiveAsync(
            token => _catalogOperations.InspectAsync( 0, 256, token ),
            cancellationToken ).ConfigureAwait( false );
        List<IndexWorkItem> merged = [.. stored.WorkItems];
        foreach ( var work in live.Items )
        {
            var described = _files.DescribeWorkItem( work.Path, work.State, work.Reason );
            var existing = merged.FindIndex( value =>
                string.Equals( value.Id, described.Id, StringComparison.Ordinal ) );
            if ( existing >= 0 )
            {
                merged[existing] = described;
            }
            else
            {
                merged.Add( described );
            }
        }

        merged.Sort( static ( left, right ) =>
        {
            var state = WorkStateOrder( left.State ).CompareTo( WorkStateOrder( right.State ) );
            return state != 0
                ? state
                : StringComparer.OrdinalIgnoreCase.Compare( left.RelativePath, right.RelativePath );
        } );
        var page = merged.Skip( boundedOffset ).Take( boundedLimit ).ToList( );
        var exact = live.Complete && !stored.WorkItemsTruncated;
        var total = exact ? merged.Count : Math.Max( stored.TotalWorkItemCount, merged.Count );

        return new CatalogInspection
        {
            Stats = stored.Stats,
            SourceCounts = stored.SourceCounts,
            KindCounts = stored.KindCounts,
            SourceCountsTruncated = stored.SourceCountsTruncated,
            KindCountsTruncated = stored.KindCountsTruncated,
            RebuildRequired = stored.RebuildRequired,
            FailedItemCount = exact ? merged.LongCount( value => value.State == IndexWorkStates.Failed )
                : Math.Max( stored.FailedItemCount, merged.LongCount( value => value.State == IndexWorkStates.Failed ) ),
            StaleItemCount = exact ? merged.LongCount( value => value.State == IndexWorkStates.Stale )
                : Math.Max( stored.StaleItemCount, merged.LongCount( value => value.State == IndexWorkStates.Stale ) ),
            PendingItemCount = exact ? merged.LongCount( value => value.State == IndexWorkStates.Pending )
                : Math.Max( stored.PendingItemCount, merged.LongCount( value => value.State == IndexWorkStates.Pending ) ),
            DetailedWorkStateAvailable = true,
            WorkItems = page,
            TotalWorkItemCount = total,
            WorkItemCountsExact = exact,
            WorkItemOffset = boundedOffset,
            WorkItemsTruncated = !exact || boundedOffset + page.Count < total,
            ExclusionDiagnostics = stored.ExclusionDiagnostics,
            TotalExclusionDiagnosticCount = stored.TotalExclusionDiagnosticCount,
            ExclusionDiagnosticsTruncated = stored.ExclusionDiagnosticsTruncated
        };
    }

    private static int WorkStateOrder( string state ) => state switch
    {
        IndexWorkStates.Failed => 0,
        IndexWorkStates.Stale => 1,
        _ => 2
    };
}
