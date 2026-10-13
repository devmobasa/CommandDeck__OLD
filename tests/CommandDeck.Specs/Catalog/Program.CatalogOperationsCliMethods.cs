using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task CatalogMaintenanceCliIsSafeAsync( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            (var config, var database) = await CreateCliCatalogAsync( root, "CLI notes" );
            await using ( SqliteCatalog catalog = new(
                new StorageOptions { DatabasePath = database } ) )
            {
                await catalog.ReplaceAllAsync(
                    SingleCliCatalogItem( ),
                    progress: null,
                    CancellationToken.None );
                await catalog.UsageHistory.RecordInvocationAsync(
                    "file:cli-notes",
                    "open",
                    query: null,
                    CancellationToken.None );
            }

            var inspected = await RunCommandDeckProcessAsync(
                "catalog", "inspect", "--json", "--config", config );
            Equal( 0, inspected.ExitCode );
            var inspection = JsonSerializer.Deserialize(
                inspected.StandardOutput,
                CommandDeckJsonContext.Default.CatalogInspection );
            ArgumentNullException.ThrowIfNull( inspection );
            Equal( 1L, inspection.Stats.ItemCount );
            True( !inspection.RebuildRequired );
            True( !inspected.StandardOutput.Contains( "CLI notes", StringComparison.Ordinal ) );
            True( !File.Exists( database + ".index-events" ),
                "catalog inspection does not start background indexing" );

            var audited = await RunCommandDeckProcessAsync(
                "catalog", "audit", "--json", "--config", config );
            Equal( 0, audited.ExitCode );
            var auditResult = JsonSerializer.Deserialize(
                audited.StandardOutput,
                CommandDeckJsonContext.Default.PrivacyAuditResult );
            True( auditResult?.Succeeded == true );
            True( !audited.StandardOutput.Contains( "CLI notes", StringComparison.Ordinal ) );

            var integrity = await RunCommandDeckProcessAsync(
                "catalog", "integrity", "--json", "--config", config );
            Equal( 0, integrity.ExitCode );
            var integrityResult = JsonSerializer.Deserialize(
                integrity.StandardOutput,
                CommandDeckJsonContext.Default.CatalogMaintenanceResult );
            True( integrityResult?.Succeeded == true );

            var optimized = await RunCommandDeckProcessAsync(
                "catalog", "optimize", "--json", "--config", config );
            Equal( 0, optimized.ExitCode );
            var optimizeResult = JsonSerializer.Deserialize(
                optimized.StandardOutput,
                CommandDeckJsonContext.Default.CatalogMaintenanceResult );
            Equal( "optimize", optimizeResult?.Operation );

            await using ( SqliteConnection connection = new( $"Data Source={database}" ) )
            {
                await connection.OpenAsync( );
                await using var command = connection.CreateCommand( );
                command.CommandText =
                    "DELETE FROM catalog_fts WHERE stable_id = 'file:cli-notes';";
                await command.ExecuteNonQueryAsync( );
            }

            var drifted = await RunCommandDeckProcessAsync(
                "catalog", "integrity", "--json", "--config", config );
            Equal( 2, drifted.ExitCode );
            True( !drifted.StandardOutput.Contains( "CLI notes", StringComparison.Ordinal ) );

            var repaired = await RunCommandDeckProcessAsync(
                "catalog", "repair", "--json", "--config", config );
            Equal( 0, repaired.ExitCode );
            var repairResult = JsonSerializer.Deserialize(
                repaired.StandardOutput,
                CommandDeckJsonContext.Default.CatalogMaintenanceResult );
            Equal( "repair", repairResult?.Operation );
            Equal( 1L, repairResult?.AffectedItemCount );
            True( repairResult?.RebuildRequired == true );
            True( !repaired.StandardOutput.Contains( "CLI notes", StringComparison.Ordinal ) );

            var rejected = await RunCommandDeckProcessAsync(
                "catalog", "purge", "--config", config );
            Equal( 2, rejected.ExitCode );
            True( rejected.StandardError.Contains( "requires --yes", StringComparison.Ordinal ) );
            await AssertCustomizationCountAsync( database, 1 );

            var purged = await RunCommandDeckProcessAsync(
                "catalog", "purge", "--yes", "--json", "--config", config );
            Equal( 0, purged.ExitCode );
            var purgeResult = JsonSerializer.Deserialize(
                purged.StandardOutput,
                CommandDeckJsonContext.Default.CatalogMaintenanceResult );
            Equal( 1L, purgeResult?.AffectedItemCount );
            True( purgeResult?.RebuildRequired == true );
            await AssertCustomizationCountAsync( database, 1 );
            await using ( SqliteCatalog preserved = new(
                new StorageOptions { DatabasePath = database } ) )
            {
                Equal( 1L, (await preserved.GetStatsAsync( CancellationToken.None )).UsageEventCount );
            }

            var afterPurge = await RunCommandDeckProcessAsync(
                "catalog", "inspect", "--json", "--config", config );
            var purgedInspection = JsonSerializer.Deserialize(
                afterPurge.StandardOutput,
                CommandDeckJsonContext.Default.CatalogInspection );
            Equal( 0L, purgedInspection?.Stats.ItemCount );
            True( purgedInspection?.RebuildRequired == true );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async IAsyncEnumerable<IndexedItem> SingleCliCatalogItem( )
    {
        yield return new IndexedItem
        {
            StableId = "file:cli-notes",
            Kind = ResultKinds.File,
            Title = "CLI notes",
            Path = "/private/cli-notes.txt",
            ProviderId = "files",
            SourceId = SourceIds.Files
        };
        await Task.CompletedTask;
    }
}
