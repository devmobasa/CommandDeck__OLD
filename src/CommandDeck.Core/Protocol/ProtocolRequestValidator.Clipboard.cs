using System.Text;

namespace CommandDeck;

public static partial class ProtocolRequestValidator
{
    private static void ValidateClipboardFields( ProtocolRequest request )
    {
        if ( !ProtocolRoutes.IsIn( request.Operation, ProtocolFamily.Clipboard ) )
        {
            if ( request.Clipboard is not null )
            {
                throw new InvalidOperationException( "Clipboard fields require a clipboard operation." );
            }

            return;
        }

        var value = request.Clipboard;
        if ( value is null )
        {
            throw new InvalidOperationException( "Clipboard operation requires clipboard fields." );
        }

        ValidateClipboardBounds( value );
        ValidateClipboardOwner( request.Operation, value );
        ValidateClipboardEntry( request.Operation, value );
        ValidateClipboardOperation( request, value );
    }

    private static void ValidateClipboardBounds( ClipboardRequest value )
    {
        CheckLength( value.Action, "clipboard.action", 32 );
        CheckLength( value.Cursor, "clipboard.cursor", 32 );
        CheckLength( value.KindFilter, "clipboard.kindFilter", 16 );
        CheckLength( value.PreparationId, "clipboard.preparationId", 32 );
        CheckLength( value.ReleaseId, "clipboard.releaseId", 32 );
        CheckLength( value.EntryId, "clipboard.entryId", 64 );
        CheckLength( value.Incarnation, "clipboard.incarnation", 32 );
        CheckLength( value.SessionId, "clipboard.sessionId", 32 );
        CheckLength( value.FrontendInstance, "clipboard.frontendInstance", 32 );
        if ( value.Keywords is not null && (value.Keywords.Count > 16 || value.Keywords.Any( static keyword =>
            keyword is null || keyword.Any( char.IsControl ) || Encoding.UTF8.GetByteCount( keyword ) > 64 )) )
        {
            throw new InvalidOperationException( "Clipboard keywords exceed their limits." );
        }

        if ( value.OpenGeneration is < 0 or > 9007199254740991 || value.QueryGeneration is < 0 or > 9007199254740991 ||
            value.EntryRevision < 0 || value.PolicyRevision < 0 || value.StoreRevision < 0 || value.MatchIndex is < 0 or > 999 )
        {
            throw new InvalidOperationException( "Clipboard revision, generation or match index is invalid." );
        }
    }

    private static void ValidateClipboardOwner( string operation, ClipboardRequest value )
    {
        if ( operation is ProtocolOperations.ClipboardOpen or ProtocolOperations.ClipboardClose or
            ProtocolOperations.LauncherOpenCancel or ProtocolOperations.ClipboardAction or
            ProtocolOperations.ClipboardActionsPrepare or ProtocolOperations.ClipboardReleaseAbort )
        {
            if ( !Guid.TryParseExact( value.FrontendInstance, "N", out _ ) || value.OpenGeneration < 1 )
            {
                throw new InvalidOperationException( "Clipboard operation requires the frontend instance and open generation." );
            }
        }

        if ( operation is ProtocolOperations.ClipboardQuery or ProtocolOperations.ClipboardPreview or ProtocolOperations.ClipboardActionsPrepare ||
            operation == ProtocolOperations.ClipboardAction && value.Action != ClipboardActionIds.Clear )
        {
            if ( !Guid.TryParseExact( value.SessionId, "N", out _ ) )
            {
                throw new InvalidOperationException( "Clipboard operation requires a session ID." );
            }
        }
    }

    private static void ValidateClipboardEntry( string operation, ClipboardRequest value )
    {
        if ( operation is ProtocolOperations.ClipboardPreview or ProtocolOperations.ClipboardActionsPrepare ||
            operation == ProtocolOperations.ClipboardAction && value.Action != ClipboardActionIds.Clear )
        {
            if ( value.EntryId?.Length is not (32 or 64) || value.EntryId.Any( static character => !char.IsAsciiHexDigit( character ) ) ||
                !Guid.TryParseExact( value.Incarnation, "N", out _ ) || value.EntryRevision < 1 )
            {
                throw new InvalidOperationException( "Clipboard entry identity and revision are required." );
            }
        }
    }

    private static void ValidateClipboardOperation( ProtocolRequest request, ClipboardRequest value )
    {
        if ( request.Operation == ProtocolOperations.ClipboardQuery &&
            (request.Limit > 100 || value.QueryGeneration < 1 || value.KindFilter is not (null or ClipboardPresentationKinds.Text or ClipboardPresentationKinds.Links or ClipboardPresentationKinds.Images or ClipboardPresentationKinds.Files) || request.Expression.IncludeFilterIds.Count != 0 || request.Expression.ExcludeFilterIds.Count != 0 ||
            request.Expression.Clauses.Any( static clause => clause.Field is not null and not QueryFieldIds.Title and not QueryFieldIds.Content and not QueryFieldIds.Metadata )) )
        {
            throw new InvalidOperationException( "Clipboard query requires a generation and supported text fields." );
        }

        if ( request.Operation == ProtocolOperations.ClipboardAction && value.Action is not (ClipboardActionIds.Copy or ClipboardActionIds.Pin or
            ClipboardActionIds.Unpin or ClipboardActionIds.Delete or ClipboardActionIds.Keywords or ClipboardActionIds.Clear or ClipboardActionIds.SequenceStart) )
        {
            throw new InvalidOperationException( "Clipboard action is unavailable." );
        }

        if ( request.Operation == ProtocolOperations.ClipboardActionsPrepare && value.Action != ClipboardActionIds.Open )
        {
            throw new InvalidOperationException( "Clipboard action preparation supports explicit Open only." );
        }

        if ( request.Operation == ProtocolOperations.ClipboardReleaseAbort && value.ReleaseId is not null && !Guid.TryParseExact( value.ReleaseId, "N", out _ ) )
        {
            throw new InvalidOperationException( "Clipboard release identity is invalid." );
        }

        if ( request.Operation == ProtocolOperations.ClipboardSettingsSet &&
            (value.PolicyRevision < 1 || !Enum.IsDefined( value.Consent ) || value.Consent == ClipboardConsent.None && value.Paused) )
        {
            throw new InvalidOperationException( "Clipboard capture policy is invalid." );
        }
    }
}
