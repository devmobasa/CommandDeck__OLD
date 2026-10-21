using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task CatalogOperationsInspectMaintainAndPurgeAsync( )
    {
        var database = SpecTemporaryFiles.PathFor( $"commanddeck-catalog-operations-{Guid.NewGuid( ):N}.db" );
        try
        {
            await using SqliteCatalog catalog = new(
                new StorageOptions { DatabasePath = database } );
            await catalog.InitializeAsync( CancellationToken.None );
            await catalog.ReplaceAllAsync(
                CatalogOperationItems( ),
                progress: null,
                CancellationToken.None );

            var favorite = (await catalog.SearchAsync(
                QueryParser.Parse( "private-notes" ),
                10,
                CancellationToken.None )).Single( );
            await catalog.SetFavoriteAsync( favorite, true, CancellationToken.None );
            await catalog.SetAliasAsync( favorite, "notes", CancellationToken.None );
            await catalog.UsageHistory.RecordInvocationAsync(
                favorite.Id,
                "open",
                query: null,
                CancellationToken.None );

#pragma warning disable CA1859 // Exercise the public capability contract, including required overloads.
            ICatalogOperations operations = catalog;
#pragma warning restore CA1859
            var inspection = await operations.InspectAsync(
                CancellationToken.None );
            Equal( 2L, inspection.Stats.ItemCount );
            Equal( 1L, inspection.SourceCounts.Single( value =>
                value.Id == SourceIds.Files ).Count );
            Equal( 1L, inspection.SourceCounts.Single( value =>
                value.Id == SourceIds.Applications ).Count );
            Equal( 1L, inspection.KindCounts.Single( value =>
                value.Id == ResultKinds.File ).Count );
            True( !inspection.RebuildRequired );
            True( inspection.DetailedWorkStateAvailable );
            Equal( 0L, inspection.FailedItemCount );
            Equal( 0L, inspection.StaleItemCount );
            Equal( 0L, inspection.PendingItemCount );

            var inspectionJson = JsonSerializer.Serialize(
                inspection,
                CommandDeckJsonContext.Default.CatalogInspection );
            True( !inspectionJson.Contains( "private-notes", StringComparison.Ordinal ) );
            True( !inspectionJson.Contains( "/private/", StringComparison.Ordinal ) );

            await catalog.ReplaceGenerationAsync(
                CatalogOperationItems( ),
                static ( ) => [],
                static ( ) =>
                [
                    WorkItem("failed", "failed.txt", "read-failed", 1),
                    WorkItem("stale", "stale.txt", "extractor-version-changed", 2),
                    WorkItem("pending", "pending.txt", "filesystem-event-pending", 3)
                ],
                progress: null,
                CancellationToken.None );
            var workPage = await operations.InspectAsync(
                1,
                1,
                CancellationToken.None );
            Equal( 1L, workPage.FailedItemCount );
            Equal( 1L, workPage.StaleItemCount );
            Equal( 1L, workPage.PendingItemCount );
            Equal( 3L, workPage.TotalWorkItemCount );
            Equal( 1, workPage.WorkItemOffset );
            Equal( "stale", workPage.WorkItems.Single( ).State );
            True( workPage.WorkItemsTruncated );

            var healthy = await operations.CheckIntegrityAsync(
                CancellationToken.None );
            True( healthy.Succeeded );
            Equal( "integrity", healthy.Operation );
            True( !healthy.RebuildRequired );

            await using ( SqliteConnection connection = new( $"Data Source={database}" ) )
            {
                await connection.OpenAsync( );
                await using var command = connection.CreateCommand( );
                command.CommandText =
                    "DELETE FROM catalog_fts WHERE stable_id = 'file:private-notes';";
                await command.ExecuteNonQueryAsync( );
            }

            var drift = await operations.CheckIntegrityAsync(
                CancellationToken.None );
            True( !drift.Succeeded );
            True( drift.RebuildRequired );
            True( drift.Messages.Any( value =>
                value.Contains( "projection", StringComparison.OrdinalIgnoreCase ) ) );
            True( !string.Join( ' ', drift.Messages ).Contains(
                "private-notes",
                StringComparison.Ordinal ) );

            var repaired = await operations.RepairAsync(
                CancellationToken.None );
            True( repaired.Succeeded );
            Equal( "repair", repaired.Operation );
            Equal( 2L, repaired.AffectedItemCount );
            True( repaired.RebuildRequired );
            var repairedIntegrity = await operations.CheckIntegrityAsync(
                CancellationToken.None );
            True( repairedIntegrity.Succeeded );
            Equal( 1, (await catalog.SearchAsync(
                QueryParser.Parse( "private-notes" ),
                10,
                CancellationToken.None )).Count );
            Equal( 0, (await catalog.SearchAsync(
                QueryParser.Parse( "missing-body-token" ),
                10,
                CancellationToken.None )).Count );
            Equal( 1, (await catalog.SearchAsync(
                QueryParser.Parse( "preserved-body-token" ),
                10,
                CancellationToken.None )).Count );

            await catalog.ReplaceAllAsync(
                CatalogOperationItems( ),
                progress: null,
                CancellationToken.None );
            var optimized = await operations.OptimizeAsync(
                CancellationToken.None );
            True( optimized.Succeeded );
            Equal( "optimize", optimized.Operation );
            Equal( 2L, (await catalog.GetStatsAsync( CancellationToken.None )).ItemCount );

            await catalog.ReplaceAllAsync(
                BoundedCatalogOperationItems( ),
                progress: null,
                CancellationToken.None );
            var boundedInspection = await operations.InspectAsync(
                CancellationToken.None );
            Equal( 64, boundedInspection.SourceCounts.Count );
            True( boundedInspection.SourceCountsTruncated );
            True( boundedInspection.SourceCounts.All( value => value.Id.Length <= 128 ) );

            var purged = await operations.PurgeProjectionAsync(
                CancellationToken.None );
            True( purged.Succeeded );
            Equal( "purge", purged.Operation );
            Equal( 70L, purged.AffectedItemCount );
            True( purged.RebuildRequired );
            Equal( 0, (await catalog.SearchAsync(
                QueryParser.Parse( "private-notes" ),
                10,
                CancellationToken.None )).Count );

            var afterPurge = await catalog.GetStatsAsync( CancellationToken.None );
            Equal( 0L, afterPurge.ItemCount );
            Equal( 1L, afterPurge.UsageEventCount );
            var preserved = await catalog.GetSnapshotAsync(
                includeShortcuts: false,
                CancellationToken.None );
            Equal( 1, preserved.Customizations.Count );
            True( preserved.Customizations[0].IsFavorite );
            Equal( "notes", preserved.Customizations[0].Aliases.Single( ) );
        }
        finally
        {
            DeleteDatabase( database );
        }
    }

    private static IndexWorkItem WorkItem(
        string state,
        string relativePath,
        string reason,
        long updated ) => new( )
        {
            Id = $"work:{state}",
            State = state,
            Root = "/safe-root",
            RelativePath = relativePath,
            Reason = reason,
            UpdatedUnixMilliseconds = updated
        };

    private static async IAsyncEnumerable<IndexedItem> CatalogOperationItems( )
    {
        yield return new IndexedItem
        {
            StableId = "file:private-notes",
            Kind = ResultKinds.File,
            Title = "private-notes.txt",
            Path = "/private/excluded-from-diagnostics/private-notes.txt",
            Body = "missing-body-token",
            ProviderId = "files",
            SourceId = SourceIds.Files
        };
        yield return new IndexedItem
        {
            StableId = "app:terminal.desktop",
            Kind = ResultKinds.Application,
            Title = "Terminal",
            Body = "preserved-body-token",
            ProviderId = "applications",
            SourceId = SourceIds.Applications
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IndexedItem> BoundedCatalogOperationItems( )
    {
        for ( var index = 0; index < 70; index++ )
        {
            yield return new IndexedItem
            {
                StableId = $"file:bounded-{index}",
                Kind = ResultKinds.File,
                Title = $"bounded-{index}.txt",
                ProviderId = "spec",
                SourceId = $"source-{index:D2}-{new string( 'x', 160 )}"
            };
        }

        await Task.CompletedTask;
    }
}
