using System.Diagnostics;

namespace CommandDeck;

public sealed partial class UnixProtocolServer
{
    private sealed record InvocationCommand(
        string ResultId,
        string ActionId,
        string InvocationReference,
        bool Confirmed,
        IReadOnlyDictionary<string, string>? InputValues )
    {
        public static InvocationCommand FromRequest( ProtocolRequest request )
        {
            if ( string.IsNullOrWhiteSpace( request.ResultId ) ||
                 string.IsNullOrWhiteSpace( request.ActionId ) ||
                 string.IsNullOrWhiteSpace( request.InvocationReference ) )
            {
                throw new InvalidOperationException(
                    "invoke requires resultId, actionId and invocationReference." );
            }

            return new InvocationCommand(
                request.ResultId,
                request.ActionId,
                request.InvocationReference,
                request.Confirmed,
                request.InputValues is null
                    ? null
                    : new Dictionary<string, string>( request.InputValues, StringComparer.Ordinal ) );
        }
    }

    private static bool HasPreviewTarget( ProtocolRequest request ) =>
        !string.IsNullOrWhiteSpace( request.ResultId ) &&
        !string.IsNullOrWhiteSpace( request.InvocationReference );

    private async ValueTask HandleFilesRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var cancellationToken = context.CancellationToken;

        if ( request.Operation == ProtocolOperations.FilesBatch )
        {
            var result = await _runtime.ExecuteFileSelectionAsync( context.OwnerId, request.ActionId ?? "", cancellationToken ).ConfigureAwait( false );
            await writer.WriteAsync( new ProtocolMessage
            {
                RequestId = context.RequestId,
                Type = ProtocolMessageTypes.ActionResult,
                ActionResult = result
            } ).ConfigureAwait( false );
        }
        else
        {
            List<FileSelectionItem> selection = [];
            if ( request.Operation == ProtocolOperations.FilesSelect )
            {
                if ( string.IsNullOrWhiteSpace( request.ResultId ) || request.Enabled is null )
                {
                    throw new InvalidOperationException( "files.select requires resultId and enabled." );
                }
                selection = _runtime.SelectFile( context.OwnerId, request.ResultId, request.Enabled.Value );
            }
            else if ( request.Operation == ProtocolOperations.FilesClear )
            {
                _runtime.ClearFileSelection( context.OwnerId );
            }
            else
            {
                throw UnknownOperation( request );
            }
            await writer.WriteAsync( new ProtocolMessage
            {
                RequestId = context.RequestId,
                Type = ProtocolMessageTypes.FileSelection,
                FileSelection = selection
            } ).ConfigureAwait( false );
        }
        await writer.DoneAsync( context.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask HandleRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ProtocolRoute route,
        ClientRequestContext context,
        RevisionGate aiBarrier,
        long aiEpoch )
    {
        var cancellationToken = context.CancellationToken;

        try
        {
            if ( route.Admission is ProtocolAdmission.AiOrdered or ProtocolAdmission.AiClearBarrier )
            {
                // ai.clear waits for the AI requests admitted before it; an AI-ordered
                // request admitted before a clear is turned away once the clear ran.
                using ( await aiBarrier.EnterAsync(
                    route.Admission == ProtocolAdmission.AiOrdered ? aiEpoch : null,
                    AiClearedBeforeStart,
                    cancellationToken ).ConfigureAwait( false ) )
                {
                    await DispatchAsync( writer, request, route.Family, context ).ConfigureAwait( false );
                }
                return;
            }

            await DispatchAsync( writer, request, route.Family, context ).ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            await writer.DoneAsync( context.RequestId ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested )
        {
            await writer.WriteAsync( new ProtocolMessage
            {
                RequestId = context.RequestId,
                Type = ProtocolMessageTypes.Error,
                Error = SafeProtocolError( exception )
            } ).ConfigureAwait( false );
            await writer.DoneAsync( context.RequestId ).ConfigureAwait( false );
        }
    }

    private static AiOperationException AiClearedBeforeStart( ) =>
        new( AiErrorCategories.StalePreflight, safeSummary: "The AI operation was cleared before it could start." );

    private ValueTask DispatchAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ProtocolFamily family,
        ClientRequestContext context ) => family switch
        {
            ProtocolFamily.Search => HandleSearchRequestAsync( writer, request, context ),
            ProtocolFamily.Suggest => HandleSuggestionsAsync( writer, request, context ),
            ProtocolFamily.Invoke => HandleInvokeAsync( writer, request, context ),
            ProtocolFamily.Preview => HandlePreviewAsync( writer, request, context ),
            ProtocolFamily.Status => HandleStatusAsync( writer, request, context ),
            ProtocolFamily.Ping => HandlePingAsync( writer, request, context ),
            ProtocolFamily.Reindex => HandleReindexAsync( writer, request, context ),
            ProtocolFamily.IndexControl => HandleIndexControlAsync( writer, request, context ),
            ProtocolFamily.Catalog => HandleCatalogRequestAsync( writer, request, context ),
            ProtocolFamily.Personalization => HandlePersonalizationRequestAsync( writer, request, context ),
            ProtocolFamily.Files => HandleFilesRequestAsync( writer, request, context ),
            ProtocolFamily.Clipboard => HandleClipboardRequestAsync( writer, request, context ),
            ProtocolFamily.ExtensionSession => HandleExtensionSessionAsync( writer, request, context ),
            ProtocolFamily.Ai => HandleAiRequestAsync( writer, request, context ),
            ProtocolFamily.AiManagement => HandleAiManagementRequestAsync( writer, request, context ),
            ProtocolFamily.Agent => HandleAgentRequestAsync( writer, request, context ),
            ProtocolFamily.Package => HandlePackageRequestAsync( writer, request, context ),
            _ => throw UnknownOperation( request )
        };

    private async ValueTask HandleClipboardRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var cancellationToken = context.CancellationToken;

        cancellationToken.ThrowIfCancellationRequested( );
        var response = await _runtime.HandleClipboardAsync( context.OwnerId, request, cancellationToken ).ConfigureAwait( false );
        await writer.WriteAsync( new ProtocolMessage
        {
            RequestId = context.RequestId,
            Type = ProtocolMessageTypes.ClipboardResult,
            Clipboard = response
        } ).ConfigureAwait( false );
        await writer.DoneAsync( context.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask HandleInvokeAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var invocation = InvocationCommand.FromRequest( request );

        var actionResult = await _runtime.InvokeReferenceAsync(
            invocation.ResultId,
            invocation.ActionId,
            invocation.InvocationReference,
            context.OwnerId,
            invocation.Confirmed,
            context.CancellationToken,
            invocation.InputValues ).ConfigureAwait( false );
        await writer.WriteAsync( new ProtocolMessage
        {
            RequestId = context.RequestId,
            Type = ProtocolMessageTypes.ActionResult,
            ActionResult = actionResult
        } ).ConfigureAwait( false );
        await writer.DoneAsync( context.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask HandlePreviewAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        if ( !HasPreviewTarget( request ) )
        {
            throw new InvalidOperationException( "preview requires resultId and invocationReference." );
        }

        var previewOutcome = await _runtime.PreviewAsync(
            request.ResultId!,
            context.CancellationToken,
            request.Query,
            request.Expression,
            request.Offset,
            context.OwnerId,
            request.InvocationReference ).ConfigureAwait( false );
        if ( previewOutcome.Succeeded )
        {
            await writer.WriteAsync( new ProtocolMessage
            {
                RequestId = context.RequestId,
                Type = ProtocolMessageTypes.PreviewResult,
                Preview = previewOutcome.Preview
            } ).ConfigureAwait( false );
        }
        else
        {
            await writer.WriteAsync( new ProtocolMessage
            {
                RequestId = context.RequestId,
                Type = ProtocolMessageTypes.Error,
                Error = previewOutcome.Error ?? "Preview is unavailable."
            } ).ConfigureAwait( false );
        }

        await writer.DoneAsync( context.RequestId ).ConfigureAwait( false );
    }

    private ValueTask HandleStatusAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context ) =>
        WriteStatusAsync( context.RequestId, writer, context.CancellationToken );

    private static async ValueTask HandlePingAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        await writer.WriteAsync( new ProtocolMessage
        {
            RequestId = context.RequestId,
            Type = ProtocolMessageTypes.Pong
        } ).ConfigureAwait( false );
        await writer.DoneAsync( context.RequestId ).ConfigureAwait( false );
    }

    private async ValueTask HandleSearchRequestAsync(
        ClientWriter writer,
        ProtocolRequest request,
        ClientRequestContext context )
    {
        var cancellationToken = context.CancellationToken;
        var started = Stopwatch.GetTimestamp( );
        SearchRequest searchRequest = new( )
        {
            SearchOwner = context.OwnerId,
            Query = request.Query ?? string.Empty,
            Limit = request.Limit,
            ActiveApplication = request.ActiveApplication,
            ActiveWorkspace = request.ActiveWorkspace,
            BrowseDirectory = request.BrowseDirectory,
            HerdrWorkspaceId = request.HerdrWorkspaceId,
            OmarchyMenuId = request.OmarchyMenuId,
            BrowseOffset = request.BrowseOffset,
            CurrentDirectory = request.CurrentDirectory,
            SelectedProject = request.SelectedProject,
            Expression = request.Expression
        };

        try
        {
            var search = await _runtime.SearchWithOutcomeAsync( searchRequest, cancellationToken )
                .ConfigureAwait( false );

            foreach ( var result in search.Results )
            {
                await writer.WriteAsync( new ProtocolMessage
                {
                    RequestId = context.RequestId,
                    Type = ProtocolMessageTypes.Result,
                    Result = result
                } ).ConfigureAwait( false );
            }

            SearchLogging.LogSearchCompletion(
                searchRequest,
                context.RequestId,
                started,
                search.Results.Count,
                SearchLogging.CompletedStatus );
            await writer.DoneAsync( context.RequestId,
                new SearchCompletion( search.IsComplete, search.IncompleteProviderCount )
                {
                    ContainsPrivateQuery = !searchRequest.IsPrivacyClassified || searchRequest.ContainsPrivateQuery
                } )
                .ConfigureAwait( false );
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            SearchLogging.LogSearchCompletion(
                searchRequest,
                context.RequestId,
                started,
                0,
                SearchLogging.CancelledStatus );
            throw;
        }
        catch ( Exception exception )
        {
            SearchLogging.LogSearchCompletion(
                searchRequest,
                context.RequestId,
                started,
                0,
                SearchLogging.FailedStatus,
                exception.GetType( ).Name );
            throw;
        }
    }

    private static string SafeProtocolError( Exception exception )
    {
        if ( exception is IOException or UnauthorizedAccessException or
            FileNotFoundException or DirectoryNotFoundException or ArgumentException )
        {
            return "The request could not be completed safely.";
        }

        var message = exception.Message;
        return message.Length is > 0 and <= 512 &&
            !message.Any( char.IsControl ) &&
            !message.Contains( Path.DirectorySeparatorChar ) &&
            !message.Contains( '/' ) &&
            !message.Contains( '\\' )
                ? message
                : "The request could not be completed safely.";
    }
}
