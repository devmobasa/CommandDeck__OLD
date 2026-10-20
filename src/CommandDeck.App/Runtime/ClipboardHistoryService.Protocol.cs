namespace CommandDeck;

public sealed partial class ClipboardHistoryService
{
    public ClipboardResponse Handle( string ownerId, ProtocolRequest request )
    {
        ProtocolRequestValidator.Validate( request );
        var value = request.Clipboard;
        if ( value is null )
        {
            throw new InvalidOperationException( "Clipboard request is missing." );
        }

        lock ( _gate )
        {
            var owner = GetOwner( ownerId );
            switch ( request.Operation )
            {
                case ProtocolOperations.ClipboardOpen:
                    return new( )
                    {
                        SessionId = Open( ownerId, value.FrontendInstance!, value.OpenGeneration ),
                        Capabilities = ClipboardOperationCapabilities.Current
                    };
                case ProtocolOperations.ClipboardClose:
                    if ( value.SessionId is not null )
                    {
                        Close( ownerId, value.SessionId );
                        return new( );
                    }

                    CloseGeneration( ownerId, value.FrontendInstance!, value.OpenGeneration );
                    return new( );
                case ProtocolOperations.LauncherOpenCancel:
                    CloseGeneration( ownerId, value.FrontendInstance!, value.OpenGeneration );
                    return new( );
                case ProtocolOperations.ClipboardQuery:
                    return new( )
                    {
                        Page = Query( ownerId, value.SessionId!, request.Expression, value.PinnedOnly, value.QueryGeneration, value.Cursor, request.Limit, value.KindFilter )
                    };
                case ProtocolOperations.ClipboardPreview:
                    return new( )
                    {
                        Preview = Preview( ownerId, value.SessionId!, value.EntryId!, value.Incarnation!, value.EntryRevision, value.QueryGeneration, value.MatchIndex )
                    };
                case ProtocolOperations.ClipboardAction:
                    ValidateActionGeneration( owner, request, value );

                    if ( value.Action == ClipboardActionIds.Clear )
                    {
                        Clear( ownerId, value.StoreRevision );
                    }
                    else if ( value.Action == ClipboardActionIds.SequenceStart )
                    {
                        return StartSequence( ownerId, value );
                    }
                    else
                    {
                        Mutate( ownerId, value.SessionId!, value.EntryId!, value.Incarnation!, value.EntryRevision, value.Action!, value.Keywords );
                    }

                    return Settings( );
                case ProtocolOperations.ClipboardSettingsGet:
                    return Settings( );
                case ProtocolOperations.ClipboardSettingsSet:
                    return SetSettings( owner, request, value );
                case ProtocolOperations.ClipboardMigrationAcknowledge:
                    store.AcknowledgeMigration( value.PolicyRevision, value.StoreRevision, request.Confirmed );
                    return Settings( );
                case ProtocolOperations.ClipboardReleaseAbort:
                    return new( )
                    {
                        Abort = AbortRelease( ownerId, value )
                    };
                case ProtocolOperations.ClipboardActionsPrepare:
                    // Preparation runs handler discovery outside the gate; HandleAsync routes it before reaching here.
                    throw new InvalidOperationException( "Clipboard action preparation requires the asynchronous dispatch path." );
                default:
                    throw new InvalidOperationException( "Clipboard operation is unavailable." );
            }
        }
    }

    // Callers hold _gate. The entry must belong to this browser snapshot at its current revision.
    private ClipboardResponse StartSequence( string ownerId, ClipboardRequest value )
    {
        var session = GetSession( ownerId, value.SessionId! );
        if ( session.QueryGeneration != value.QueryGeneration )
        {
            throw new InvalidOperationException( "Clipboard sequence start belongs to a previous query." );
        }

        Resolve( session, value.EntryId!, value.Incarnation!, value.EntryRevision );
        var count = store.StartSequenceAt( value.EntryId!, value.Incarnation );
        return new( )
        {
            Action = ActionExecutionResult.Success( ClipboardSequenceCommands.Started( count ) )
        };
    }

    private static void ValidateActionGeneration( Owner owner, ProtocolRequest request, ClipboardRequest value )
    {
        if ( owner.Instance != value.FrontendInstance || owner.Generation != value.OpenGeneration || owner.Closed )
        {
            throw new InvalidOperationException( "Clipboard action belongs to a closed or stale generation." );
        }

        if ( value.Action == ClipboardActionIds.Clear && !request.Confirmed )
        {
            throw new InvalidOperationException( "Confirm clipboard deletion before continuing." );
        }
    }

    private ClipboardResponse Settings( ClipboardPolicyReview? review = null )
    {
        var policy = store.Policy;
        var migration = store.MigrationReview;
        return new( )
        {
            Policy = policy,
            PolicyReview = review,
            Migration = migration,
            Status = policy.Consent == ClipboardConsent.None ? "Disabled" : policy.Paused ? "Paused" :
                migration.Required ? "MigrationReviewRequired" : captureStatus?.Invoke( ) ?? "Unavailable"
        };
    }
}
