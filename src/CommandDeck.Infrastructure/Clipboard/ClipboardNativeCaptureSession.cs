namespace CommandDeck;

internal sealed class ClipboardNativeCaptureSession( ClipboardHistoryStore store, ClipboardHostProcess helper,
    ulong hostEpoch, long captureEpoch, ClipboardConsent consent = ClipboardConsent.Text, ClipboardPayloadProjection? projections = null,
    Action<string>? skipped = null ) : IDisposable
{
    private readonly Dictionary<Guid, Pending> _pending = [];
    private Pending? _active;

    internal async Task RunAsync( Action configured, CancellationToken token )
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource( token );
        var monitoring = MonitorAsync( lifetime );
        try
        {
            lifetime.CancelAfter( TimeSpan.FromSeconds( 3 ) );
            var ready = await helper.Events.ReadAsync( lifetime.Token ).ConfigureAwait( false );
            if ( ClipboardCaptureProtocol.ReadStatus( ready ).Status != ClipboardCaptureStatus.Ready )
            {
                throw new InvalidDataException( "Clipboard capture host did not become ready." );
            }

            await helper.SendAsync( ClipboardCaptureProtocol.Configure( hostEpoch, consent ), lifetime.Token ).ConfigureAwait( false );
            var acknowledgement = ClipboardCaptureProtocol.ReadStatus( await helper.Events.ReadAsync( lifetime.Token ).ConfigureAwait( false ) );
            if ( acknowledgement.Status != ClipboardCaptureStatus.Configured || acknowledgement.Detail != ( uint ) consent )
            {
                throw new InvalidDataException( "Clipboard capture host did not accept the requested consent." );
            }

            lifetime.CancelAfter( Timeout.InfiniteTimeSpan );
            configured( );
            while ( true )
            {
                var frame = await helper.Events.ReadAsync( lifetime.Token ).ConfigureAwait( false );
                await AcceptAsync( frame, lifetime.Token ).ConfigureAwait( false );
                if ( _active is null )
                {
                    var next = _pending.Values.FirstOrDefault( static pending => pending.Inventory.Complete && !pending.Cancelled );
                    if ( next is not null )
                    {
                        await AdmitAsync( next, next.FirstSequence, lifetime.Token ).ConfigureAwait( false );
                    }
                }
            }
        }
        finally
        {
            await lifetime.CancelAsync( ).ConfigureAwait( false );
            try
            {
                await helper.DisposeAsync( ).ConfigureAwait( false );
            }
            finally
            {
                Dispose( );
                await monitoring.ConfigureAwait( false );
            }
        }
    }

    // Revocation is event-driven: the store signals every write that can change capture currency.
    private async Task MonitorAsync( CancellationTokenSource lifetime )
    {
        try
        {
            var change = store.NextCaptureChange;
            while ( store.IsCaptureCurrent( captureEpoch, consent ) )
            {
                await change.WaitAsync( lifetime.Token ).ConfigureAwait( false );
                change = store.NextCaptureChange;
            }
        }
        catch ( OperationCanceledException ) when ( lifetime.IsCancellationRequested ) { }
        finally
        {
            await lifetime.CancelAsync( ).ConfigureAwait( false );
        }
    }

    private async Task AcceptAsync( ClipboardHelperFrame frame, CancellationToken token )
    {
        if ( frame.Header.HostEpoch != hostEpoch )
        {
            throw new InvalidDataException( "Clipboard capture host identity changed." );
        }

        var selection = frame.Header.SelectionId;
        if ( frame.Header.Type == ClipboardHelperFrameType.SelectionStart )
        {
            if ( _pending.Count == 4 || !_pending.TryAdd( selection,
                    new( selection, new( hostEpoch, selection, ClipboardCaptureProtocol.ReadSelectionStart( frame ) ) ) ) )
            {
                throw new InvalidDataException( "Clipboard capture pending inventory bound was exceeded." );
            }

            return;
        }

        if ( selection == Guid.Empty && frame.Header.Type == ClipboardHelperFrameType.Status )
        {
            RecordSkipped( frame );
            return;
        }

        if ( !_pending.TryGetValue( selection, out var pending ) )
        {
            throw new InvalidDataException( "Clipboard capture message has no admitted selection." );
        }

        if ( frame.Header.Type == ClipboardHelperFrameType.Status )
        {
            if ( ClipboardCaptureProtocol.ReadStatus( frame ).Status != ClipboardCaptureStatus.SelectionCancelled )
            {
                throw new InvalidDataException( "Clipboard capture status is unexpected." );
            }

            Remove( pending );
            return;
        }

        if ( pending.Cancelled )
        {
            return;
        }

        if ( !pending.Inventory.Complete )
        {
            pending.Inventory.Accept( frame );
            if ( pending.Inventory.Complete )
            {
                pending.FirstSequence = frame.Header.Sequence + 1;
            }

            return;
        }

        await ReceiveContentAsync( pending, frame, token ).ConfigureAwait( false );
    }

    private async Task ReceiveContentAsync( Pending pending, ClipboardHelperFrame frame, CancellationToken token )
    {
        if ( pending != _active || pending.Admission is null || pending.Plan is null )
        {
            throw new InvalidDataException( "Clipboard content was not requested." );
        }

        try
        {
            await pending.Admission.AcceptAsync( frame, token ).ConfigureAwait( false );
            if ( pending.Admission.Complete )
            {
                Remove( pending );
            }
            else if ( frame.Header.Type == ClipboardHelperFrameType.OfferEnd && ++pending.NextOffer < pending.Plan.Offers.Count )
            {
                await RequestAsync( pending, token ).ConfigureAwait( false );
            }
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or System.Text.DecoderFallbackException ||
            exception is InvalidDataException && pending.Admission.ContentRejected )
        {
            await CancelAsync( pending, token ).ConfigureAwait( false );
            if ( frame.Header.Type == ClipboardHelperFrameType.SelectionEnd )
            {
                Remove( pending );
            }
        }
    }

    private void RecordSkipped( ClipboardHelperFrame frame )
    {
        var (status, detail) = ClipboardCaptureProtocol.ReadStatus( frame );
        if ( status != ClipboardCaptureStatus.SelectionRejected || !Enum.IsDefined( ( ClipboardInventoryRejection ) detail ) )
        {
            throw new InvalidDataException( "Clipboard capture status is unexpected." );
        }

        skipped?.Invoke( (( ClipboardInventoryRejection ) detail).ToString( ) );
    }

    private async Task AdmitAsync( Pending pending, uint firstSequence, CancellationToken token )
    {
        pending.Plan = ClipboardOfferPlan.Create( pending.Inventory.Names, consent );
        if ( _active is not null || !pending.Plan.Accepted )
        {
            await CancelAsync( pending, token ).ConfigureAwait( false );
            return;
        }

        try
        {
            pending.Admission = store.AdmitCapture( pending.Inventory.Names, captureEpoch, hostEpoch, pending.Id, firstSequence, consent, projections );
            _active = pending;
            await RequestAsync( pending, token ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            await CancelAsync( pending, token ).ConfigureAwait( false );
        }
    }

    private async Task RequestAsync( Pending pending, CancellationToken token )
    {
        var offer = pending.Plan!.Offers[pending.NextOffer];
        pending.Admission!.BeginOffer( offer.InventoryIndex );
        await helper.SendAsync( new( new( ClipboardHelperFrameType.ReceiveOffer, hostEpoch, pending.Id,
            offer.InventoryIndex, ( uint ) pending.NextOffer, 0 ), ReadOnlyMemory<byte>.Empty ), token ).ConfigureAwait( false );
    }

    private async Task CancelAsync( Pending pending, CancellationToken token )
    {
        pending.Cancelled = true;
        try
        {
            if ( !await helper.CancelAsync( pending.Id, token ).ConfigureAwait( false ) )
            {
                throw new IOException( "Clipboard capture cancellation was not acknowledged." );
            }
        }
        catch
        {
            await helper.DisposeAsync( ).ConfigureAwait( false );
            throw;
        }
        finally
        {
            pending.Admission?.Dispose( );
            if ( pending == _active )
            {
                _active = null;
            }
        }
    }

    private void Remove( Pending pending )
    {
        pending.Admission?.Dispose( );
        _pending.Remove( pending.Id );
        if ( _active == pending )
        {
            _active = null;
        }
    }

    public void Dispose( )
    {
        foreach ( var pending in _pending.Values )
        {
            pending.Admission?.Dispose( );
        }

        _pending.Clear( );
        _active = null;
    }

    private sealed class Pending( Guid id, ClipboardHelperInventory inventory )
    {
        internal Guid Id { get; } = id;
        internal ClipboardHelperInventory Inventory { get; } = inventory;
        internal ClipboardOfferPlan? Plan { get; set; }
        internal ClipboardCaptureAdmission? Admission { get; set; }
        internal int NextOffer { get; set; }
        internal uint FirstSequence { get; set; }
        internal bool Cancelled { get; set; }
    }
}
