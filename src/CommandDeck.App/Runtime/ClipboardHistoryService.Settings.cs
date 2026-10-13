namespace CommandDeck;

public sealed partial class ClipboardHistoryService
{
    private ClipboardResponse SetSettings( Owner owner, ProtocolRequest request, ClipboardRequest value )
    {
        if ( request.ConfirmationToken is not null )
        {
            var prepared = owner.PolicyChange;
            owner.PolicyChange = null;
            if ( prepared is null || !request.Confirmed || prepared.Token != request.ConfirmationToken ||
                value.Patch is not null || value.PolicyRevision != prepared.Change.Before.Revision ||
                value.StoreRevision != prepared.Change.StoreRevision ||
                _time.GetElapsedTime( prepared.CreatedAt ) >= TimeSpan.FromMinutes( 2 ) )
            {
                throw new InvalidOperationException( "Clipboard settings preview expired or changed; request it again." );
            }

            store.ApplyPolicyChange( prepared.Change, confirmed: true );
            return Settings( );
        }

        if ( value.Patch is null )
        {
            var previous = store.Policy;
            if ( value.Consent > previous.Consent && !request.Confirmed )
            {
                throw new InvalidOperationException( "Confirm local plaintext clipboard storage and imperfect secret detection before enabling capture." );
            }

            owner.PolicyChange = null;
            store.SetCapturePolicy( value.PolicyRevision, value.Consent, value.Paused );
            return Settings( );
        }

        var change = store.PreparePolicyChange( value.PolicyRevision, value.Patch );
        owner.PolicyChange = null;
        if ( !change.RequiresConfirmation )
        {
            store.ApplyPolicyChange( change, confirmed: false );
            return Settings( );
        }

        var token = Guid.NewGuid( ).ToString( "N" );
        owner.PolicyChange = new( token, _time.GetTimestamp( ), change );
        return Settings( new( token, change.Before.Revision, change.StoreRevision, change.Proposed,
            change.DeleteIds.Count, change.PinnedCount, change.RetainedBytes, true ) );
    }

    private sealed record PreparedPolicy( string Token, long CreatedAt, ClipboardPolicyChange Change );
}
