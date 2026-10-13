namespace CommandDeck;

public sealed partial class ClipboardAssociationResolver
{
    private DesktopEntry? ResolveEntry( ApplicationIndex applications, Layout layout, string id )
    {
        if ( _entries.TryGetValue( id, out var cached ) )
        {
            return cached;
        }

        DesktopEntry? entry = null;
        foreach ( var root in layout.ApplicationRoots )
        {
            var candidates = applications.Candidates( root, id );
            if ( candidates.Count == 0 )
            {
                continue;
            }

            // The highest-priority root decides: a same-root collision or an invalid/hidden entry never revives a lower file.
            if ( candidates.Count == 1 )
            {
                var bytes = ReadBounded( candidates[0], MaximumDesktopFileBytes, required: true );
                var text = bytes.Length == 0 ? null : DecodeUtf8( bytes );
                entry = text is null ? null : DesktopEntry.Parse( text, candidates[0], environment.Path );
            }

            break;
        }

        _entries[id] = entry;
        return entry;
    }

    private string? Evaluate( DesktopEntry entry, ClipboardLaunchTarget targetKind, string target )
    {
        if ( entry.DBusActivatable )
        {
            return ClipboardHandlerReasons.UnsupportedActivation;
        }

        if ( entry.Exec is null )
        {
            return "InvalidDesktopExec";
        }

        var rejection = ClipboardDesktopExec.TryExpand( entry.Exec, entry.LocalizedName( environment.Locale ), entry.Icon, entry.Path,
            targetKind, target, out var launch );
        if ( rejection != ClipboardLaunchRejection.None )
        {
            return rejection.ToString( );
        }

        var executable = ResolveExecutable( launch!.Executable );
        if ( executable is null )
        {
            return ClipboardHandlerReasons.ExecutableUnavailable;
        }

        Record( "executable", executable + "\n" + FileIdentity( executable ) );
        if ( entry.Terminal )
        {
            return ClipboardHandlerReasons.UnsupportedTerminalLaunch;
        }

        var directory = entry.WorkingDirectory;
        if ( directory is not null && !ValidWorkingDirectory( directory ) )
        {
            return "WorkingDirectoryUnavailable";
        }

        Record( "working-directory", directory ?? "/" );
        return null;
    }

    private string? ResolveExecutable( string token )
    {
        if ( token.StartsWith( '/' ) )
        {
            return IsExecutable( token ) ? token : null;
        }

        foreach ( var directory in environment.Path.Split( ':', StringSplitOptions.RemoveEmptyEntries ) )
        {
            if ( !directory.StartsWith( '/' ) )
            {
                continue;
            }

            var candidate = System.IO.Path.Combine( directory, token );
            if ( IsExecutable( candidate ) )
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsExecutable( string path )
    {
        try
        {
            FileInfo info = new( path );
            return OperatingSystem.IsLinux( ) && info.Exists && (File.GetUnixFileMode( path ) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
        {
            return false;
        }
    }

    private static string FileIdentity( string path )
    {
        FileInfo info = new( path );
        return info.Length.ToString( System.Globalization.CultureInfo.InvariantCulture ) + "\n" +
            info.LastWriteTimeUtc.Ticks.ToString( System.Globalization.CultureInfo.InvariantCulture );
    }

    private static bool ValidWorkingDirectory( string directory )
    {
        if ( !directory.StartsWith( '/' ) || directory.Contains( "//", StringComparison.Ordinal ) ||
            directory.Split( '/' ).Any( static segment => segment is "." or ".." ) || directory.Length > 1 && directory.EndsWith( '/' ) )
        {
            return false;
        }

        var current = "";
        foreach ( var segment in directory.Split( '/', StringSplitOptions.RemoveEmptyEntries ) )
        {
            current += "/" + segment;
            DirectoryInfo info = new( current );
            if ( !info.Exists || info.LinkTarget is not null )
            {
                return false;
            }
        }

        return true;
    }

    private ApplicationIndex IndexApplications( Layout layout )
    {
        ApplicationIndex index = new( );
        foreach ( var root in layout.ApplicationRoots )
        {
            if ( !Directory.Exists( root ) )
            {
                Record( "missing-root", root );
                continue;
            }

            try
            {
                foreach ( var file in Directory.EnumerateFiles( root, "*.desktop", SearchOption.AllDirectories ) )
                {
                    if ( ++_entryCount > MaximumEntries )
                    {
                        throw new DiscoveryUnavailableException( );
                    }

                    var id = System.IO.Path.GetRelativePath( root, file ).Replace( System.IO.Path.DirectorySeparatorChar, '-' );
                    index.Add( root, id, file );
                }
            }
            catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
            {
                throw new DiscoveryUnavailableException( );
            }

            Record( "root", root + "\n" + string.Join( '\n', index.Inventory( root ) ) );
        }

        return index;
    }

    private IEnumerable<string> DeclaredIds( ApplicationIndex applications, Layout layout, string root, string key )
    {
        foreach ( var id in applications.Inventory( root ) )
        {
            var entry = ResolveEntry( applications, layout, id );
            if ( entry is not null && entry.MimeTypes.Contains( key, StringComparer.Ordinal ) && entry.Path.StartsWith( root, StringComparison.Ordinal ) )
            {
                yield return id;
            }
        }
    }

    private sealed class ApplicationIndex
    {
        private readonly Dictionary<string, Dictionary<string, List<string>>> _roots = new( StringComparer.Ordinal );

        internal void Add( string root, string id, string file )
        {
            if ( !_roots.TryGetValue( root, out var ids ) )
            {
                ids = new( StringComparer.Ordinal );
                _roots.Add( root, ids );
            }

            if ( !ids.TryGetValue( id, out var files ) )
            {
                files = [];
                ids.Add( id, files );
            }

            files.Add( file );
        }

        internal List<string> Candidates( string root, string id ) =>
            _roots.TryGetValue( root, out var ids ) && ids.TryGetValue( id, out var files ) ? files : [];

        internal IEnumerable<string> Inventory( string root ) =>
            _roots.TryGetValue( root, out var ids ) ? ids.Keys.Order( StringComparer.Ordinal ) : [];
    }

    private sealed class Layout
    {
        internal List<AssociationLocation> AssociationLocations { get; } = [];
        internal List<string> ApplicationRoots { get; } = [];

        internal static Layout Create( ClipboardResolverEnvironment environment )
        {
            var configHome = AbsoluteOrDefault( environment.ConfigHome, System.IO.Path.Combine( environment.Home, ".config" ) );
            var dataHome = AbsoluteOrDefault( environment.DataHome, System.IO.Path.Combine( environment.Home, ".local", "share" ) );
            var configDirs = SplitDirectories( environment.ConfigDirs, ["/etc/xdg"] );
            var dataDirs = SplitDirectories( environment.DataDirs, ["/usr/local/share", "/usr/share"] );
            var desktops = (environment.CurrentDesktop ?? "").Split( ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
                .Select( static desktop => desktop.ToLowerInvariant( ) ).Where( static desktop => desktop.All( static character => char.IsAsciiLetterOrDigit( character ) || character is '-' or '_' ) )
                .Distinct( StringComparer.Ordinal ).ToArray( );

            Layout layout = new( );
            foreach ( var directory in new[] { configHome }.Concat( configDirs ).Distinct( StringComparer.Ordinal ) )
            {
                layout.AssociationLocations.Add( new( directory, null, desktops ) );
            }

            foreach ( var directory in new[] { dataHome }.Concat( dataDirs ).Distinct( StringComparer.Ordinal ) )
            {
                var applications = System.IO.Path.Combine( directory, "applications" );
                layout.AssociationLocations.Add( new( applications, applications, desktops ) );
                layout.ApplicationRoots.Add( applications );
            }

            return layout;
        }

        private static string AbsoluteOrDefault( string? value, string fallback ) =>
            value is { Length: > 0 } && value.StartsWith( '/' ) ? System.IO.Path.TrimEndingDirectorySeparator( value ) : fallback;

        private static string[] SplitDirectories( string? value, string[] fallback )
        {
            if ( string.IsNullOrEmpty( value ) )
            {
                return fallback;
            }

            var directories = value.Split( ':', StringSplitOptions.RemoveEmptyEntries ).Where( static directory => directory.StartsWith( '/' ) )
                .Select( System.IO.Path.TrimEndingDirectorySeparator ).Distinct( StringComparer.Ordinal ).ToArray( );
            return directories.Length == 0 ? fallback : directories;
        }
    }

    private sealed class AssociationLocation( string directory, string? applicationsRoot, string[] desktops )
    {
        internal string? ApplicationsRoot { get; } = applicationsRoot;
        internal IReadOnlyList<(string Path, bool DesktopSpecific)> Files { get; } = desktops
            .Select( desktop => (System.IO.Path.Combine( directory, desktop + "-mimeapps.list" ), true) )
            .Append( (System.IO.Path.Combine( directory, "mimeapps.list" ), false) ).ToArray( );
    }

    private sealed class DesktopEntry( string path, Dictionary<string, string> values, bool valid )
    {
        internal string Path { get; } = path;
        internal bool Valid { get; } = valid;
        internal string? Exec => values.GetValueOrDefault( "Exec" );
        internal string? Icon => values.GetValueOrDefault( "Icon" );
        internal bool Terminal => IsTrue( values.GetValueOrDefault( "Terminal" ) );
        internal bool DBusActivatable => IsTrue( values.GetValueOrDefault( "DBusActivatable" ) );
        internal string? WorkingDirectory => values.GetValueOrDefault( "Path" ) is { Length: > 0 } directory ? directory : null;
        internal string[] MimeTypes => (values.GetValueOrDefault( "MimeType" ) ?? "").Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

        internal string LocalizedName( string? locale )
        {
            foreach ( var suffix in LocaleSuffixes( locale ) )
            {
                if ( values.TryGetValue( "Name[" + suffix + "]", out var localized ) && localized.Length > 0 )
                {
                    return localized;
                }
            }

            return values["Name"];
        }

        private static IEnumerable<string> LocaleSuffixes( string? locale )
        {
            if ( string.IsNullOrEmpty( locale ) )
            {
                yield break;
            }

            var modifierIndex = locale.IndexOf( '@' );
            var modifier = modifierIndex >= 0 ? locale[modifierIndex..] : "";
            var withoutModifier = modifierIndex >= 0 ? locale[..modifierIndex] : locale;
            var encodingIndex = withoutModifier.IndexOf( '.' );
            var languageCountry = encodingIndex >= 0 ? withoutModifier[..encodingIndex] : withoutModifier;
            var underscore = languageCountry.IndexOf( '_' );
            var language = underscore >= 0 ? languageCountry[..underscore] : languageCountry;
            if ( modifier.Length > 0 && underscore >= 0 )
            {
                yield return languageCountry + modifier;
            }

            if ( underscore >= 0 )
            {
                yield return languageCountry;
            }

            if ( modifier.Length > 0 )
            {
                yield return language + modifier;
            }

            yield return language;
        }

        internal static DesktopEntry? Parse( string text, string path, string searchPath )
        {
            Dictionary<string, string> values = new( StringComparer.Ordinal );
            var inGroup = false;
            var sawGroup = false;
            foreach ( var raw in text.Split( '\n' ) )
            {
                var line = raw.TrimEnd( '\r' );
                if ( line.Length == 0 || line.StartsWith( '#' ) )
                {
                    continue;
                }

                if ( line.StartsWith( '[' ) )
                {
                    inGroup = line.Trim( ) == "[Desktop Entry]";
                    if ( inGroup && sawGroup )
                    {
                        return null;
                    }

                    sawGroup |= inGroup;
                    continue;
                }

                var equals = line.IndexOf( '=' );
                if ( !inGroup || equals <= 0 )
                {
                    continue;
                }

                values.TryAdd( line[..equals].Trim( ), line[(equals + 1)..].Trim( ) );
            }

            var valid = sawGroup && values.GetValueOrDefault( "Type" ) == "Application" && !IsTrue( values.GetValueOrDefault( "Hidden" ) ) &&
                values.GetValueOrDefault( "Name" ) is { Length: > 0 } &&
                (values.GetValueOrDefault( "Exec" ) is { Length: > 0 } || IsTrue( values.GetValueOrDefault( "DBusActivatable" ) )) &&
                TryExecAvailable( values.GetValueOrDefault( "TryExec" ), searchPath );
            return valid ? new( path, values, true ) : null;
        }

        private static bool TryExecAvailable( string? tryExec, string searchPath )
        {
            if ( string.IsNullOrEmpty( tryExec ) )
            {
                return true;
            }

            if ( tryExec.StartsWith( '/' ) )
            {
                return IsExecutable( tryExec );
            }

            return searchPath.Split( ':', StringSplitOptions.RemoveEmptyEntries ).Where( static directory => directory.StartsWith( '/' ) )
                .Any( directory => IsExecutable( System.IO.Path.Combine( directory, tryExec ) ) );
        }

        private static bool IsTrue( string? value ) => string.Equals( value, "true", StringComparison.OrdinalIgnoreCase );
    }
}
