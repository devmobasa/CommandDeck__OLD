using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task SpecSelectionReportsAsync( )
    {
        var executable = Environment.ProcessPath;
        if ( executable is null )
        {
            throw new InvalidOperationException( "Cannot locate specs executable." );
        }

        string[] prefix = Path.GetFileNameWithoutExtension( executable ) == "dotnet"
            ? [typeof( Program ).Assembly.Location] : [];
        var directory = SpecTemporaryFiles.PathFor( $"commanddeck-spec-selection-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( directory );
        var originalRequirement = Environment.GetEnvironmentVariable( "COMMANDDECK_REQUIRE_NO_SKIPS" );
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        Environment.SetEnvironmentVariable( "COMMANDDECK_REQUIRE_NO_SKIPS", null );
        try
        {
            async Task<ProcessResult> Invoke( string workingDirectory, params string[] arguments ) =>
                await ProcessRunner.RunAsync( executable, [.. prefix, .. arguments], null, workingDirectory,
                    TimeSpan.FromSeconds( 30 ), CancellationToken.None );

            var root = FindRepositoryRoot( );
            var first = await Invoke( root, "--list" );
            var second = await Invoke( root, "--list" );
            Equal( 0, first.ExitCode );
            Equal( first.StandardOutput, second.StandardOutput );
            True( first.StandardOutput.Split( '\n', StringSplitOptions.RemoveEmptyEntries ).Length >= 360 );

            var output = Path.Combine( directory, "results.json" );
            var selected = await Invoke( root, "--filter", "QML constants match", "--category", "Core", "--results", output );
            Equal( 0, selected.ExitCode );
            using ( var json = JsonDocument.Parse( await File.ReadAllTextAsync( output ) ) )
            {
                Equal( 1, json.RootElement.GetProperty( "executed" ).GetInt32( ) );
                Equal( "passed", json.RootElement.GetProperty( "tests" )[0].GetProperty( "status" ).GetString( ) );
            }

            var empty = await Invoke( root, "--filter", "nonexistent-fixture-9220", "--results", output );
            Equal( 1, empty.ExitCode );
            using ( var json = JsonDocument.Parse( await File.ReadAllTextAsync( output ) ) )
            {
                Equal( 0, json.RootElement.GetProperty( "executed" ).GetInt32( ) );
            }
            Equal( 2, (await Invoke( root, "--filter" )).ExitCode );

            var storage = await Invoke( root, "--spec-storage-fixture", "--results", output );
            Equal( 1, storage.ExitCode );
            var temporary = storage.StandardOutput.Split( '\n' ).Single( line => line.StartsWith( "SPEC_TEMP path=", StringComparison.Ordinal ) );
            var paths = temporary["SPEC_TEMP path=".Length..].Split( " physical=", StringSplitOptions.None );
            True( paths[1].StartsWith( Path.Combine( root, "tmp" ) + Path.DirectorySeparatorChar, StringComparison.Ordinal ) );
            True( !Directory.Exists( paths[0] ) && !Directory.Exists( paths[1].Trim( ) ) );
            var diagnostics = storage.StandardError.Split( '\n' ).Where( line => line.StartsWith( "SPEC_STORAGE ", StringComparison.Ordinal ) ).ToArray( );
            Equal( 6, diagnostics.Length );
            True( diagnostics.All( line => line.Contains( "freeBytes=", StringComparison.Ordinal ) && line.Contains( "totalBytes=", StringComparison.Ordinal ) ) );
            using ( var json = JsonDocument.Parse( await File.ReadAllTextAsync( output ) ) )
            {
                Equal( 2, json.RootElement.GetProperty( "failed" ).GetInt32( ) );
            }

            var skipped = await Invoke( root, "--spec-reporting-fixture", "--results", output );
            Equal( 0, skipped.ExitCode );
            using ( var json = JsonDocument.Parse( await File.ReadAllTextAsync( output ) ) )
            {
                Equal( 1, json.RootElement.GetProperty( "executed" ).GetInt32( ) );
                Equal( 0, json.RootElement.GetProperty( "failed" ).GetInt32( ) );
                Equal( 2, json.RootElement.GetProperty( "skipped" ).GetInt32( ) );
                var tests = json.RootElement.GetProperty( "tests" );
                Equal( "skipped", tests[0].GetProperty( "status" ).GetString( ) );
                Equal( "fixture tool is unavailable", tests[0].GetProperty( "reason" ).GetString( ) );
                Equal( "skipped", tests[1].GetProperty( "status" ).GetString( ) );
                Equal( "fixture platform is unavailable", tests[1].GetProperty( "reason" ).GetString( ) );
                Equal( "passed", tests[2].GetProperty( "status" ).GetString( ) );
            }
            True( skipped.StandardOutput.Contains( "Skipped 2 CommandDeck specs.", StringComparison.Ordinal ) );
            Equal( 1, (await Invoke( root, "--spec-reporting-fixture", "--require-no-skips", "--results", output )).ExitCode );
            using ( var json = JsonDocument.Parse( await File.ReadAllTextAsync( output ) ) )
            {
                Equal( "skipped", json.RootElement.GetProperty( "tests" )[0].GetProperty( "status" ).GetString( ) );
                Equal( 0, json.RootElement.GetProperty( "failed" ).GetInt32( ) );
            }

            Environment.SetEnvironmentVariable( "COMMANDDECK_REQUIRE_NO_SKIPS", "1" );
            Equal( 1, (await Invoke( root, "--spec-reporting-fixture" )).ExitCode );
            Environment.SetEnvironmentVariable( "COMMANDDECK_REQUIRE_NO_SKIPS", null );

            Environment.SetEnvironmentVariable( "PATH", directory );
            var unavailable = await Invoke( root, "--spec-reporting-fixture", "--filter", "unavailable synchronous prerequisite", "--require-no-skips", "--results", output );
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Equal( 1, unavailable.ExitCode );
            using ( var json = JsonDocument.Parse( await File.ReadAllTextAsync( output ) ) )
            {
                Equal( 0, json.RootElement.GetProperty( "executed" ).GetInt32( ) );
                Equal( 1, json.RootElement.GetProperty( "skipped" ).GetInt32( ) );
                Equal( "fixture tool is unavailable", json.RootElement.GetProperty( "tests" )[0].GetProperty( "reason" ).GetString( ) );
            }

            // An empty synthetic repository makes a real guard fail without modifying production sources.
            await File.WriteAllTextAsync( Path.Combine( directory, "CommandDeck.slnx" ), "<Solution />" );
            var failed = await Invoke( directory, "--filter", "architecture boundaries", "--results", output );
            Equal( 1, failed.ExitCode );
            using var failure = JsonDocument.Parse( await File.ReadAllTextAsync( output ) );
            Equal( 1, failure.RootElement.GetProperty( "failed" ).GetInt32( ) );
            True( failure.RootElement.GetProperty( "tests" )[0].GetProperty( "error" ).GetString( )!.Contains( "Exception", StringComparison.Ordinal ) );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "COMMANDDECK_REQUIRE_NO_SKIPS", originalRequirement );
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Directory.Delete( directory, recursive: true );
        }
    }
}
