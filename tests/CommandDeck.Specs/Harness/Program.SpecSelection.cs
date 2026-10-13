using System.Diagnostics;
using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static bool _listSpecs;
    private static bool _requireNoSkips;
    private static int _skipped;
    private static string? _specFilter;
    private static string? _specCategory;
    private static string? _specOutput;
    private sealed record SpecResult( string Name, string Category, double Milliseconds, string Status, string? Error, string? Reason );
    private static readonly List<SpecResult> SpecResults = [];
    private static readonly List<string> SelectedSpecNames = [];

    private static bool ConfigureSpecSelection( string[] args )
    {
        _listSpecs = args.Contains( "--list", StringComparer.Ordinal );
        _requireNoSkips = args.Contains( "--require-no-skips", StringComparer.Ordinal ) ||
            Environment.GetEnvironmentVariable( "COMMANDDECK_REQUIRE_NO_SKIPS" ) == "1";
        foreach ( var option in new[] { "--filter", "--category", "--results" } )
        {
            if ( args.Contains( option, StringComparer.Ordinal ) &&
                (ReadArgumentValue( args, option ) is not { Length: > 0 } value || value.StartsWith( "--", StringComparison.Ordinal )) )
            {
                Console.Error.WriteLine( $"{option} requires a value." );
                return false;
            }
        }

        _specFilter = ReadArgumentValue( args, "--filter" );
        _specCategory = ReadArgumentValue( args, "--category" );
        _specOutput = ReadArgumentValue( args, "--results" );
        return true;
    }

    private static string SpecCategory( string registration ) =>
        Path.GetFileNameWithoutExtension( registration ).Replace( "Program.", "", StringComparison.Ordinal )
            .Replace( "Registrations", "", StringComparison.Ordinal );

    private static bool SelectSpec( string name, string registration )
    {
        if ( (_specFilter is not null && !name.Contains( _specFilter, StringComparison.OrdinalIgnoreCase )) ||
            (_specCategory is not null && !SpecCategory( registration ).Equals( _specCategory, StringComparison.OrdinalIgnoreCase )) )
        {
            return false;
        }

        SelectedSpecNames.Add( $"{SpecCategory( registration )}: {name}" );
        return !_listSpecs;
    }

    private static void RecordSpec( string name, string registration, long started, Exception? error ) =>
        SpecResults.Add( new( name, SpecCategory( registration ), Stopwatch.GetElapsedTime( started ).TotalMilliseconds,
            error is null ? "passed" : "failed", error?.ToString( ), null ) );

    private static void RecordSkippedSpec( string name, string registration, long started, SpecSkippedException error )
    {
        _executed--;
        _skipped++;
        Console.WriteLine( $"SKIP {name}: {error.Message}" );
        SpecResults.Add( new( name, SpecCategory( registration ), Stopwatch.GetElapsedTime( started ).TotalMilliseconds,
            "skipped", null, error.Message ) );
    }

    private static int FinishSpecs( )
    {
        if ( !_listSpecs && _skipped > 0 )
        {
            Console.WriteLine( $"Skipped {_skipped} CommandDeck specs." );
            if ( _requireNoSkips )
            {
                Console.Error.WriteLine( "This validation profile requires every selected spec prerequisite." );
            }
        }

        if ( _listSpecs )
        {
            foreach ( var name in SelectedSpecNames.Order( StringComparer.Ordinal ) )
            {
                Console.WriteLine( name );
            }
        }

        if ( _specOutput is not null )
        {
            using var stream = File.Create( _specOutput );
            using Utf8JsonWriter json = new( stream, new JsonWriterOptions { Indented = true } );
            json.WriteStartObject( );
            json.WriteNumber( "executed", _executed );
            json.WriteNumber( "failed", _failed );
            json.WriteNumber( "skipped", _skipped );
            json.WriteStartArray( "tests" );
            foreach ( var result in SpecResults )
            {
                json.WriteStartObject( );
                json.WriteString( "name", result.Name );
                json.WriteString( "category", result.Category );
                json.WriteString( "status", result.Status );
                json.WriteNumber( "durationMilliseconds", result.Milliseconds );
                json.WriteString( "error", result.Error );
                json.WriteString( "reason", result.Reason );
                json.WriteEndObject( );
            }
            json.WriteEndArray( );
            json.WriteEndObject( );
        }

        if ( SelectedSpecNames.Count == 0 )
        {
            Console.Error.WriteLine( "No specs matched the selection." );
            return 1;
        }

        return _failed == 0 && (!_requireNoSkips || _skipped == 0) ? 0 : 1;
    }
}
