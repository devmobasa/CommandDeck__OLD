namespace CommandDeck;

public sealed record ClipboardPolicyChange(
    ClipboardHistoryPolicy Before, ClipboardHistoryPolicy Proposed, long StoreRevision,
    IReadOnlyList<string> DeleteIds, int PinnedCount, long RetainedBytes, bool RequiresConfirmation );

public sealed partial class ClipboardHistoryStore
{
    private bool _startupClearCompleted;

    public ClipboardPolicyChange PreparePolicyChange( long expectedRevision, ClipboardPolicyPatch patch )
    {
        lock ( _gate )
        {
            var current = ReadPolicy( );
            if ( current.Revision != expectedRevision )
            {
                throw new InvalidOperationException( "Clipboard settings changed; refresh them before saving." );
            }

            var candidate = current with
            {
                Revision = checked(current.Revision + 1),
                Consent = patch.Consent ?? current.Consent,
                Paused = patch.Paused ?? current.Paused,
                MaximumEntries = patch.MaximumEntries ?? current.MaximumEntries,
                RetentionHours = patch.RetentionHours ?? current.RetentionHours,
                QuotaMiB = patch.QuotaMiB ?? current.QuotaMiB,
                ClearOnStartup = patch.ClearOnStartup ?? current.ClearOnStartup
            };
            ValidatePolicyCandidate( candidate );
            var rows = PolicyRows( );
            var pins = rows.Where( static row => row.Pinned ).ToArray( );
            if ( pins.Length > candidate.MaximumEntries / 2 || pins.Sum( static row => row.Bytes ) > ( long ) candidate.QuotaMiB * 1024 * 1024 / 2 )
            {
                throw new InvalidOperationException( "Clipboard limits cannot be lower than the pinned allocation; unpin entries first." );
            }

            var cutoff = _time.GetUtcNow( ).AddHours( -candidate.RetentionHours ).ToUnixTimeSeconds( );
            var kept = rows.Where( row => !row.Pinned && row.Copied >= cutoff )
                .Take( candidate.MaximumEntries - pins.Length ).Select( static row => row.Id ).ToHashSet( StringComparer.Ordinal );
            var deleted = rows.Where( row => !row.Pinned && !kept.Contains( row.Id ) ).Select( static row => row.Id ).ToArray( );
            var retainedBytes = rows.Where( row => row.Pinned || kept.Contains( row.Id ) ).Sum( static row => row.Bytes );
            ValidateQuotaChange( current, candidate, retainedBytes );

            var confirmation = candidate.MaximumEntries < current.MaximumEntries || candidate.RetentionHours < current.RetentionHours ||
                candidate.QuotaMiB < current.QuotaMiB || candidate.ClearOnStartup && !current.ClearOnStartup ||
                candidate.Consent > current.Consent || deleted.Length > 0;
            return new( current, candidate, StoreRevision, deleted, pins.Length, retainedBytes, confirmation );
        }
    }

    public ClipboardHistoryPolicy ApplyPolicyChange( ClipboardPolicyChange change, bool confirmed )
    {
        lock ( _gate )
        {
            if ( change.RequiresConfirmation && !confirmed )
            {
                throw new InvalidOperationException( "Confirm the clipboard settings preview before saving." );
            }

            var policy = Transaction( ( ) =>
            {
                if ( ReadPolicy( ) != change.Before || StoreRevision != change.StoreRevision )
                {
                    throw new InvalidOperationException( "Clipboard settings preview changed; request a new preview." );
                }

                var proposed = change.Proposed;
                ValidatePolicyCandidate( proposed );
                Execute( """
                    UPDATE clipboard_policy SET revision=$revision,consent=$consent,paused=$paused,
                        maximum_entries=$maximum,retention_hours=$age,quota_mib=$quota,clear_on_startup=$clear
                    """, ("$revision", proposed.Revision), ("$consent", ( int ) proposed.Consent), ("$paused", proposed.Paused ? 1 : 0),
                    ("$maximum", proposed.MaximumEntries), ("$age", proposed.RetentionHours), ("$quota", proposed.QuotaMiB),
                    ("$clear", proposed.ClearOnStartup ? 1 : 0) );
                foreach ( var id in change.DeleteIds )
                {
                    Execute( "DELETE FROM entries WHERE id=$id AND pinned=0", ("$id", id) );
                }

                if ( change.DeleteIds.Count > 0 )
                {
                    AdvanceRevision( );
                }

                return ReadPolicy( );
            } );
            AdvanceCaptureEpoch( );
            _payloadLedger!.Cleanup( );
            return policy;
        }
    }

    private static void ValidatePolicyCandidate( ClipboardHistoryPolicy policy )
    {
        if ( !Enum.IsDefined( policy.Consent ) || policy.Consent == ClipboardConsent.None && policy.Paused ||
            policy.MaximumEntries is < 10 or > 1000 || policy.RetentionHours is < 1 or > 8760 || policy.QuotaMiB is < 64 or > 1024 )
        {
            throw new InvalidOperationException( "Clipboard settings are outside supported limits." );
        }
    }

    private void ValidateQuotaChange( ClipboardHistoryPolicy current, ClipboardHistoryPolicy candidate, long retainedBytes )
    {
        var quota = ( long ) candidate.QuotaMiB * 1024 * 1024;
        if ( retainedBytes > quota || candidate.QuotaMiB < current.QuotaMiB && _payloadLedger!.ChargedBytes > quota )
        {
            throw new InvalidOperationException( "Clipboard payload quota is below the retained or pending cleanup allocation." );
        }
    }

    private List<PolicyRow> PolicyRows( )
    {
        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT id,(SELECT coalesce(sum(p.actual),0) FROM clipboard_offers o JOIN clipboard_payloads p ON p.id=o.payload_id WHERE o.entry_id=e.id),copied,pinned FROM entries e ORDER BY copied DESC,rowid DESC";
        using var reader = command.ExecuteReader( );
        List<PolicyRow> rows = [];
        while ( reader.Read( ) )
        {
            rows.Add( new( reader.GetString( 0 ), reader.GetInt64( 2 ), reader.GetBoolean( 3 ), reader.GetInt64( 1 ) ) );
        }

        return rows;
    }

    private void CompleteStartupClear( )
    {
        if ( _startupClearCompleted || MigrationRequired( ) )
        {
            return;
        }

        if ( ReadPolicy( ).ClearOnStartup )
        {
            Clear( );
        }

        _startupClearCompleted = true;
    }

    private sealed record PolicyRow( string Id, long Copied, bool Pinned, long Bytes );
}
