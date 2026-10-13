using System.Security.Cryptography;

namespace CommandDeck;

/// <summary>Projects only bytes verified against a held payload lease, never referenced user files.</summary>
public sealed class ClipboardPayloadProjection( ClipboardHtmlProjectionService html, ImagePreviewRenderer? images = null )
{
    private readonly ImagePreviewRenderer _images = images ?? new( );
    public bool ImagesAvailable => _images.Available;

    public async ValueTask<ClipboardHtmlProjection> ReadHtmlAsync( ClipboardPayloadLease payload, long length, string digest, CancellationToken token )
    {
        using var worker = ClipboardProjectionWorker.TryAcquire( token );
        if ( worker is null )
        {
            return new( false, "", false, "WorkerBusy" );
        }

        var bytes = await ReadVerifiedAsync( payload, length, digest, ClipboardHtmlProjector.MaximumInputBytes, token ).ConfigureAwait( false );
        return await html.ProjectAdmittedAsync( bytes, token ).ConfigureAwait( false );
    }

    public async ValueTask<FilePreviewOutcome> ReadPngAsync( ClipboardPayloadLease payload, long length, string digest, CancellationToken token )
    {
        using var worker = ClipboardProjectionWorker.TryAcquire( token );
        if ( worker is null )
        {
            return FilePreviewOutcome.Failure( "Clipboard preview is busy." );
        }

        var bytes = await ReadVerifiedAsync( payload, length, digest, ImagePreviewRenderer.MaximumInputBytes, token ).ConfigureAwait( false );
        return await _images.ReadClipboardPngAsync( bytes, token ).ConfigureAwait( false );
    }

    private static async ValueTask<byte[]> ReadVerifiedAsync( ClipboardPayloadLease payload, long length, string digest, int maximum, CancellationToken token )
    {
        ArgumentNullException.ThrowIfNull( payload );
        if ( length is < 1 || length > maximum || !LinuxMountIdentity.TryGet( payload.Stream.SafeFileHandle, out var before ) ||
            before.Size != ( ulong ) length )
        {
            throw new InvalidDataException( "Clipboard projection payload length is invalid." );
        }

        var bytes = new byte[checked(( int ) length)];
        var offset = 0;
        while ( offset < bytes.Length )
        {
            var read = await RandomAccess.ReadAsync( payload.Stream.SafeFileHandle, bytes.AsMemory( offset ), offset, token ).ConfigureAwait( false );
            if ( read == 0 )
            {
                throw new InvalidDataException( "Clipboard projection payload ended early." );
            }

            offset += read;
        }

        token.ThrowIfCancellationRequested( );
        if ( !LinuxMountIdentity.TryGet( payload.Stream.SafeFileHandle, out var after ) || before != after ||
            !string.Equals( Convert.ToHexStringLower( SHA256.HashData( bytes ) ), digest, StringComparison.Ordinal ) )
        {
            throw new InvalidDataException( "Clipboard projection payload changed." );
        }

        return bytes;
    }
}
