using System.Text;
using System.Text.RegularExpressions;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task PickerReturnsOnlyTheSelectedValueAsync( )
    {
        var parsed = PickerSession.Parse( Encoding.UTF8.GetBytes( "first\tOne\r\nsecond value\tTwo\n" ), "Pick", true );
        Equal( 2, parsed.Choices.Count );
        Equal( "second value", parsed.Choices[1].Value );
        Equal( "Two", parsed.Choices[1].Label );
        Throws<InvalidOperationException>( ( ) => PickerSession.Parse( new byte[1024 * 1024 + 1], "Pick", false ) );
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-picker-test-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            var qs = Path.Combine( directory, "qs" );
            await File.WriteAllTextAsync( qs, "#!/usr/bin/python3\nimport os, json, stat\np=os.environ['COMMANDDECK_PICKER_INPUT']\nassert stat.S_IMODE(os.stat(p).st_mode)==0o600\nassert stat.S_IMODE(os.stat(os.path.dirname(p)).st_mode)==0o700\nassert json.load(open(p))['choices'][1]['label']=='Two'\nopen(os.environ['COMMANDDECK_PICKER_OUTPUT'],'w').write('1')\nprint('UI logs must not reach stdout')\n" );
            File.SetUnixFileMode( qs, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( "PATH", directory + Path.PathSeparator + originalPath );
            var binary = Path.Combine( FindRepositoryRoot( ), "src/CommandDeck.App/bin/Release/net11.0/linux-x64/commanddeck" );
            var result = await ProcessRunner.RunAsync( binary, ["pick", "--label-tab"], "first\tOne\nsecond value\tTwo\n", null, TimeSpan.FromSeconds( 10 ), default );
            Equal( 0, result.ExitCode );
            Equal( "second value\n", result.StandardOutput );
            var index = await ProcessRunner.RunAsync( binary, ["pick", "--label-tab", "--index"], "first\tOne\nsecond value\tTwo\n", null, TimeSpan.FromSeconds( 10 ), default );
            Equal( "1\n", index.StandardOutput );
            await File.WriteAllTextAsync( qs, "#!/bin/sh\nexit 1\n" );
            var cancelled = await ProcessRunner.RunAsync( binary, ["pick"], "first\nsecond\n", null, TimeSpan.FromSeconds( 10 ), default );
            Equal( 1, cancelled.ExitCode );
            Equal( "", cancelled.StandardOutput );
            await File.WriteAllTextAsync( qs, "#!/bin/sh\nprintf 999 > \"$COMMANDDECK_PICKER_OUTPUT\"\n" );
            var invalid = await ProcessRunner.RunAsync( binary, ["pick"], "first\nsecond\n", null, TimeSpan.FromSeconds( 10 ), default );
            Equal( 2, invalid.ExitCode );
            Equal( "", invalid.StandardOutput );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( directory, recursive: true );
        }
    }

    // Quickshell loads the picker from its own directory, so every relative
    // import and same-directory component must be delivered beside it.
    private static async Task PickerDeliveryCarriesEveryRelativeDependencyAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var built = Path.Combine( AppContext.BaseDirectory, "picker" );
        var delivered = PickerAssetNames( built );
        True( delivered.Contains( "shell.qml" ), "the build delivers the picker entry point" );
        AssertPickerDependenciesDelivered( built );

        var root = SpecTemporaryFiles.CreateDirectory( "commanddeck-picker-install-" ).FullName;
        try
        {
            var scripts = Path.Combine( root, "scripts" );
            var publish = Path.Combine( root, "artifacts", "publish", "linux-x64" );
            var installed = Path.Combine( root, "data", "commanddeck", "picker" );
            Directory.CreateDirectory( scripts );
            Directory.CreateDirectory( Path.Combine( publish, "picker" ) );
            Directory.CreateDirectory( installed );
            File.Copy( Path.Combine( FindRepositoryRoot( ), "scripts", "install-local.sh" ), Path.Combine( scripts, "install-local.sh" ) );
            var binary = Path.Combine( publish, "commanddeck" );
            await File.WriteAllTextAsync( binary, "#!/bin/sh\n" );
            File.SetUnixFileMode( binary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            foreach ( var asset in Directory.EnumerateFiles( built ) )
            {
                File.Copy( asset, Path.Combine( publish, "picker", Path.GetFileName( asset ) ) );
            }
            await File.WriteAllTextAsync( Path.Combine( installed, "Retired.qml" ), "" );

            var result = await ProcessRunner.RunAsync( "env",
                ["HOME=" + Path.Combine( root, "home" ), "XDG_DATA_HOME=" + Path.Combine( root, "data" ),
                    "bash", Path.Combine( scripts, "install-local.sh" ), "linux-x64"],
                null, root, TimeSpan.FromSeconds( 10 ), default );
            True( result.ExitCode == 0, "install-local.sh failed: " + result.StandardError );

            Equal( string.Join( ",", delivered ), string.Join( ",", PickerAssetNames( installed ) ) );
            AssertPickerDependenciesDelivered( installed );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static List<string> PickerAssetNames( string directory )
    {
        True( Directory.Exists( directory ), "picker directory is missing: " + directory );

        return [.. Directory.EnumerateFiles( directory ).Select( Path.GetFileName ).OfType<string>( ).Order( StringComparer.Ordinal )];
    }

    private static void AssertPickerDependenciesDelivered( string directory )
    {
        Regex relativeImport = new( @"^\s*\.?import\s+""(?<path>[^""]+)""", RegexOptions.Multiline );
        Regex objectDeclaration = new( @"\b(?<type>[A-Z][A-Za-z0-9_]*)\s*\{" );
        var components = Directory.EnumerateFiles( Path.Combine( FindRepositoryRoot( ), "qml", "common" ), "*.qml" )
            .Select( Path.GetFileNameWithoutExtension )
            .OfType<string>( )
            .ToHashSet( StringComparer.Ordinal );

        foreach ( var file in Directory.EnumerateFiles( directory ) )
        {
            var source = File.ReadAllText( file );
            foreach ( Match match in relativeImport.Matches( source ) )
            {
                var dependency = match.Groups["path"].Value;
                True( File.Exists( Path.Combine( directory, dependency ) ),
                    $"{Path.GetFileName( file )} imports {dependency}, which the picker does not deliver" );
            }

            if ( !file.EndsWith( ".qml", StringComparison.Ordinal ) )
            {
                continue;
            }

            var code = source.Split( '\n' ).Where( line => !line.TrimStart( ).StartsWith( "//", StringComparison.Ordinal ) );
            foreach ( Match match in objectDeclaration.Matches( string.Join( '\n', code ) ) )
            {
                var type = match.Groups["type"].Value;
                True( !components.Contains( type ) || File.Exists( Path.Combine( directory, type + ".qml" ) ),
                    $"{Path.GetFileName( file )} uses {type}, which the picker does not deliver" );
            }
        }
    }
}
