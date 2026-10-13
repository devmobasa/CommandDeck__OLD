namespace CommandDeck;

public sealed partial class ClipboardHistoryService
{
    /// <summary>Prepares an explicit Open capability for one URL entry. Discovery runs outside the gate; nothing launches.</summary>
    private async ValueTask<ClipboardResponse> PrepareOpenAsync( string ownerId, ProtocolRequest request, CancellationToken cancellationToken )
    {
        ProtocolRequestValidator.Validate( request );
        var value = request.Clipboard!;
        string destination;
        Owner owner;
        Session session;
        lock ( _gate )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            owner = GetOwner( ownerId );
            ValidateActionGeneration( owner, request, value );
            RequireEffects( owner );
            session = GetSession( ownerId, value.SessionId! );
            if ( session.QueryGeneration != value.QueryGeneration )
            {
                throw new InvalidOperationException( "Clipboard action preparation belongs to a previous query." );
            }

            var document = Resolve( session, value.EntryId!, value.Incarnation!, value.EntryRevision );
            if ( document.Summary.Presentation.Kind != ClipboardPresentationKinds.Links || document.Metadata.OpenDestination is null )
            {
                throw new InvalidOperationException( "Clipboard entry has no single Open destination." );
            }

            destination = document.Metadata.OpenDestination;
            session.Capability = null;
        }

        if ( !ClipboardHttpUrl.TryParseSingleHttpUrl( destination, out var url, out _ ) || resolveHandlers is null )
        {
            throw new InvalidOperationException( "Clipboard Open is unavailable for this destination." );
        }

        var resolution = await resolveHandlers( "x-scheme-handler/" + url!.Scheme, url.Literal, cancellationToken ).ConfigureAwait( false );
        lock ( _gate )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            var current = GetOwner( ownerId );
            ValidateActionGeneration( current, request, value );
            RequireEffects( current );
            var live = GetSession( ownerId, value.SessionId! );
            if ( live != session || live.QueryGeneration != value.QueryGeneration )
            {
                throw new InvalidOperationException( "Clipboard browser changed during action preparation." );
            }

            Capability capability = new( Guid.NewGuid( ).ToString( "N" ), Guid.NewGuid( ).ToString( "N" ), value.EntryId!, value.Incarnation!,
                value.EntryRevision, current.Generation, url.Literal, resolution.Revision, _time.GetTimestamp( ) );
            live.Capability = capability;
            return new( )
            {
                Actions = new( capability.PreparationId, capability.ReleaseId, resolution.Status, resolution.Handlers, resolution.Total,
                    resolution.MoreAvailable, 30, ClipboardOperationCapabilities.Current.Open,
                    ClipboardOperationCapabilities.Current.EffectsUnavailableReason )
            };
        }
    }

    /// <summary>Idempotent non-effect revocation of a generation's action authority; Copy and browsing remain usable.</summary>
    private ClipboardReleaseAbortResult AbortRelease( string ownerId, ClipboardRequest value )
    {
        var owner = GetOwner( ownerId );
        if ( owner.Instance is not null && owner.Instance != value.FrontendInstance || value.OpenGeneration > owner.Generation )
        {
            throw new InvalidOperationException( "Clipboard release abort names an unknown frontend generation." );
        }

        if ( value.OpenGeneration < owner.Generation || owner.Closed )
        {
            return new( value.FrontendInstance!, value.OpenGeneration, value.ReleaseId, "GenerationClosed" );
        }

        // No dispatch permit can exist in this build, so revocation always precedes any effect.
        owner.EffectsRevoked = true;
        foreach ( var session in _sessions.Values.Where( session => session.OwnerId == ownerId ) )
        {
            session.Capability = null;
        }

        return new( value.FrontendInstance!, value.OpenGeneration, value.ReleaseId, "RevokedBeforeDispatch" );
    }

    private static void RequireEffects( Owner owner )
    {
        if ( owner.EffectsRevoked )
        {
            throw new InvalidOperationException( "GenerationEffectsRevoked: reopen the launcher before preparing Open again." );
        }
    }

    private sealed record Capability( string PreparationId, string ReleaseId, string EntryId, string Incarnation, long Revision,
        long Generation, string Destination, string ResolutionRevision, long CreatedAt );
}
