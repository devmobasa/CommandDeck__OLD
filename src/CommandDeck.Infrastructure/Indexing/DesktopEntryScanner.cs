using System.Runtime.CompilerServices;
using System.Text;

namespace CommandDeck;

public static class DesktopEntryScanner
{
    public static async IAsyncEnumerable<IndexedItem> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken )
    {
        HashSet<string> seen = new( StringComparer.Ordinal );
        foreach ( var directory in GetApplicationDirectories( ) )
        {
            if ( !Directory.Exists( directory ) )
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(
                    directory,
                    "*.desktop",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        ReturnSpecialDirectories = false
                    } );
            }
            catch ( UnauthorizedAccessException )
            {
                continue;
            }

            foreach ( var file in files )
            {
                cancellationToken.ThrowIfCancellationRequested( );
                var entry = await ParseAsync(
                    file,
                    directory,
                    cancellationToken ).ConfigureAwait( false );
                if ( entry is null || !seen.Add( entry.DesktopId ) || !entry.IsVisible )
                {
                    continue;
                }

                yield return new IndexedItem
                {
                    StableId = $"app:{entry.DesktopId}",
                    Kind = ResultKinds.Application,
                    Title = entry.Name,
                    Subtitle = entry.Comment ?? entry.GenericName,
                    Path = file,
                    Icon = entry.Icon ?? "application-x-executable",
                    StartupWmClass = entry.StartupWmClass,
                    Body = string.Join(
                        ' ',
                        entry.Keywords
                            .Append( entry.Exec ?? string.Empty )
                            .Append( entry.StartupWmClass ?? string.Empty ) ),
                    PayloadJson = PayloadWriter.Create(
                        ("desktopFile", file),
                        ("desktopId", entry.DesktopId),
                        ("exec", entry.Exec),
                        ("icon", entry.Icon),
                        ("startupWmClass", entry.StartupWmClass),
                        ("mimeTypes", string.Join( ';', entry.MimeTypes )),
                        ("terminal", entry.Terminal ? "true" : "false") ),
                    ProviderId = ProviderIds.Applications
                    ,
                    SourceId = SourceIds.Applications
                };
            }
        }
    }

    private static IEnumerable<string> GetApplicationDirectories( )
    {
        var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
        var dataHome = Environment.GetEnvironmentVariable( "XDG_DATA_HOME" )
                       ?? Path.Combine( home, ".local", "share" );
        yield return Path.Combine( dataHome, "applications" );

        var dataDirectories = Environment.GetEnvironmentVariable( "XDG_DATA_DIRS" )
                              ?? "/usr/local/share:/usr/share";
        foreach ( var directory in dataDirectories.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries ) )
        {
            yield return Path.Combine( directory, "applications" );
        }
    }

    private static async ValueTask<DesktopEntry?> ParseAsync(
        string path,
        string applicationsDirectory,
        CancellationToken cancellationToken )
    {
        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync( path, Encoding.UTF8, cancellationToken )
                .ConfigureAwait( false );
        }
        catch ( Exception exception ) when (
            exception is UnauthorizedAccessException or IOException )
        {
            return null;
        }

        var inDesktopEntry = false;
        Dictionary<string, string> values = new( StringComparer.Ordinal );
        foreach ( var raw in lines )
        {
            var line = raw.Trim( );
            if ( line.StartsWith( '[' ) )
            {
                inDesktopEntry = line.Equals( "[Desktop Entry]", StringComparison.Ordinal );
                continue;
            }

            if ( !inDesktopEntry || line.Length == 0 || line.StartsWith( '#' ) )
            {
                continue;
            }

            var equals = line.IndexOf( '=' );
            if ( equals <= 0 )
            {
                continue;
            }

            var key = line[..equals];
            if ( key.Contains( '[', StringComparison.Ordinal ) )
            {
                continue;
            }

            values[key] = line[(equals + 1)..];
        }

        var desktopId = Path.GetRelativePath(
                applicationsDirectory,
                path )
            .Replace( Path.DirectorySeparatorChar, '-' );
        values.TryGetValue( "Name", out var name );
        var exec = values.GetValueOrDefault( "Exec" );

        return new DesktopEntry
        {
            DesktopId = desktopId,
            Name = name ?? desktopId[..^".desktop".Length],
            GenericName = values.GetValueOrDefault( "GenericName" ),
            Comment = values.GetValueOrDefault( "Comment" ),
            Icon = values.GetValueOrDefault( "Icon" ),
            StartupWmClass = values.GetValueOrDefault( "StartupWMClass" ),
            Exec = exec,
            Terminal = IsTrue( values.GetValueOrDefault( "Terminal" ) ),
            IsVisible = !string.IsNullOrWhiteSpace( name ) &&
                !string.IsNullOrWhiteSpace( exec ) &&
                string.Equals(
                    values.GetValueOrDefault( "Type", "Application" ),
                    "Application",
                    StringComparison.Ordinal ) &&
                !IsTrue( values.GetValueOrDefault( "Hidden" ) ) &&
                !IsTrue( values.GetValueOrDefault( "NoDisplay" ) ) &&
                IsVisibleOnCurrentDesktop( values ) &&
                IsTryExecAvailable( values.GetValueOrDefault( "TryExec" ) ),
            Keywords = values.GetValueOrDefault( "Keywords", string.Empty )
                .Split( ';', StringSplitOptions.RemoveEmptyEntries ),
            MimeTypes = values.GetValueOrDefault( "MimeType", string.Empty )
                .Split( ';', StringSplitOptions.RemoveEmptyEntries )
        };
    }

    private static bool IsVisibleOnCurrentDesktop( IReadOnlyDictionary<string, string> values )
    {
        HashSet<string> currentDesktops = new( StringComparer.OrdinalIgnoreCase );
        AddDesktopNames( currentDesktops, Environment.GetEnvironmentVariable( "XDG_CURRENT_DESKTOP" ) );
        AddDesktopNames( currentDesktops, Environment.GetEnvironmentVariable( "DESKTOP_SESSION" ) );

        var onlyShowIn = SplitList( values.GetValueOrDefault( "OnlyShowIn" ) );
        if ( onlyShowIn.Length > 0 && !onlyShowIn.Any( currentDesktops.Contains ) )
        {
            return false;
        }

        var notShowIn = SplitList( values.GetValueOrDefault( "NotShowIn" ) );
        return !notShowIn.Any( currentDesktops.Contains );
    }

    private static void AddDesktopNames( HashSet<string> names, string? value )
    {
        if ( string.IsNullOrWhiteSpace( value ) )
        {
            return;
        }

        foreach ( var name in value.Split(
            [':', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
        {
            names.Add( name );
        }
    }

    private static string[] SplitList( string? value ) =>
        string.IsNullOrWhiteSpace( value )
            ? []
            : value.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

    private static bool IsTryExecAvailable( string? tryExec ) =>
        string.IsNullOrWhiteSpace( tryExec ) || ProcessRunner.ExistsOnPath( tryExec );

    private static bool IsTrue( string? value ) =>
        string.Equals( value, "true", StringComparison.OrdinalIgnoreCase );

    private sealed class DesktopEntry
    {
        public required string DesktopId { get; init; }
        public required string Name { get; init; }
        public string? GenericName { get; init; }
        public string? Comment { get; init; }
        public string? Icon { get; init; }
        public string? StartupWmClass { get; init; }
        public string? Exec { get; init; }
        public bool Terminal { get; init; }
        public bool IsVisible { get; init; }
        public IReadOnlyList<string> Keywords { get; init; } = [];
        public IReadOnlyList<string> MimeTypes { get; init; } = [];
    }
}
