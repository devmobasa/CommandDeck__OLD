using System.Text;

namespace CommandDeck;

public sealed partial class FilePreviewService
{
    private const string UnavailableMessage = "Preview is unavailable for this file.";

    private static readonly IReadOnlyDictionary<string, string> Languages =
        new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
        {
            [".bash"] = "bash",
            [".c"] = "c",
            [".cc"] = "cpp",
            [".conf"] = "ini",
            [".cpp"] = "cpp",
            [".cs"] = "csharp",
            [".css"] = "css",
            [".desktop"] = "ini",
            [".fish"] = "fish",
            [".go"] = "go",
            [".h"] = "c",
            [".hpp"] = "cpp",
            [".htm"] = "html",
            [".html"] = "html",
            [".ini"] = "ini",
            [".java"] = "java",
            [".js"] = "javascript",
            [".json"] = "json",
            [".jsonc"] = "json",
            [".jsx"] = "javascript",
            [".kt"] = "kotlin",
            [".lua"] = "lua",
            [".md"] = "markdown",
            [".py"] = "python",
            [".qml"] = "qml",
            [".rb"] = "ruby",
            [".rs"] = "rust",
            [".service"] = "ini",
            [".sh"] = "bash",
            [".sql"] = "sql",
            [".svg"] = "xml",
            [".toml"] = "toml",
            [".ts"] = "typescript",
            [".tsx"] = "typescript",
            [".txt"] = "text",
            [".xml"] = "xml",
            [".yaml"] = "yaml",
            [".yml"] = "yaml",
            [".zsh"] = "bash"
        };

    private readonly IndexExclusionPolicy _exclusions;
    private readonly ImagePreviewRenderer _images;

    public FilePreviewService( IndexingOptions indexing, PrivacyOptions privacy, ImagePreviewRenderer? images = null )
    {
        _exclusions = new IndexExclusionPolicy( indexing, privacy );
        _images = images ?? new ImagePreviewRenderer( );
    }

    public async ValueTask<FilePreviewOutcome> ReadAsync(
        string path,
        CancellationToken cancellationToken,
        QueryExpression? expression = null,
        int matchIndex = 0 )
    {
        cancellationToken.ThrowIfCancellationRequested( );

        SafeRegularFile? opened;
        try
        {
            opened = OpenAuthorized( path );
        }
        catch ( Exception exception ) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException )
        {
            return FilePreviewOutcome.Failure( UnavailableMessage );
        }

        if ( opened is null )
        {
            return FilePreviewOutcome.Failure( UnavailableMessage );
        }

        using ( opened )
        {
            try
            {
                var header = new byte[16];
                int headerLength;
                // Probe a separate descriptor for the authorized inode, leaving
                // the existing reader's file position untouched.
                await using ( var probe = File.OpenRead( opened.ProcessDescriptorPath ) )
                {
                    headerLength = await probe.ReadAtLeastAsync( header, header.Length,
                        throwOnEndOfStream: false, cancellationToken ).ConfigureAwait( false );
                }

                var imageFormat = ImagePreviewRenderer.DetectFormat( header.AsSpan( 0, headerLength ) );
                if ( imageFormat is not null )
                {
                    return await _images.ReadAsync( opened, imageFormat, cancellationToken ).ConfigureAwait( false );
                }

                var terms = expression?.Clauses
                    .Where( clause => !clause.Excluded &&
                        clause.Field is null or QueryFieldIds.Content or QueryFieldIds.Config )
                    .Select( clause => clause.Text ).Where( text => !string.IsNullOrWhiteSpace( text ) )
                    .Distinct( StringComparer.OrdinalIgnoreCase ).ToArray( ) ?? [];
                if ( terms.Length > 0 )
                {
                    return await ReadMatchesAsync( opened, terms, matchIndex, cancellationToken )
                        .ConfigureAwait( false );
                }

                var buffer = new byte[ProtocolLimits.MaximumPreviewBytes + 1];
                var bytesRead = await opened.ReadPrefixAsync( buffer, cancellationToken )
                    .ConfigureAwait( false );
                var decodedLength = Math.Min( bytesRead, ProtocolLimits.MaximumPreviewBytes );

                if ( buffer.AsSpan( 0, decodedLength ).Contains( ( byte ) 0 ) )
                {
                    return FilePreviewOutcome.Failure( "Preview is unavailable for binary files." );
                }

                var text = Encoding.UTF8.GetString( buffer, 0, decodedLength );
                var truncated = bytesRead > ProtocolLimits.MaximumPreviewBytes;
                (text, var lineTruncated) = TruncateLines( text );

                return FilePreviewOutcome.Success( new FilePreview
                {
                    Text = text,
                    Truncated = truncated || lineTruncated,
                    Language = LanguageFor( opened.CanonicalPath )
                } );
            }
            catch ( Exception exception ) when (
                exception is IOException or UnauthorizedAccessException )
            {
                return FilePreviewOutcome.Failure( UnavailableMessage );
            }
        }
    }

    public async ValueTask<AiFileAttachmentSnapshot> ReadAiAttachmentAsync(
        string path,
        CancellationToken cancellationToken )
    {
        using var opened = OpenAuthorized( path );
        if ( opened is null )
        {
            throw new InvalidOperationException( "The selected attachment is not authorized." );
        }
        if ( opened.Length > 8L * 1024 * 1024 )
        {
            throw new InvalidOperationException( "The selected attachment exceeds 8 MiB." );
        }
        var buffer = new byte[AiLimits.MaximumAttachmentBytes + 1];
        var bytes = await opened.ReadPrefixAsync( buffer, cancellationToken ).ConfigureAwait( false );
        if ( bytes > AiLimits.MaximumAttachmentBytes )
        {
            throw new InvalidOperationException( "The selected attachment exceeds 64 KiB of text." );
        }
        if ( buffer.AsSpan( 0, bytes ).Contains( ( byte ) 0 ) )
        {
            throw new InvalidOperationException( "Binary files cannot be attached to AI requests." );
        }
        if ( !LinuxMountIdentity.TryGet( opened.Handle, out var after ) ||
            after != opened.Identity )
        {
            throw new InvalidOperationException( "The selected attachment changed while it was read." );
        }
        try
        {
            return new AiFileAttachmentSnapshot(
                new UTF8Encoding( false, true ).GetString( buffer, 0, bytes ),
                opened.CanonicalPath,
                opened.Length,
                after.MountId,
                after.DeviceMajor,
                after.DeviceMinor,
                after.Inode,
                after.ModificationSeconds,
                after.ModificationNanoseconds,
                after.ChangeSeconds,
                after.ChangeNanoseconds );
        }
        catch ( DecoderFallbackException )
        {
            throw new InvalidOperationException( "The selected attachment is not valid UTF-8 text." );
        }
    }

    private SafeRegularFile? OpenAuthorized( string path )
    {
        if ( string.IsNullOrWhiteSpace( path ) )
        {
            return null;
        }

        var requested = Path.GetFullPath( path );
        var owner = _exclusions.FindRoot( requested );
        if ( owner is null )
        {
            return null;
        }
        var opened = SafeRegularFile.Open( requested, owner.Value.Options.FollowSymbolicLinks );
        try
        {
            if ( IsAuthorized( owner.Value.Boundary, owner.Value.Options, requested, opened ) )
            {
                return opened;
            }
        }
        catch
        {
            opened.Dispose( );
            throw;
        }

        opened.Dispose( );
        return null;
    }

    private bool IsAuthorized(
        CanonicalPathBoundary boundary,
        IndexRootOptions rootOptions,
        string requested,
        SafeRegularFile opened )
    {
        if ( !FileIndexPolicyResolver.Resolve( rootOptions, requested ).IncludeContent ||
            !boundary.Contains( requested ) ||
            !boundary.ContainsOpenedPath( opened.CanonicalPath, opened.Identity.MountId ) )
        {
            return false;
        }

        return _exclusions.Match( boundary, rootOptions, requested, opened.CanonicalPath,
            directory: false ) is null;
    }

    private static (string Text, bool Truncated) TruncateLines( string text )
    {
        var lines = 1;
        for ( var index = 0; index < text.Length; index++ )
        {
            var isLineBreak = text[index] == '\n' || text[index] == '\r';
            if ( !isLineBreak )
            {
                continue;
            }

            var lineBreakStart = index;
            if ( text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' )
            {
                index++;
            }

            if ( ++lines > ProtocolLimits.MaximumPreviewLines )
            {
                return (text[..lineBreakStart], true);
            }
        }

        return (text, false);
    }

    private static string LanguageFor( string path )
    {
        var extension = Path.GetExtension( path );
        return Languages.GetValueOrDefault( extension, "text" );
    }
}

public sealed record AiFileAttachmentSnapshot(
    string Text,
    string CanonicalPath,
    long FileLength,
    ulong MountId,
    uint DeviceMajor,
    uint DeviceMinor,
    ulong Inode,
    long ModificationSeconds,
    uint ModificationNanoseconds,
    long ChangeSeconds,
    uint ChangeNanoseconds );
