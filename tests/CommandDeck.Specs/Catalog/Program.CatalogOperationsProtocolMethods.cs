namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task VerifyCatalogOperationsProtocolAsync(
        UnixProtocolClient client,
        ILauncherPreferenceStore preferences,
        CancellationToken cancellationToken )
    {
        await VerifyCatalogInspectionAndMaintenanceProtocolAsync(
            client,
            cancellationToken );
        await VerifyCatalogPurgeProtocolAsync( client, preferences, cancellationToken );
    }

    private static async Task VerifyCatalogInspectionAndMaintenanceProtocolAsync(
        UnixProtocolClient client,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 130,
            Operation = ProtocolOperations.CatalogInspect,
            Offset = 1,
            Limit = 1
        }, CancellationToken.None );
        var inspected = await ReadMessageAsync(
            client,
            130,
            ProtocolMessageTypes.CatalogInspection,
            cancellationToken );
        ArgumentNullException.ThrowIfNull( inspected?.CatalogInspection );
        True( inspected.CatalogInspection.Stats.ItemCount > 0 );
        True( !inspected.CatalogInspection.RebuildRequired );
        Equal( 1, inspected.CatalogInspection.WorkItemOffset );
        Equal( 1L, inspected.CatalogInspection.TotalExclusionDiagnosticCount );
        Equal( IndexExclusionReasons.RootGlob,
            inspected.CatalogInspection.ExclusionDiagnostics.Single( ).Reason );
        Equal( "*.tmp", inspected.CatalogInspection.ExclusionDiagnostics.Single( ).Rule );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 138,
            Operation = ProtocolOperations.PrivacyAudit
        }, CancellationToken.None );
        var privacyAudit = await ReadMessageAsync(
            client,
            138,
            ProtocolMessageTypes.PrivacyAuditResult,
            cancellationToken );
        True( privacyAudit?.PrivacyAudit?.Succeeded == true );
        Equal( 1L, privacyAudit?.PrivacyAudit?.ExclusionDiagnosticCount );
        True( !System.Text.Json.JsonSerializer.Serialize(
                privacyAudit,
                CommandDeckJsonContext.Default.ProtocolMessage )
            .Contains( "PrivateContentMarker", StringComparison.Ordinal ) );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 131,
            Operation = ProtocolOperations.DatabaseIntegrity
        }, CancellationToken.None );
        var integrity = await ReadMessageAsync(
            client,
            131,
            ProtocolMessageTypes.CatalogMaintenanceResult,
            cancellationToken );
        True( integrity?.CatalogMaintenance?.Succeeded == true );
        Equal( "integrity", integrity?.CatalogMaintenance?.Operation );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 132,
            Operation = ProtocolOperations.DatabaseOptimize
        }, CancellationToken.None );
        var optimized = await ReadMessageAsync(
            client,
            132,
            ProtocolMessageTypes.CatalogMaintenanceResult,
            cancellationToken );
        True( optimized?.CatalogMaintenance?.Succeeded == true );
        Equal( "optimize", optimized?.CatalogMaintenance?.Operation );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 137,
            Operation = ProtocolOperations.DatabaseRepair
        }, CancellationToken.None );
        var repaired = await ReadMessageAsync(
            client,
            137,
            ProtocolMessageTypes.CatalogMaintenanceResult,
            cancellationToken );
        True( repaired?.CatalogMaintenance?.Succeeded == true );
        Equal( "repair", repaired?.CatalogMaintenance?.Operation );
    }

    private static async Task VerifyCatalogPurgeProtocolAsync(
        UnixProtocolClient client,
        ILauncherPreferenceStore preferences,
        CancellationToken cancellationToken )
    {
        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 133,
            Operation = ProtocolOperations.DatabasePurge
        }, CancellationToken.None );
        var rejected = await ReadMessageAsync(
            client,
            133,
            ProtocolMessageTypes.Error,
            cancellationToken );
        True( rejected?.Error?.Contains( "confirmed", StringComparison.Ordinal ) == true );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 134,
            Operation = ProtocolOperations.CatalogInspect
        }, CancellationToken.None );
        var unchanged = await ReadMessageAsync(
            client,
            134,
            ProtocolMessageTypes.CatalogInspection,
            cancellationToken );
        True( unchanged?.CatalogInspection?.Stats.ItemCount > 0 );

        await preferences.SetFavoriteAsync( new SearchResult
        {
            Id = "file:preserved-across-catalog-purge",
            Kind = ResultKinds.File,
            Title = "Preserved favorite",
            ProviderId = "files",
            SourceId = SourceIds.Files,
            CanCustomize = true
        }, true, CancellationToken.None );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 135,
            Operation = ProtocolOperations.DatabasePurge,
            Confirmed = true
        }, CancellationToken.None );
        var purged = await ReadMessageAsync(
            client,
            135,
            ProtocolMessageTypes.CatalogMaintenanceResult,
            cancellationToken );
        True( purged?.CatalogMaintenance?.Succeeded == true );
        True( purged?.CatalogMaintenance?.AffectedItemCount > 0 );
        True( purged?.CatalogMaintenance?.RebuildRequired == true );

        var preserved = await preferences.GetSnapshotAsync(
            includeShortcuts: false,
            CancellationToken.None );
        Equal( 1, preserved.Customizations.Count );
        Equal( "file:preserved-across-catalog-purge", preserved.Customizations[0].StableId );

        await client.SendAsync( new ProtocolRequest
        {
            RequestId = 136,
            Operation = ProtocolOperations.CatalogInspect
        }, CancellationToken.None );
        var afterPurge = await ReadMessageAsync(
            client,
            136,
            ProtocolMessageTypes.CatalogInspection,
            cancellationToken );
        Equal( 0L, afterPurge?.CatalogInspection?.Stats.ItemCount );
        True( afterPurge?.CatalogInspection?.RebuildRequired == true );
    }
}
