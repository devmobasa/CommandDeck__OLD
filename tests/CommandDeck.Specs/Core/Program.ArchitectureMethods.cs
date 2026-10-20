using System.Xml.Linq;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ArchitectureBoundariesAreEnforced( )
    {
        var root = FindRepositoryRoot( );
        AssertProjectReferences(
            root,
            "src/CommandDeck.Core/CommandDeck.Core.csproj",
            [] );
        AssertProjectReferences(
            root,
            "src/CommandDeck.ExtensionProtocol/CommandDeck.ExtensionProtocol.csproj",
            [] );
        AssertProjectReferences(
            root,
            "src/CommandDeck.Infrastructure/CommandDeck.Infrastructure.csproj",
            ["../CommandDeck.Core/CommandDeck.Core.csproj", "../CommandDeck.ExtensionProtocol/CommandDeck.ExtensionProtocol.csproj"] );
        AssertProjectReferences(
            root,
            "src/CommandDeck.App/CommandDeck.App.csproj",
            [
                "../CommandDeck.Core/CommandDeck.Core.csproj",
                "../CommandDeck.Infrastructure/CommandDeck.Infrastructure.csproj"
            ] );

        AssertProjectReferences( root, "src/CommandDeck.Extensions/CommandDeck.Extensions.csproj", ["../CommandDeck.ExtensionProtocol/CommandDeck.ExtensionProtocol.csproj"] );
        AssertProjectReferences( root, "src/CommandDeck.ExtensionTools/CommandDeck.ExtensionTools.csproj", ["../CommandDeck.ExtensionProtocol/CommandDeck.ExtensionProtocol.csproj"] );
        AssertSourcesExclude( Path.Combine( root, "src/CommandDeck.Extensions" ), "CommandDeck.Core", "CommandDeck.Infrastructure", "CommandDeck.App", "Microsoft.Data.Sqlite", "System.Diagnostics.Process", "System.Net.Http" );

        var coreRoot = Path.Combine( root, "src/CommandDeck.Core" );
        AssertSourcesExclude(
            Path.Combine( root, "src/CommandDeck.ExtensionProtocol" ),
            "CommandDeck.Core", "CommandDeck.Infrastructure", "CommandDeck.App", "Microsoft.Data.Sqlite",
            "System.Diagnostics.Process", "System.Net.Http" );
        AssertSourcesExclude(
            coreRoot,
            "CommandDeck.Infrastructure",
            "CommandDeck.App",
            "Microsoft.Data.Sqlite",
            "System.Diagnostics.Process",
            "System.Net.Http" );

        var sourceRoot = Path.Combine( root, "src" );
        AssertSourcesExclude(
            sourceRoot,
            "Assembly.Load(",
            "AssemblyLoadContext" );
        AssertNativeLibraryOwner( root, sourceRoot );

        // Usage, invocation events and query history have one writer, which shares
        // no transaction with generation, preferences or reset.
        string[] historyWrites =
        [
            "INSERT INTO usage", "DELETE FROM usage", "UPDATE usage",
            "INSERT INTO invocation_events", "DELETE FROM invocation_events", "UPDATE invocation_events",
            "INSERT INTO query_history", "DELETE FROM query_history", "UPDATE query_history"
        ];
        var historyWriters = Directory.EnumerateFiles( sourceRoot, "*.cs", SearchOption.AllDirectories )
            .Where( IsProductionSource )
            .Where( path => historyWrites.Any( write => File.ReadAllText( path ).Contains( write, StringComparison.Ordinal ) ) )
            .Select( path => Path.GetRelativePath( root, path ) )
            .ToArray( );
        True( historyWriters is ["src/CommandDeck.Infrastructure/Storage/SqliteUsageHistoryStore.cs"],
            $"Usage history writers: {string.Join( ", ", historyWriters )}" );

        var networkOwners = Directory.EnumerateFiles(
                sourceRoot,
                "*.cs",
                SearchOption.AllDirectories )
            .Where( IsProductionSource )
            .Where( path => File.ReadAllText( path ).Contains( "HttpClient", StringComparison.Ordinal ) )
            .ToArray( );
        True( networkOwners.Length > 0 );
        string[] extensionNetworkOwners = ["src/CommandDeck.Infrastructure/Extensions/ExtensionHttpTransport.cs", "src/CommandDeck.Extensions/CommandBrokers.cs"];
        True( networkOwners.All( path => extensionNetworkOwners.Contains( Path.GetRelativePath( root, path ), StringComparer.Ordinal ) || path.Contains(
            $"{Path.DirectorySeparatorChar}Ai{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal ) ) );

        var qmlRoot = Path.Combine( root, "qml", "common" );
        AssertSourcesExclude(
            qmlRoot,
            "XMLHttpRequest",
            "WebSocket",
            "LocalStorage" );
        foreach ( var name in new[] { "CommandDeck.qml", "CommandDeckState.qml", "WindowPreview.qml" } )
        {
            var path = Path.Combine( qmlRoot, name );
            AssertSourceExcludes( path, File.ReadAllText( path ), ["Quickshell.Hyprland"] );
        }
        var omarchyHost = File.ReadAllText( Path.Combine( root, "qml/omarchy/CommandDeck.qml" ) );
        var standaloneHost = File.ReadAllText( Path.Combine( root, "qml/quickshell-standalone/StandaloneHost.qml" ) );
        True( omarchyHost.Contains( "Common.HyprlandDesktopIntegration", StringComparison.Ordinal ) );
        True( standaloneHost.Contains( "Common.HyprlandDesktopIntegration", StringComparison.Ordinal ) );

        var qmlTypes = Directory.EnumerateFiles( qmlRoot, "*.qml" )
            .Select( static path => Path.GetFileNameWithoutExtension( path ) )
            .Order( StringComparer.Ordinal )
            .ToArray( );
        var declaredQmlTypes = File.ReadLines( Path.Combine( qmlRoot, "qmldir" ) )
            .Skip( 1 )
            .Select( static line => line.Split( ' ', StringSplitOptions.RemoveEmptyEntries )[0] )
            .Order( StringComparer.Ordinal )
            .ToArray( );
        True( qmlTypes.SequenceEqual( declaredQmlTypes, StringComparer.Ordinal ),
            "qml/common/qmldir must declare every shared QML type." );
        foreach ( var extension in new[] { ".qml", ".js" } )
        {
            True( IsProductionSource( "fixture" + extension ) );
            foreach ( var token in new[] { "XMLHttpRequest", "WebSocket", "LocalStorage" } )
            {
                Throws<InvalidOperationException>( ( ) => AssertSourceExcludes( "fixture" + extension, token, [token] ) );
            }
        }
        // The host-owned connection setup may select paths; it never reads their contents in QML.
        var fileDialogs = Directory.EnumerateFiles( qmlRoot, "*.qml" ).Where( path => File.ReadAllText( path ).Contains( "FileDialog", StringComparison.Ordinal ) ).Select( Path.GetFileName ).ToArray( );
        True( fileDialogs.SequenceEqual( ["ExtensionConnectionsView.qml"] ) );
        var processOwners = Directory.EnumerateFiles( qmlRoot, "*.qml" )
            .Where( path => File.ReadAllText( path ).Contains( "Process {", StringComparison.Ordinal ) )
            .Select( static path => Path.GetFileName( path )! )
            .ToArray( );
        Equal( 1, processOwners.Length );
        Equal( "ProtocolClient.qml", processOwners[0] );
        var protocolClient = File.ReadAllText( Path.Combine( qmlRoot, processOwners[0]! ) );
        True( protocolClient.Contains( "[root.executable, \"bridge\"]", StringComparison.Ordinal ) );

        var actionRoot = Path.Combine( root, "src/CommandDeck.Infrastructure/Actions" );
        var shellOwners = Directory.EnumerateFiles( actionRoot, "*.cs" )
            .Where( path => File.ReadAllText( path ).Contains( "\"/bin/sh\"", StringComparison.Ordinal ) )
            .ToArray( );
        Equal( 1, shellOwners.Length );
        Equal( "DesktopActionHandler.cs", Path.GetFileName( shellOwners[0] ) );
        Equal( 1, CountOccurrences( File.ReadAllText( shellOwners[0] ), "\"/bin/sh\"" ) );

        AssertProgramPartLineLimits( Path.Combine( root, "src/CommandDeck.App" ) );
        AssertProgramPartLineLimits( Path.Combine( root, "tests/CommandDeck.Specs" ) );
        AssertHandwrittenLineLimits( root );

        AssertSourcesExclude(
            Path.Combine( root, "src/CommandDeck.Core/Search" ),
            "ISearchCatalog" );
        AssertSourcesExclude(
            Path.Combine( root, "src/CommandDeck.Infrastructure/Providers" ),
            "ISearchCatalog" );
        True( typeof( IndexCoordinator ).GetConstructors( ).Any( constructor =>
        {
            var parameters = constructor.GetParameters( );
            var types = parameters.Select( parameter => parameter.ParameterType ).ToArray( );
            return types.Contains( typeof( ICatalogProjectionStore ) ) &&
                types.Contains( typeof( ICatalogOperations ) ) &&
                !types.Contains( typeof( ISearchCatalog ) );
        } ), "IndexCoordinator must expose its narrow projection and maintenance dependencies." );
    }

    private static void AssertNativeLibraryOwner( string root, string sourceRoot )
    {
        const string owner = "src/CommandDeck.Infrastructure/Clipboard/Native/ClipboardWaylandInterfaces.cs";
        foreach ( var path in Directory.EnumerateFiles( sourceRoot, "*.cs", SearchOption.AllDirectories ).Where( IsProductionSource ) )
        {
            var source = File.ReadAllText( path );
            if ( Path.GetRelativePath( root, path ) == owner )
            {
                // ADR 0019: fixed system ABI metadata, never a plugin or client-selected path.
                Equal( 1, CountOccurrences( source, "NativeLibrary.Load(" ) );
                True( source.Contains( "NativeLibrary.Load( \"libwayland-client.so.0\" )", StringComparison.Ordinal ) );
            }
            else
            {
                AssertSourceExcludes( path, source, ["NativeLibrary.Load("] );
            }
        }
    }

    private static void AssertProjectReferences(
        string root,
        string projectPath,
        IReadOnlyList<string> expected )
    {
        var project = XDocument.Load( Path.Combine( root, projectPath ) );
        var actual = project.Descendants( "ProjectReference" )
            .Select( element => element.Attribute( "Include" )?.Value ?? string.Empty )
            .Order( StringComparer.Ordinal )
            .ToArray( );
        Equal(
            string.Join( '|', expected.Order( StringComparer.Ordinal ) ),
            string.Join( '|', actual ) );
    }

    private static void AssertSourcesExclude( string directory, params string[] forbidden )
    {
        foreach ( var path in Directory.EnumerateFiles(
            directory,
            "*.*",
            SearchOption.AllDirectories ).Where( IsProductionSource ) )
        {
            AssertSourceExcludes( path, File.ReadAllText( path ), forbidden );
        }
    }

    private static void AssertSourceExcludes( string path, string source, string[] forbidden )
    {
        foreach ( var token in forbidden )
        {
            if ( source.Contains( token, StringComparison.Ordinal ) )
            {
                throw new InvalidOperationException(
                    $"Forbidden architecture token '{token}' found in {path}." );
            }
        }
    }

    private static bool IsProductionSource( string path ) =>
        (path.EndsWith( ".cs", StringComparison.Ordinal ) ||
            IsUiSource( path )) &&
        !path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal ) &&
        !path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal );

    private static void AssertProgramPartLineLimits( string directory )
    {
        foreach ( var path in Directory.EnumerateFiles( directory, "Program*.cs", SearchOption.AllDirectories )
            .Where( IsProductionSource ) )
        {
            var lineCount = File.ReadLines( path ).Count( );
            if ( lineCount > 500 )
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName( path )} has {lineCount} lines; split it below 500." );
            }
        }
    }

    // AGENTS.md: handwritten source stays at most 600 lines; a file that says
    // in its first lines it is generated and not to be edited is exempt.
    private static void AssertHandwrittenLineLimits( string root )
    {
        string[] extensions = [".cs", ".qml", ".js", ".sh"];
        string[] directories = ["src", "qml", "scripts", "tests"];
        foreach ( var path in directories
            .SelectMany( directory => Directory.EnumerateFiles( Path.Combine( root, directory ), "*", SearchOption.AllDirectories ) )
            .Where( path => extensions.Contains( Path.GetExtension( path ), StringComparer.Ordinal ) && IsProductionSource( path ) ) )
        {
            var lines = File.ReadAllLines( path );
            var header = string.Join( '\n', lines.Take( 3 ) );
            var generated = header.Contains( "Generated", StringComparison.OrdinalIgnoreCase ) &&
                header.Contains( "do not edit", StringComparison.OrdinalIgnoreCase );
            if ( lines.Length > 600 && !generated )
            {
                throw new InvalidOperationException(
                    $"{Path.GetRelativePath( root, path )} has {lines.Length} lines; split it to at most 600." );
            }
        }
    }

    private static string FindRepositoryRoot( )
    {
        DirectoryInfo? current = new( Directory.GetCurrentDirectory( ) );
        while ( current is not null && !File.Exists( Path.Combine( current.FullName, "CommandDeck.slnx" ) ) )
        {
            current = current.Parent;
        }

        var result = current?.FullName;
        if ( result is null )
        {
            throw new InvalidOperationException( "Could not locate the CommandDeck repository root." );
        }

        return result;
    }

    private static int CountOccurrences( string value, string token ) =>
        value.Split( token, StringSplitOptions.None ).Length - 1;
}
