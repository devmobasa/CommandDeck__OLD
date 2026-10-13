using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CommandDeck;

public sealed record AgentContextAuthorization(
    bool Allowed,
    bool RequiresDecision,
    bool Protected,
    string CanonicalPath,
    string? ProposedFolder,
    string Reason );

public sealed record AgentDirectoryEntry( string Name, string Kind, long Size );

public sealed record AgentSearchMatch( string Path, int Line, string Text );

public sealed partial class AgentContextBroker
{
    private const int OpenReadOnly = 0;
    private const int OpenNonBlocking = 0x800;
    private const int OpenDirectory = 0x10000;
    private const int OpenNoFollow = 0x20000;
    private const int OpenCloseOnExec = 0x80000;
    private static readonly string[] ProtectedRelativePaths =
    [
        ".ssh", ".gnupg", ".password-store", ".local/share/keyrings", ".config/gcloud", ".aws", ".azure",
        ".kube", ".docker", ".npmrc", ".pypirc", ".netrc", ".git-credentials", ".codex", ".claude",
        ".config/commanddeck", ".local/state/commanddeck", ".mozilla", ".config/chromium",
        ".config/google-chrome", ".config/BraveSoftware", ".gradle/gradle.properties", ".m2/settings.xml",
        ".cargo/credentials.toml", ".cargo/credentials", ".config/gh/hosts.yml", ".local/share/commanddeck",
        ".config/op", ".1password", ".config/Bitwarden", ".local/share/1password", ".config/sops",
        ".terraform.d/credentials.tfrc.json", ".config/hcloud", ".config/doctl", ".oci", ".config/rclone"
    ];

    private static readonly string[] ProtectedFileNames =
    [
        ".env", ".envrc", "credentials.json", "token.json", "secrets.json", "secrets.yaml", "secrets.yml"
    ];

    private static readonly string[] ProtectedExtensions = [".pem", ".key", ".kdbx", ".p12", ".pfx", ".ppk", ".asc"];

    private readonly AgentAuthority _authority;
    private readonly string _home;
    private readonly string[] _protectedDirectories;

    public AgentContextBroker( AgentAuthority authority, string? homeDirectory = null, AgentOptions? options = null )
    {
        if ( authority is null )
        {
            throw new ArgumentNullException( nameof( authority ) );
        }

        _authority = authority;
        _home = Path.TrimEndingDirectorySeparator( Path.GetFullPath(
            homeDirectory ?? Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ) ) );
        var configured = options ?? new AgentOptions( );
        _protectedDirectories =
        [
            Path.TrimEndingDirectorySeparator( PathExpansion.Expand( configured.StateDirectory ) ),
            Path.TrimEndingDirectorySeparator( PathExpansion.Expand( configured.RuntimeDirectory ) )
        ];
    }

    public string HomeDirectory => _home;

    public static string Canonicalize( string requestedPath, string home )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( requestedPath );
        if ( requestedPath.Contains( '\0', StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( "The path contains an invalid character." );
        }
        var expanded = requestedPath == "~" ? home
            : requestedPath.StartsWith( "~/", StringComparison.Ordinal ) ? Path.Combine( home, requestedPath[2..] )
            : requestedPath;
        if ( !Path.IsPathRooted( expanded ) )
        {
            expanded = Path.Combine( home, expanded );
        }
        var full = Path.TrimEndingDirectorySeparator( Path.GetFullPath( expanded ) );
        return ResolveLinks( full );
    }

    public bool IsProtected( string canonicalPath ) =>
        IsProtectedPath( canonicalPath, _home ) || _protectedDirectories.Any( directory =>
            IsWithinDirectory( canonicalPath, directory ) ||
            IsWithinDirectory( canonicalPath, ResolveProtectedDirectory( directory ) ) );

    private static bool IsWithinDirectory( string path, string directory ) =>
        string.Equals( path, directory, StringComparison.Ordinal ) ||
        path.StartsWith( Path.EndsInDirectorySeparator( directory ) ? directory : directory + "/", StringComparison.Ordinal );

    // Resolve every existing ancestor, including storage whose leaf directory
    // has not yet been created by a backend.
    private static string ResolveProtectedDirectory( string directory )
    {
        var root = Path.GetPathRoot( directory )!;
        var resolved = root;
        foreach ( var segment in directory[root.Length..].Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries ) )
        {
            resolved = Path.Combine( resolved, segment );
            if ( Directory.Exists( resolved ) )
            {
                resolved = ResolveLinks( resolved );
            }
        }

        return Path.TrimEndingDirectorySeparator( resolved );
    }

    internal static bool IsProtectedPath( string canonicalPath, string home )
    {
        var relative = Path.GetRelativePath( home, canonicalPath );
        if ( relative.StartsWith( "..", StringComparison.Ordinal ) )
        {
            return false;
        }
        foreach ( var protectedPath in ProtectedRelativePaths )
        {
            if ( string.Equals( relative, protectedPath, StringComparison.Ordinal ) ||
                relative.StartsWith( protectedPath + "/", StringComparison.Ordinal ) )
            {
                return true;
            }
        }
        var name = Path.GetFileName( canonicalPath );
        if ( ProtectedFileNames.Contains( name, StringComparer.Ordinal ) ||
            name.StartsWith( ".env.", StringComparison.Ordinal ) ||
            name.StartsWith( "id_", StringComparison.Ordinal ) )
        {
            return true;
        }
        var extension = Path.GetExtension( name );
        return ProtectedExtensions.Contains( extension, StringComparer.OrdinalIgnoreCase );
    }

    public async ValueTask<AgentContextAuthorization> AuthorizeAsync(
        string requestedPath,
        string operation,
        AgentPreferences preferences,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( preferences );
        string canonical;
        try
        {
            canonical = Canonicalize( requestedPath, _home );
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or ArgumentException )
        {
            return new AgentContextAuthorization( false, false, false, requestedPath, null, "The path cannot be resolved." );
        }
        if ( preferences.TrustLevel < AgentTrustLevels.ApprovedContext )
        {
            return new AgentContextAuthorization( false, false, false, canonical, null, "File context is not enabled at this trust level." );
        }
        if ( !IsUnderHome( canonical ) )
        {
            return new AgentContextAuthorization( false, false, false, canonical, null, "Only paths under your home directory are readable." );
        }
        var protectedTarget = IsProtected( canonical );
        // Unrestricted removes every safety layer, protected paths included:
        // the user typed the phrase and holds a lease. Every lower level keeps
        // protected paths behind an explicit one-time override.
        if ( preferences.TrustLevel >= AgentTrustLevels.Unrestricted )
        {
            return new AgentContextAuthorization( true, false, protectedTarget, canonical, null,
                protectedTarget ? "Unrestricted access includes protected paths." : "Unrestricted access is enabled." );
        }
        if ( protectedTarget )
        {
            return new AgentContextAuthorization( false, true, true, canonical, null, "This path holds credentials or private configuration." );
        }
        if ( preferences.TrustLevel >= AgentTrustLevels.ReadEntireHome )
        {
            return new AgentContextAuthorization( true, false, false, canonical, null, "Read entire home is enabled." );
        }
        if ( preferences.ContextPolicy == AgentContextPolicies.ApprovedFolders &&
            await IsGrantedAsync( canonical, cancellationToken ).ConfigureAwait( false ) )
        {
            return new AgentContextAuthorization( true, false, false, canonical, null, "Inside an approved folder." );
        }
        var proposed = operation == AgentContextOperations.ReadFile ? Path.GetDirectoryName( canonical ) ?? canonical : canonical;
        return new AgentContextAuthorization( false, true, false, canonical, proposed, "Approval is required for this folder." );
    }

    public async ValueTask<AgentGrant> GrantFolderAsync( string canonicalFolder, CancellationToken cancellationToken )
    {
        if ( !Directory.Exists( canonicalFolder ) || !IsUnderHome( canonicalFolder ) || IsProtected( canonicalFolder ) )
        {
            throw new InvalidOperationException( "The folder cannot be approved." );
        }
        var identity = LinuxPathIdentity.TryGet( canonicalFolder, out var token ) ? token : string.Empty;
        AgentGrant grant = new( )
        {
            Id = Convert.ToHexString( RandomNumberGenerator.GetBytes( 16 ) ).ToLowerInvariant( ),
            Kind = AgentGrantKinds.Folder,
            Scope = canonicalFolder,
            Identity = identity,
            PayloadDigest = Digest( canonicalFolder + "\n" + identity ),
            State = AgentGrantStates.Active,
            Label = Path.GetFileName( canonicalFolder )
        };
        return await _authority.AddGrantAsync( grant, cancellationToken ).ConfigureAwait( false );
    }

    public async ValueTask<bool> IsGrantedAsync( string canonicalPath, CancellationToken cancellationToken )
    {
        var grants = await _authority.GetGrantsAsync( cancellationToken ).ConfigureAwait( false );
        foreach ( var grant in grants )
        {
            if ( grant.Kind != AgentGrantKinds.Folder || grant.State != AgentGrantStates.Active )
            {
                continue;
            }
            if ( !string.Equals( canonicalPath, grant.Scope, StringComparison.Ordinal ) &&
                !canonicalPath.StartsWith( grant.Scope + "/", StringComparison.Ordinal ) )
            {
                continue;
            }
            if ( !string.IsNullOrEmpty( grant.Identity ) &&
                (!LinuxPathIdentity.TryGet( grant.Scope, out var current ) ||
                    !string.Equals( current, grant.Identity, StringComparison.Ordinal )) )
            {
                await _authority.MarkGrantStateAsync(
                    grant.Id, AgentGrantStates.Stale, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds( ), cancellationToken )
                    .ConfigureAwait( false );
                continue;
            }
            return true;
        }
        return false;
    }

    public IReadOnlyList<AgentDirectoryEntry> ListDirectory( string canonicalDirectory, int maximumEntries )
    {
        using var opened = OpenAuthorizedDirectory( canonicalDirectory );
        List<AgentDirectoryEntry> entries = [];
        EnumerationOptions options = new( )
        {
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            IgnoreInaccessible = true
        };
        foreach ( var path in Directory.EnumerateFileSystemEntries( opened.DescriptorPath, "*", options ).Order( StringComparer.Ordinal ) )
        {
            if ( entries.Count >= maximumEntries )
            {
                break;
            }
            var name = Path.GetFileName( path );
            if ( IsProtected( Path.Combine( opened.CanonicalPath, name ) ) )
            {
                continue;
            }
            var attributes = File.GetAttributes( path );
            var link = (attributes & FileAttributes.ReparsePoint) != 0;
            var directory = (attributes & FileAttributes.Directory) != 0;
            var size = directory || link ? 0 : new FileInfo( path ).Length;
            entries.Add( new AgentDirectoryEntry( name, link ? "link" : directory ? "directory" : "file", size ) );
        }
        return entries;
    }

    public string ReadTextFile( string canonicalFile, int maximumBytes, out bool truncated )
    {
        using var file = OpenAuthorizedFile( canonicalFile );
        return ReadOpenedTextFile( file, maximumBytes, out truncated );
    }

    private static string ReadOpenedTextFile( SafeRegularFile file, int maximumBytes, out bool truncated )
    {
        var length = ( int ) Math.Min( file.Length, maximumBytes );
        truncated = file.Length > maximumBytes;
        var buffer = new byte[length];
        var total = 0;
        while ( total < length )
        {
            var read = RandomAccess.Read( file.Handle, buffer.AsSpan( total ), total );
            if ( read <= 0 )
            {
                break;
            }
            total += read;
        }
        if ( buffer.AsSpan( 0, Math.Min( total, 8 * 1024 ) ).IndexOf( ( byte ) 0 ) >= 0 )
        {
            throw new InvalidOperationException( "The file is binary and cannot be read as text." );
        }
        try
        {
            return new UTF8Encoding( false, true ).GetString( buffer, 0, total );
        }
        catch ( DecoderFallbackException )
        {
            throw new InvalidOperationException( "The file is not valid UTF-8 text." );
        }
    }

    public IReadOnlyList<AgentSearchMatch> SearchText(
        string canonicalDirectory,
        string query,
        int maximumMatches,
        int maximumBytes,
        CancellationToken cancellationToken )
    {
        if ( string.IsNullOrWhiteSpace( query ) || query.Length > 256 )
        {
            throw new InvalidOperationException( "The search query is empty or too long." );
        }
        List<AgentSearchMatch> matches = [];
        long budget = maximumBytes;
        EnumerationOptions options = new( )
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 6,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        using var opened = OpenAuthorizedDirectory( canonicalDirectory );
        foreach ( var path in Directory.EnumerateFiles( opened.DescriptorPath, "*", options ) )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            if ( matches.Count >= maximumMatches || budget <= 0 )
            {
                break;
            }
            var relative = Path.GetRelativePath( opened.DescriptorPath, path );
            var expectedPath = Path.GetFullPath( Path.Combine( opened.CanonicalPath, relative ) );
            if ( IsProtected( expectedPath ) )
            {
                continue;
            }
            SearchOneFile( path, expectedPath, query, matches, maximumMatches, ref budget );
        }
        return matches;
    }

    private void SearchOneFile(
        string path,
        string expectedPath,
        string query,
        List<AgentSearchMatch> matches,
        int maximumMatches,
        ref long budget )
    {
        string text;
        try
        {
            using var file = SafeRegularFile.Open( path, followLeafSymbolicLink: false );
            EnsureOpenedPath( expectedPath, file.CanonicalPath );
            text = ReadOpenedTextFile( file, ( int ) Math.Min( AgentLimits.MaximumFileBytes, budget ), out _ );
        }
        catch ( InvalidOperationException )
        {
            return;
        }
        budget -= Encoding.UTF8.GetByteCount( text );
        var line = 0;
        foreach ( var candidate in text.Split( '\n' ) )
        {
            line++;
            if ( candidate.Contains( query, StringComparison.OrdinalIgnoreCase ) )
            {
                var trimmed = candidate.Length > 240 ? candidate[..240] : candidate;
                matches.Add( new AgentSearchMatch( Path.GetRelativePath( _home, expectedPath ), line, trimmed.TrimEnd( '\r' ) ) );
                if ( matches.Count >= maximumMatches )
                {
                    return;
                }
            }
        }
    }

    private bool IsUnderHome( string canonicalPath ) =>
        string.Equals( canonicalPath, _home, StringComparison.Ordinal ) ||
        canonicalPath.StartsWith( _home + "/", StringComparison.Ordinal );

    private SafeRegularFile OpenAuthorizedFile( string expectedPath )
    {
        var file = SafeRegularFile.Open( expectedPath, followLeafSymbolicLink: false );
        try
        {
            EnsureOpenedPath( expectedPath, file.CanonicalPath );
            return file;
        }
        catch
        {
            file.Dispose( );
            throw;
        }
    }

    private OpenedDirectory OpenAuthorizedDirectory( string expectedPath )
    {
        var descriptor = OpenNative(
            Path.GetFullPath( expectedPath ),
            OpenReadOnly | OpenNonBlocking | OpenDirectory | OpenCloseOnExec | OpenNoFollow );
        if ( descriptor < 0 )
        {
            throw new InvalidOperationException( "The directory cannot be opened safely." );
        }
        SafeFileHandle handle = new( ( IntPtr ) descriptor, ownsHandle: true );
        try
        {
            var target = Directory.ResolveLinkTarget( $"/proc/self/fd/{descriptor}", returnFinalTarget: true );
            if ( target is null )
            {
                throw new InvalidOperationException( "The directory identity cannot be resolved safely." );
            }
            var canonical = Path.TrimEndingDirectorySeparator( Path.GetFullPath( target.FullName ) );
            EnsureOpenedPath( expectedPath, canonical );
            return new OpenedDirectory( handle, canonical );
        }
        catch
        {
            handle.Dispose( );
            throw;
        }
    }

    private void EnsureOpenedPath( string expectedPath, string openedPath )
    {
        var expected = Path.TrimEndingDirectorySeparator( Path.GetFullPath( expectedPath ) );
        if ( !IsUnderHome( openedPath ) || !string.Equals( expected, openedPath, StringComparison.Ordinal ) )
        {
            throw new InvalidOperationException( "The authorized path changed before it could be opened." );
        }
    }

    private static string ResolveLinks( string fullPath )
    {
        FileSystemInfo info = Directory.Exists( fullPath ) ? new DirectoryInfo( fullPath ) : new FileInfo( fullPath );
        if ( !info.Exists )
        {
            return fullPath;
        }
        var target = info.ResolveLinkTarget( returnFinalTarget: true );
        return target is null ? fullPath : Path.TrimEndingDirectorySeparator( Path.GetFullPath( target.FullName ) );
    }

    private static string Digest( string value ) =>
        Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( value ) ) ).ToLowerInvariant( );

    private sealed class OpenedDirectory( SafeFileHandle handle, string canonicalPath ) : IDisposable
    {
        public SafeFileHandle Handle { get; } = handle;
        public string CanonicalPath { get; } = canonicalPath;
        public string DescriptorPath => $"/proc/{Environment.ProcessId}/fd/{checked(( int ) Handle.DangerousGetHandle( ))}";
        public void Dispose( ) => Handle.Dispose( );
    }

    [LibraryImport( "libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true )]
    private static partial int OpenNative( string path, int flags );
}
