using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

internal sealed class IndexPolicyInputs
{
    internal const int MaximumRuleBytes = 1024 * 1024;
    private readonly ConcurrentDictionary<string, CachedMarkers> _directories = new( StringComparer.Ordinal );
    private readonly ConcurrentDictionary<string, CachedText> _files = new( StringComparer.Ordinal );
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _controlScopes = new( StringComparer.Ordinal );
    private readonly ConcurrentDictionary<string, PolicyInputStamp> _observed = new( StringComparer.Ordinal );
    private readonly object _observationLock = new( );
    private long _revision;

    public long Revision => Interlocked.Read( ref _revision );

    public IReadOnlySet<string> Markers( string directory )
    {
        var version = MetadataVersion( directory );
        if ( !_directories.TryGetValue( directory, out var cached ) || cached.Version != version )
        {
            var names = ReadMarkers( directory );
            cached = new CachedMarkers( version, names, MarkerFingerprint( names ) );
            _directories[directory] = cached;
        }
        Observe( directory, directory: true, cached.Fingerprint );
        return cached.Names;
    }

    public PolicyText ReadText( string path )
    {
        var version = MetadataVersion( path );
        if ( !_files.TryGetValue( path, out var cached ) || cached.Version != version )
        {
            cached = new CachedText( version, ReadBoundedText( path ) );
            _files[path] = cached;
        }
        Observe( path, directory: false, cached.Text.Fingerprint );
        return cached.Text;
    }

    public void Invalidate( string path )
    {
        _files.TryRemove( path, out _ );
        _directories.TryRemove( path, out _ );
        var parent = Path.GetDirectoryName( path );
        if ( parent is not null )
        {
            _directories.TryRemove( parent, out _ );
        }
    }

    public void RegisterControlFile( string path, string scope )
    {
        lock ( _observationLock )
        {
            if ( _controlScopes.GetOrAdd( path,
                    static _ => new ConcurrentDictionary<string, byte>( StringComparer.Ordinal ) ).TryAdd( scope, 0 ) )
            {
                Interlocked.Increment( ref _revision );
            }
        }
    }

    public void PruneControlScopes( IReadOnlyList<string> roots )
    {
        lock ( _observationLock )
        {
            foreach ( var entry in _controlScopes )
            {
                foreach ( var scope in entry.Value.Keys )
                {
                    if ( !roots.Any( root => Within( scope, root ) || Within( root, scope ) ) )
                    {
                        entry.Value.TryRemove( scope, out _ );
                    }
                }
                if ( entry.Value.IsEmpty )
                {
                    _controlScopes.TryRemove( entry.Key, out _ );
                    _observed.TryRemove( entry.Key, out _ );
                    _files.TryRemove( entry.Key, out _ );
                    Interlocked.Increment( ref _revision );
                }
            }
        }

        static bool Within( string path, string root ) =>
            string.Equals( path, root, StringComparison.Ordinal ) ||
            path.StartsWith( root.TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar, StringComparison.Ordinal );
    }

    public string[] ControlFiles( ) => _controlScopes.Keys.ToArray( );

    public IEnumerable<string> ControlScopes( string changedPath ) => _controlScopes
        .Where( pair => pair.Key == changedPath || pair.Key.StartsWith( changedPath.TrimEnd( '/' ) + "/", StringComparison.Ordinal ) )
        .SelectMany( static pair => pair.Value.Keys );

    public PolicyInputStamp[] Snapshot( out long revision )
    {
        lock ( _observationLock )
        {
            revision = _revision;
            return _observed.Values.Select( stamp => stamp with
            {
                Scopes = _controlScopes.TryGetValue( stamp.Path, out var scopes ) ? scopes.Keys.ToArray( ) : []
            } ).ToArray( );
        }
    }

    public bool RestoreIfCurrent( IReadOnlyList<PolicyInputStamp> stamps, CancellationToken cancellationToken = default )
    {
        foreach ( var stamp in stamps )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            var current = stamp.Directory
                ? MarkerFingerprint( ReadMarkers( stamp.Path ) )
                : ReadBoundedText( stamp.Path ).Fingerprint;
            if ( !string.Equals( current, stamp.Fingerprint, StringComparison.Ordinal ) )
            {
                return false;
            }
        }
        foreach ( var stamp in stamps )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            Observe( stamp.Path, stamp.Directory, stamp.Fingerprint );
            foreach ( var scope in stamp.Scopes )
            {
                RegisterControlFile( stamp.Path, scope );
            }
        }
        return true;
    }

    internal static bool IsMarkerName( string name ) => name is
        ".git" or ".gitignore" or "package.json" or "Cargo.toml" or "pyproject.toml" or
        "setup.py" or "setup.cfg" or "requirements.txt" or "Pipfile" or "pyvenv.cfg" or
        "pom.xml" or "build.gradle" or "build.gradle.kts" or "settings.gradle" or "settings.gradle.kts" or
        "composer.json" || IsDotNetProject( name );

    internal static bool IsDotNetProject( string name ) => Path.GetExtension( name ) is
        ".csproj" or ".fsproj" or ".vbproj";

    private void Observe( string path, bool directory, string fingerprint )
    {
        lock ( _observationLock )
        {
            if ( _observed.TryGetValue( path, out var previous ) &&
                previous.Directory == directory && previous.Fingerprint == fingerprint )
            {
                return;
            }
            _observed[path] = new PolicyInputStamp( path, directory, fingerprint );
            Interlocked.Increment( ref _revision );
        }
    }

    private static HashSet<string> ReadMarkers( string directory )
    {
        HashSet<string> names = new( StringComparer.Ordinal );
        try
        {
            foreach ( var entry in Directory.EnumerateFileSystemEntries( directory ) )
            {
                var name = Path.GetFileName( entry );
                if ( !IsMarkerName( name ) )
                {
                    continue;
                }
                var attributes = File.GetAttributes( entry );
                if ( (attributes & FileAttributes.ReparsePoint) != 0 )
                {
                    continue;
                }
                if ( name == ".git" || (attributes & FileAttributes.Directory) == 0 )
                {
                    names.Add( name );
                }
            }
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
        {
            // Traversal reports unavailable directories; incomplete marker discovery
            // never turns an ambiguous user directory into a generated directory.
            names.Clear( );
        }
        return names;
    }

    private static string MarkerFingerprint( IReadOnlySet<string> names ) => Hash(
        string.Join( '\0', names.Order( StringComparer.Ordinal ) ) );

    private static PolicyText ReadBoundedText( string path )
    {
        try
        {
            var attributes = File.GetAttributes( path );
            if ( (attributes & FileAttributes.ReparsePoint) != 0 )
            {
                return new PolicyText( "", "symlink", null );
            }
            using var opened = SafeRegularFile.Open( path );
            if ( opened.Length > MaximumRuleBytes )
            {
                return new PolicyText( "", "too-large", "rule-file-too-large" );
            }
            var bytes = new byte[checked(( int ) opened.Length)];
            var total = 0;
            while ( total < bytes.Length )
            {
                var count = RandomAccess.Read( opened.Handle, bytes.AsSpan( total ), total );
                if ( count == 0 )
                {
                    break;
                }
                total += count;
            }
            if ( total != bytes.Length )
            {
                return new PolicyText( "", "changed", "rule-file-changed-during-read" );
            }
            var text = new UTF8Encoding( false, true ).GetString( bytes );
            return new PolicyText( text, Convert.ToHexString( SHA256.HashData( bytes ) ), null );
        }
        catch ( Exception exception ) when ( exception is FileNotFoundException or DirectoryNotFoundException )
        {
            return new PolicyText( "", "missing", null );
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or DecoderFallbackException )
        {
            return new PolicyText( "", "unreadable", "rule-file-unreadable" );
        }
    }

    // Traversal asks once per directory, so the version is one status call.
    private static LinuxPathVersion MetadataVersion( string path ) => LinuxMountIdentity.GetVersion( path );

    private static string Hash( string value ) => Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( value ) ) );

    private sealed record CachedMarkers( LinuxPathVersion Version, HashSet<string> Names, string Fingerprint );
    private sealed record CachedText( LinuxPathVersion Version, PolicyText Text );
}

internal sealed record PolicyText( string Text, string Fingerprint, string? Error );
internal sealed record PolicyInputStamp( string Path, bool Directory, string Fingerprint )
{
    public string[] Scopes { get; init; } = [];
}
