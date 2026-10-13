namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    // Scheduled pruning rechecks at least this often, bounding lateness after an unpin, a shorter
    // retention setting, a wall-clock change or a suspend.
    private static readonly TimeSpan MaximumPruneInterval = TimeSpan.FromMinutes( 5 );
    private TaskCompletionSource _captureChange = new( TaskCreationOptions.RunContinuationsAsynchronously );

    // Completes at the next write that can change capture currency: consent, pause, migration review or
    // capture epoch (including Clear). Take it before checking currency so a change in between is not missed.
    internal Task NextCaptureChange
    {
        get
        {
            lock ( _gate )
            {
                return _captureChange.Task;
            }
        }
    }

    public ClipboardHistoryPolicy Policy
    {
        get
        {
            lock ( _gate )
            {
                return ReadPolicy( );
            }
        }
    }

    public ClipboardMigrationReview MigrationReview
    {
        get
        {
            lock ( _gate )
            {
                var policy = ReadPolicy( );
                using var command = _connection.CreateCommand( );
                command.CommandText = "SELECT count(*),coalesce(sum(pinned),0),coalesce(sum(pinned=0 AND copied<$cutoff),0) FROM entries";
                command.Parameters.AddWithValue( "$cutoff", _time.GetUtcNow( ).AddHours( -policy.RetentionHours ).ToUnixTimeSeconds( ) );
                using var reader = command.ExecuteReader( );
                _ = reader.Read( );
                var count = reader.GetInt32( 0 );
                var pins = reader.GetInt32( 1 );
                var expired = reader.GetInt32( 2 );
                reader.Close( );

                return new( MigrationRequired( ), Scalar( "SELECT revision FROM clipboard_state" ), count, pins, expired,
                    checked(( int ) Scalar( "SELECT original_capacity FROM clipboard_state" )), policy.MaximumEntries,
                    !_startupClearCompleted && policy.ClearOnStartup );
            }
        }
    }

    public void AcknowledgeMigration( long expectedPolicyRevision, long expectedStoreRevision, bool confirmExpiredDeletion )
    {
        lock ( _gate )
        {
            var acknowledged = false;
            var clearedAtStartup = false;
            Transaction( ( ) =>
            {
                var policy = ReadPolicy( );
                var review = MigrationReview;
                if ( policy.Revision != expectedPolicyRevision || review.StoreRevision != expectedStoreRevision )
                {
                    throw new InvalidOperationException( "Clipboard migration review changed; refresh it before confirming." );
                }

                if ( !review.Required )
                {
                    return 0;
                }

                if ( (review.ExpiredCount > 0 || review.PendingStartupClear && review.EntryCount > 0) && !confirmExpiredDeletion )
                {
                    throw new InvalidOperationException( "Confirm removal of expired clipboard entries before applying retention." );
                }

                Execute( "UPDATE clipboard_state SET migration_review=0,revision=revision+1" );
                acknowledged = true;
                if ( review.PendingStartupClear )
                {
                    Execute( "DELETE FROM entries" );
                    clearedAtStartup = true;
                }
                else
                {
                    PrunePolicy( policy );
                }

                return 0;
            } );
            if ( clearedAtStartup )
            {
                _startupClearCompleted = true;
                AdvanceCaptureEpoch( );
                Interlocked.Increment( ref _clearEpoch );
            }
            else if ( acknowledged )
            {
                // Leaving migration review can make capture current again.
                SignalCaptureChange( );
            }

            CompleteStartupClear( );
            _payloadLedger!.Cleanup( );
        }
    }

    public ClipboardHistoryPolicy SetCapturePolicy( long expectedRevision, ClipboardConsent consent, bool paused )
    {
        if ( !Enum.IsDefined( consent ) || consent == ClipboardConsent.None && paused )
        {
            throw new ArgumentException( "Invalid clipboard capture policy." );
        }

        lock ( _gate )
        {
            var updated = Transaction( ( ) =>
            {
                var policy = ReadPolicy( );
                if ( policy.Revision != expectedRevision )
                {
                    throw new InvalidOperationException( "Clipboard settings changed; refresh them before saving." );
                }

                Execute( "UPDATE clipboard_policy SET consent=$consent,paused=$paused,revision=revision+1",
                    ("$consent", ( int ) consent), ("$paused", paused ? 1 : 0) );
                return ReadPolicy( );
            } );
            AdvanceCaptureEpoch( );
            return updated;
        }
    }

    private ClipboardHistoryPolicy ReadPolicy( )
    {
        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT revision,consent,paused,maximum_entries,retention_hours,quota_mib,clear_on_startup FROM clipboard_policy";
        using var reader = command.ExecuteReader( );
        if ( !reader.Read( ) )
        {
            throw new IOException( "Clipboard policy is missing." );
        }

        if ( reader.GetInt64( 2 ) is not (0 or 1) || reader.GetInt64( 6 ) is not (0 or 1) )
        {
            throw new IOException( "Clipboard policy contains an invalid Boolean value." );
        }

        ClipboardHistoryPolicy policy = new( reader.GetInt64( 0 ), ( ClipboardConsent ) reader.GetInt32( 1 ),
            reader.GetBoolean( 2 ), reader.GetInt32( 3 ), reader.GetInt32( 4 ), reader.GetInt32( 5 ), reader.GetBoolean( 6 ) );
        if ( reader.Read( ) || policy.Revision < 1 || !Enum.IsDefined( policy.Consent ) ||
            policy.MaximumEntries is < 10 or > 1000 || policy.RetentionHours is < 1 or > 8760 ||
            policy.QuotaMiB is < 64 or > 1024 || policy.Consent == ClipboardConsent.None && policy.Paused )
        {
            throw new IOException( "Clipboard policy is invalid." );
        }

        return policy;
    }

    // Callers hold _gate.
    private void AdvanceCaptureEpoch( )
    {
        Interlocked.Increment( ref _captureEpoch );
        SignalCaptureChange( );
    }

    private void SignalCaptureChange( )
    {
        var change = _captureChange;
        _captureChange = new( TaskCreationOptions.RunContinuationsAsynchronously );
        change.SetResult( );
    }

    private bool MigrationRequired( ) => Scalar( "SELECT migration_review FROM clipboard_state" ) != 0;
    private void AdvanceRevision( ) => Execute( "UPDATE clipboard_state SET revision=revision+1" );

    private void PrunePolicy( ClipboardHistoryPolicy policy )
    {
        var changed = Execute( "DELETE FROM entries WHERE pinned=0 AND copied<$cutoff",
            ("$cutoff", _time.GetUtcNow( ).AddHours( -policy.RetentionHours ).ToUnixTimeSeconds( )) );
        changed += Execute( "DELETE FROM entries WHERE id IN (SELECT id FROM entries WHERE pinned=0 ORDER BY copied DESC,rowid DESC LIMIT -1 OFFSET max(0,$maximum-(SELECT count(*) FROM entries WHERE pinned=1)))",
            ("$maximum", policy.MaximumEntries) );
        if ( changed > 0 )
        {
            AdvanceRevision( );
        }
    }

    // Prunes now, then waits until the oldest unpinned entry can expire. Inserts already enforce the
    // entry bound, so only expiry needs a timer.
    internal async Task PruneThenWaitForExpiryAsync( CancellationToken cancellationToken )
    {
        TimeSpan delay;
        lock ( _gate )
        {
            Prune( );
            delay = MigrationRequired( ) ? MaximumPruneInterval : NextExpiryDelay( );
        }

        await Task.Delay( delay, _time, cancellationToken ).ConfigureAwait( false );
    }

    private TimeSpan NextExpiryDelay( )
    {
        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT min(copied) FROM entries WHERE pinned=0";
        if ( command.ExecuteScalar( ) is not long oldest )
        {
            return MaximumPruneInterval;
        }

        // Pruning deletes an entry once the cutoff has passed its copy second. Offsets from the Unix
        // epoch stay in range for every stored timestamp.
        var expiry = TimeSpan.FromSeconds( oldest + 1 + ReadPolicy( ).RetentionHours * 3600L );
        var delay = expiry - (_time.GetUtcNow( ) - DateTimeOffset.UnixEpoch);
        return TimeSpan.FromTicks( Math.Clamp( delay.Ticks, TimeSpan.TicksPerSecond, MaximumPruneInterval.Ticks ) );
    }
}
