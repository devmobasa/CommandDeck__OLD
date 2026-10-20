using System.Security.Cryptography;

namespace CommandDeck;

public sealed partial class ClipboardHistoryStore
{
    internal bool IsRichCaptureCurrent( long epoch )
    {
        lock ( _gate )
        {
            return epoch == _captureEpoch && Enabled && ReadPolicy( ).Consent == ClipboardConsent.Rich;
        }
    }

    internal ClipboardCaptureAdmission AdmitRichCapture( IReadOnlyList<string> names, long epoch,
        ulong hostEpoch, Guid selection, uint firstSequence, ClipboardPayloadProjection projections )
    {
        lock ( _gate )
        {
            if ( !IsRichCaptureCurrent( epoch ) || hostEpoch == 0 || selection == Guid.Empty )
            {
                throw new InvalidOperationException( "Clipboard rich capture is not authorized." );
            }

            var plan = ClipboardOfferPlan.Create( names, ClipboardConsent.Rich );
            _payloadLedger!.Cleanup( );
            var reservation = _payloadLedger.Reserve( plan, ( long ) ReadPolicy( ).QuotaMiB * 1024 * 1024 );
            try
            {
                return new( reservation, plan, hostEpoch, selection, firstSequence, ( ) => IsRichCaptureCurrent( epoch ),
                    ( admitted, token ) => PublishRichCaptureAsync( admitted, plan, epoch, projections, token ) );
            }
            catch
            {
                reservation.Dispose( );
                _payloadLedger.Cleanup( );
                throw;
            }
        }
    }

    private async ValueTask PublishRichCaptureAsync( ClipboardPayloadReservation reservation, ClipboardOfferPlan plan,
        long epoch, ClipboardPayloadProjection projections, CancellationToken token )
    {
        Dictionary<int, ClipboardPayloadLease> leases = [];
        try
        {
            lock ( _gate )
            {
                if ( !IsRichCaptureCurrent( epoch ) )
                {
                    throw new InvalidOperationException( "Clipboard rich capture authority changed." );
                }

                foreach ( var payload in reservation.Completed )
                {
                    leases.Add( payload.Offer.InventoryIndex, _payloadDirectory!.OpenRead( payload.Id ) );
                }
            }

            var streams = reservation.Completed.ToDictionary( static payload => payload.Offer.InventoryIndex,
                payload => new ClipboardClassificationOffer( leases[payload.Offer.InventoryIndex].Stream, payload.Length, payload.Digest ) );
            var content = await ClipboardSelectionClassifier.ClassifyAsync( plan, streams, null, token ).ConfigureAwait( false );
            var retained = reservation.Completed.Where( payload => content.RetainedIndexes.Contains( payload.Offer.InventoryIndex ) ).ToArray( );
            var projected = await ProjectRichContentAsync( content, retained, leases, projections, token ).ConfigureAwait( false );
            content = projected.Content;
            token.ThrowIfCancellationRequested( );

            foreach ( var payload in retained )
            {
                leases[payload.Offer.InventoryIndex].Stream.Position = 0;
            }

            var digest = ClipboardOfferDigest.Compute( retained.Select( payload =>
                new ClipboardDigestOffer( payload.Offer.RawName, payload.Length, leases[payload.Offer.InventoryIndex].Stream ) ).ToArray( ) );
            lock ( _gate )
            {
                token.ThrowIfCancellationRequested( );
                CommitRichCapture( reservation, retained, content, projected.Metadata, digest, epoch );
            }
        }
        finally
        {
            foreach ( var lease in leases.Values )
            {
                lease.Dispose( );
            }
        }
    }

    private static async ValueTask<(ClipboardSelectionContent Content, ClipboardProjectionMetadata Metadata)> ProjectRichContentAsync(
        ClipboardSelectionContent content, IReadOnlyList<ClipboardCompletedPayload> retained, Dictionary<int, ClipboardPayloadLease> leases,
        ClipboardPayloadProjection projections, CancellationToken token )
    {
        var html = retained.FirstOrDefault( static payload => payload.Offer.Kind == ClipboardOfferKind.Html );
        if ( content.Kind == ClipboardContentKind.Text && html is not null && !retained.Any( static payload => payload.Offer.Kind == ClipboardOfferKind.PlainText ) )
        {
            var text = await projections.ReadHtmlAsync( leases[html.Offer.InventoryIndex], html.Length, html.Digest, token ).ConfigureAwait( false );
            if ( text.Available )
            {
                content = content with
                {
                    Text = text.Text,
                    ExtractedText = true,
                    Truncated = text.Truncated
                };
            }
        }

        int? width = null, height = null;
        var png = retained.FirstOrDefault( static payload => payload.Offer.Kind == ClipboardOfferKind.Png );
        if ( png is not null && projections.ImagesAvailable )
        {
            // Missing decoder tooling keeps the signature-checked PNG without dimensions; a failed isolated decode rejects it.
            var image = await projections.ReadPngAsync( leases[png.Offer.InventoryIndex], png.Length, png.Digest, token ).ConfigureAwait( false );
            if ( !image.Succeeded )
            {
                throw new InvalidDataException( "Clipboard PNG validation failed." );
            }

            width = image.Preview!.ImageWidth;
            height = image.Preview.ImageHeight;
        }

        return (content, new( content.ExtractedText, content.Truncated, content.ReducedFidelity,
            content.Destinations.Select( static destination => destination.Literal ).ToArray( ), content.ReportedSource?.Literal,
            width, height, content.OpenDestination?.Literal ));
    }

    private void CommitRichCapture( ClipboardPayloadReservation reservation, IReadOnlyList<ClipboardCompletedPayload> retained,
        ClipboardSelectionContent content, ClipboardProjectionMetadata metadata, string digest, long epoch )
    {
        var id = RichEntryId( retained, content, digest );
        var preferred = PreferredRichMime( retained, content.Kind );
        _payloadLedger!.Publish( reservation, retained.Select( static payload => payload.Id ).ToHashSet( ),
            ( ) => IsRichCaptureCurrent( epoch ), ( _, payloads ) =>
            {
                Execute( """
                    UPDATE clipboard_payloads SET state=2 WHERE id IN (SELECT payload_id FROM clipboard_offers WHERE entry_id=$id);
                    DELETE FROM clipboard_offers WHERE entry_id=$id;
                    DELETE FROM clipboard_selections WHERE entry_id=$id;
                    INSERT INTO entries(id,text,copied,incarnation) VALUES($id,$text,$time,$incarnation)
                        ON CONFLICT(id) DO UPDATE SET text=excluded.text,copied=excluded.copied,revision=entries.revision+1;
                    INSERT INTO clipboard_selections VALUES($id,$kind,$mime,$digest);
                    """, ("$id", id), ("$text", content.Text), ("$time", _time.GetUtcNow( ).ToUnixTimeSeconds( )),
                    ("$incarnation", Guid.NewGuid( ).ToString( "N" )), ("$kind", content.Kind.ToString( ).ToLowerInvariant( )),
                    ("$mime", preferred), ("$digest", digest) );
                foreach ( var payload in payloads )
                {
                    Execute( "INSERT INTO clipboard_offers VALUES($id,$payload,$mime,$index)",
                        ("$id", id), ("$payload", payload.Id.ToString( "N" )), ("$mime", payload.Offer.RawName), ("$index", payload.Offer.InventoryIndex) );
                }

                WriteProjectionMetadata( id, metadata );
                RequirePinnedPayloadBudget( );
                AdvanceRevision( );
                PrunePolicy( ReadPolicy( ) );
            } );
        QueueSequenceCopy( id );
    }

    private string RichEntryId( IReadOnlyList<ClipboardCompletedPayload> retained, ClipboardSelectionContent content, string digest )
    {
        if ( retained.All( static payload => payload.Offer.Kind == ClipboardOfferKind.PlainText ) )
        {
            return Convert.ToHexString( SHA256.HashData( StrictUtf8.GetBytes( content.Text ) ) );
        }

        using var command = _connection.CreateCommand( );
        command.CommandText = "SELECT entry_id FROM clipboard_selections WHERE dedup_key=$digest";
        command.Parameters.AddWithValue( "$digest", digest );
        return command.ExecuteScalar( ) as string ?? Guid.NewGuid( ).ToString( "N" );
    }

    private static string PreferredRichMime( IReadOnlyList<ClipboardCompletedPayload> retained, ClipboardContentKind kind )
    {
        ClipboardOfferKind[] preference = kind switch
        {
            ClipboardContentKind.Files => [ClipboardOfferKind.UriList, ClipboardOfferKind.GnomeCopiedFiles],
            ClipboardContentKind.Images => [ClipboardOfferKind.Png],
            ClipboardContentKind.Links => [ClipboardOfferKind.UriList, ClipboardOfferKind.PlainText],
            _ => [ClipboardOfferKind.PlainText, ClipboardOfferKind.Html]
        };
        foreach ( var representation in preference )
        {
            var offer = retained.FirstOrDefault( payload => payload.Offer.Kind == representation );
            if ( offer is not null )
            {
                return offer.Offer.RawName;
            }
        }

        throw new InvalidDataException( "Clipboard preferred representation is unavailable." );
    }

    private void RequirePinnedPayloadBudget( )
    {
        if ( PolicyRows( ).Where( static row => row.Pinned ).Sum( static row => row.Bytes ) > ( long ) ReadPolicy( ).QuotaMiB * 1024 * 1024 / 2 )
        {
            throw new InvalidOperationException( "Clipboard pinned payload capacity is occupied; unpin an entry first." );
        }
    }
}
