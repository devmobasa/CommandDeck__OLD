namespace CommandDeck;

public sealed partial class UnixProtocolServer
{
    private async ValueTask WriteStatusAsync(
        long requestId,
        ClientWriter writer,
        CancellationToken cancellationToken )
    {
        var status = await _runtime.GetRuntimeStatusAsync( cancellationToken )
            .ConfigureAwait( false );
        await writer.WriteAsync( new ProtocolMessage
        {
            RequestId = requestId,
            Type = ProtocolMessageTypes.Status,
            RuntimeStatus = status
        } ).ConfigureAwait( false );
        await writer.DoneAsync( requestId ).ConfigureAwait( false );
    }

    private async ValueTask HandleIndexControlAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var cancellationToken = context.CancellationToken;

        if ( request.Operation is not (ProtocolOperations.IndexPause or ProtocolOperations.IndexResume) )
        {
            var roots = await _runtime.Indexer.ManageRootsAsync( request.Operation, request.Path, request.RootId,
                request.IndexRoots, cancellationToken ).ConfigureAwait( false );
            await writer.WriteAsync( new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.IndexRootsResult,
                IndexRoots = roots
            } ).ConfigureAwait( false );
            await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
            return;
        }

        var change = request.Operation switch
        {
            ProtocolOperations.IndexPause => _runtime.Indexer.PauseAsync( request.RootId, cancellationToken ),
            ProtocolOperations.IndexResume => _runtime.Indexer.ResumeAsync( request.RootId, cancellationToken ),
            _ => throw UnknownOperation( request )
        };
        await change.ConfigureAwait( false );

        await WriteStatusAsync( request.RequestId, writer, cancellationToken )
            .ConfigureAwait( false );
    }

    private async ValueTask HandleSuggestionsAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var cancellationToken = context.CancellationToken;

        var suggestions = await _runtime.SuggestAsync(
            new SearchRequest
            {
                Query = request.Query ?? string.Empty,
                Limit = Math.Min( request.Limit, 8 ),
                ActiveApplication = request.ActiveApplication,
                ActiveWorkspace = request.ActiveWorkspace,
                CurrentDirectory = request.CurrentDirectory,
                SelectedProject = request.SelectedProject,
                Expression = request.Expression
            },
            cancellationToken ).ConfigureAwait( false );
        await writer.WriteAsync( new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.SuggestionsResult,
            Suggestions = suggestions
        } ).ConfigureAwait( false );
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask HandleReindexAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var cancellationToken = context.CancellationToken;

        await writer.WriteAsync( new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.Progress,
            Progress = new IndexProgress { Stage = "begin" }
        } ).ConfigureAwait( false );
        var report = await _runtime.Indexer.ReindexAsync(
            new IndexReindexScope
            {
                Kind = request.Scope ?? IndexReindexScopeKinds.Full,
                Path = request.Path,
                RootId = request.RootId
            },
            progress: null,
            cancellationToken ).ConfigureAwait( false );
        await writer.WriteAsync( new ProtocolMessage
        {
            RequestId = request.RequestId,
            Type = ProtocolMessageTypes.Progress,
            Progress = new IndexProgress
            {
                Stage = "complete",
                Discovered = report.Discovered,
                Indexed = report.Indexed,
                Skipped = report.Skipped,
                Failed = report.Failed
            },
            ReindexReport = report
        } ).ConfigureAwait( false );
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask HandleCatalogRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var cancellationToken = context.CancellationToken;

        if ( request.Operation == ProtocolOperations.DatabasePurge && !request.Confirmed )
        {
            throw new InvalidOperationException( "database.purge requires confirmed: true." );
        }

        ProtocolMessage message;
        if ( request.Operation == ProtocolOperations.CatalogInspect )
        {
            message = new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.CatalogInspection,
                CatalogInspection = await _runtime.CatalogOperations.InspectAsync(
                    request.Offset,
                    request.Limit,
                    cancellationToken ).ConfigureAwait( false )
            };
        }
        else if ( request.Operation == ProtocolOperations.PrivacyAudit )
        {
            message = new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.PrivacyAuditResult,
                PrivacyAudit = await _runtime.CatalogOperations.AuditPrivacyAsync(
                    cancellationToken ).ConfigureAwait( false )
            };
        }
        else
        {
            var result = request.Operation switch
            {
                ProtocolOperations.DatabaseIntegrity =>
                    await _runtime.CatalogOperations.CheckIntegrityAsync( cancellationToken )
                        .ConfigureAwait( false ),
                ProtocolOperations.DatabaseOptimize =>
                    await _runtime.CatalogOperations.OptimizeAsync( cancellationToken )
                        .ConfigureAwait( false ),
                ProtocolOperations.DatabaseRepair =>
                    await _runtime.CatalogOperations.RepairAsync( cancellationToken )
                        .ConfigureAwait( false ),
                ProtocolOperations.DatabasePurge =>
                    await _runtime.CatalogOperations.PurgeProjectionAsync( cancellationToken )
                        .ConfigureAwait( false ),
                _ => throw UnknownOperation( request )
            };
            message = new ProtocolMessage
            {
                RequestId = request.RequestId,
                Type = ProtocolMessageTypes.CatalogMaintenanceResult,
                CatalogMaintenance = result
            };
        }

        await writer.WriteAsync( message ).ConfigureAwait( false );
        await writer.DoneAsync( request.RequestId ).ConfigureAwait( false );
    }
}
