using System.Security.Cryptography;

namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    internal bool IsTextCaptureCurrent( long captureEpoch )
    {
        lock ( _gate )
        {
            return captureEpoch == _captureEpoch && Enabled && ReadPolicy( ).Consent == ClipboardConsent.Text;
        }
    }

    internal bool IsCaptureCurrent( long captureEpoch, ClipboardConsent consent ) =>
        consent == ClipboardConsent.Rich ? IsRichCaptureCurrent( captureEpoch ) : IsTextCaptureCurrent( captureEpoch );

    internal ClipboardCaptureAdmission AdmitCapture( IReadOnlyList<string> names, long captureEpoch, ulong hostEpoch,
        Guid selection, uint firstSequence, ClipboardConsent consent, ClipboardPayloadProjection? projections )
    {
        if ( consent != ClipboardConsent.Rich )
        {
            return AdmitTextCapture( names, captureEpoch, hostEpoch, selection, firstSequence );
        }

        if ( projections is null )
        {
            throw new InvalidOperationException( "Clipboard rich capture requires isolated projections." );
        }

        return AdmitRichCapture( names, captureEpoch, hostEpoch, selection, firstSequence, projections );
    }

    internal ClipboardCaptureAdmission AdmitTextCapture( IReadOnlyList<string> names, long captureEpoch,
        ulong hostEpoch, Guid selection, uint firstSequence )
    {
        lock ( _gate )
        {
            if ( !IsTextCaptureCurrent( captureEpoch ) || hostEpoch == 0 || selection == Guid.Empty )
            {
                throw new InvalidOperationException( "Clipboard capture is not authorized." );
            }

            var plan = ClipboardOfferPlan.Create( names, ClipboardConsent.Text );
            _payloadLedger!.Cleanup( );
            var reservation = _payloadLedger.Reserve( plan, ( long ) ReadPolicy( ).QuotaMiB * 1024 * 1024 );
            try
            {
                return new( reservation, plan, hostEpoch, selection, firstSequence, ( ) => IsTextCaptureCurrent( captureEpoch ),
                    ( admitted, _ ) =>
                    {
                        PublishTextCapture( admitted, plan, captureEpoch );
                        return ValueTask.CompletedTask;
                    } );
            }
            catch
            {
                reservation.Dispose( );
                _payloadLedger.Cleanup( );
                throw;
            }
        }
    }

    internal void PublishTextCapture( ClipboardPayloadReservation reservation, ClipboardOfferPlan plan, long captureEpoch )
    {
        lock ( _gate )
        {
            var retained = reservation.Completed.Where( static payload => payload.Offer.Kind == ClipboardOfferKind.PlainText ).ToArray( );
            if ( retained.Length is < 1 or > 2 )
            {
                throw new InvalidDataException( "Clipboard text aliases are missing or excessive." );
            }

            Dictionary<int, ReadOnlyMemory<byte>> received = [];
            foreach ( var payload in reservation.Completed )
            {
                using var lease = _payloadDirectory!.OpenRead( payload.Id );
                var limit = payload.Offer.HeaderOnly ? 8 : 32768;
                if ( payload.Length is < 1 || payload.Length > limit || lease.Stream.Length != payload.Length )
                {
                    throw new InvalidDataException( "Clipboard text offer length is invalid." );
                }

                var bytes = new byte[checked(( int ) payload.Length)];
                lease.Stream.ReadExactly( bytes );
                if ( lease.Stream.ReadByte( ) != -1 || Convert.ToHexStringLower( SHA256.HashData( bytes ) ) != payload.Digest )
                {
                    throw new InvalidDataException( "Clipboard text payload changed before publication." );
                }

                received.Add( payload.Offer.InventoryIndex, bytes );
            }

            var content = ClipboardSelectionClassifier.Classify( plan, received );
            var text = content.Text;
            var textBytes = received[retained[0].Offer.InventoryIndex].ToArray( );

            var id = Convert.ToHexString( SHA256.HashData( textBytes ) );
            var digest = TextOfferDigest( textBytes, retained.Select( static payload => payload.Offer.RawName ).ToArray( ) );
            _payloadLedger!.Publish( reservation, retained.Select( static payload => payload.Id ).ToHashSet( ),
                ( ) => IsTextCaptureCurrent( captureEpoch ), ( _, payloads ) =>
                {
                    Execute( """
                        UPDATE clipboard_payloads SET state=2 WHERE id IN (SELECT payload_id FROM clipboard_offers WHERE entry_id=$id);
                        DELETE FROM clipboard_offers WHERE entry_id=$id;
                        DELETE FROM clipboard_selections WHERE entry_id=$id;
                        INSERT INTO entries(id,text,copied,incarnation) VALUES($id,$text,$time,$incarnation)
                            ON CONFLICT(id) DO UPDATE SET copied=excluded.copied,revision=entries.revision+1;
                        INSERT INTO clipboard_selections VALUES($id,'text',$mime,$digest);
                        """, ("$id", id), ("$text", text), ("$time", _time.GetUtcNow( ).ToUnixTimeSeconds( )),
                        ("$incarnation", Guid.NewGuid( ).ToString( "N" )), ("$mime", payloads[0].Offer.RawName), ("$digest", digest) );
                    foreach ( var payload in payloads )
                    {
                        Execute( "INSERT INTO clipboard_offers VALUES($id,$payload,$mime,$index)",
                            ("$id", id), ("$payload", payload.Id.ToString( "N" )), ("$mime", payload.Offer.RawName),
                            ("$index", payload.Offer.InventoryIndex) );
                    }

                    WriteProjectionMetadata( id, EmptyProjection );
                    RequirePinnedPayloadBudget( );
                    AdvanceRevision( );
                    PrunePolicy( ReadPolicy( ) );
                } );
            QueueSequenceCopy( id );
        }
    }

    private static string TextOfferDigest( byte[] bytes, IReadOnlyList<string> names )
    {
        var streams = names.Select( _ => new MemoryStream( bytes, writable: false ) ).ToArray( );
        try
        {
            return ClipboardOfferDigest.Compute( names.Select( ( name, index ) => new ClipboardDigestOffer( name, bytes.Length, streams[index] ) ).ToArray( ) );
        }
        finally
        {
            foreach ( var stream in streams )
            {
                stream.Dispose( );
            }
        }
    }
}
