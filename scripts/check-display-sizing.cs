#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

var entry = AppContext.GetData( "EntryPointFileDirectoryPath" ) as string;
if ( entry is null )
{
    throw new InvalidOperationException( "Sizing check script directory unavailable." );
}

var repository = Path.GetFullPath( Path.Combine( entry, ".." ) );
var output = Path.Combine( repository, "build", "display-sizing-matrix" );
Directory.CreateDirectory( output );
File.Delete( Path.Combine( output, "report.json" ) );

var cases = new List<(int Width, int Height, double Dpr, int Percent, double Font, int PanelWidth, int PanelHeight)>( );
foreach ( var (width, height) in new[] { (1280, 720), (1440, 900), (1920, 1080), (2304, 1296) } )
{
    foreach ( var dpr in new[] { 1.0, 1.25, 1.666667, 2.0 } )
    {
        cases.Add( (width, height, dpr, 100, 1, 100, 100) );
    }
}
cases.AddRange( [(720, 1280, 1.25, 100, 1, 100, 100), (3440, 1440, 1, 100, 1, 100, 100),
    (1280, 720, 2, 150, 1.5, 100, 100), (2304, 1296, 1.666667, 150, 1.25, 100, 100)] );

foreach ( var (width, height) in new[] { (1280, 720), (3440, 1440) } )
{
    foreach ( var (panelWidth, panelHeight) in new[] { (80, 80), (140, 100), (100, 140), (140, 140) } )
    {
        foreach ( var dpr in new[] { 1.0, 2.0 } )
        {
            cases.Add( (width, height, dpr, 100, 1, panelWidth, panelHeight) );
        }
    }
}

string[] surfaces = ["launcher", "settings", "layout", "help", "actions", "confirmation", "extension"];
Dictionary<string, JsonElement> launcherLayouts = new( StringComparer.Ordinal );
Dictionary<string, string> logicalMetrics = new( StringComparer.Ordinal );
const string metricsPrefix = "Saved sizing proof ";
foreach ( var scenario in cases )
{
    var name = FormattableString.Invariant( $"{scenario.Width}x{scenario.Height}-dpr{scenario.Dpr}-zoom{scenario.Percent}-font{scenario.Font}-panel{scenario.PanelWidth}x{scenario.PanelHeight}" );
    Console.WriteLine( name );
    var directory = Path.Combine( output, name );
    ProcessStartInfo start = new( "dotnet" )
    {
        WorkingDirectory = repository,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    foreach ( var argument in new[] { "scripts/capture-gallery.cs", "sizing", "--width", Number( scenario.Width ),
        "--height", Number( scenario.Height ), "--dpr", Number( scenario.Dpr ), "--percent", Number( scenario.Percent ),
        "--font-scale", Number( scenario.Font ), "--panel-width", Number( scenario.PanelWidth ),
        "--panel-height", Number( scenario.PanelHeight ), "--output", directory } )
    {
        start.ArgumentList.Add( argument );
    }

    using var process = Process.Start( start );
    if ( process is null )
    {
        throw new InvalidOperationException( "Gallery sizing renderer did not start." );
    }

    var stdout = process.StandardOutput.ReadToEndAsync( );
    var stderr = process.StandardError.ReadToEndAsync( );
    await process.WaitForExitAsync( );
    var log = await stdout + await stderr;
    await File.WriteAllTextAsync( Path.Combine( output, name + ".log" ), log );
    if ( process.ExitCode != 0 )
    {
        Console.Error.WriteLine( log );
        return process.ExitCode;
    }

    // Compare measurements from the rendered production window and theme,
    // rather than passing unused DPR fields to the pure sizing function.
    var metrics = log.Split( '\n' )
        .Where( line => line.Contains( metricsPrefix, StringComparison.Ordinal ) )
        .Select( line => line[(line.IndexOf( metricsPrefix, StringComparison.Ordinal ) + metricsPrefix.Length)..].Trim( ) )
        .Order( StringComparer.Ordinal ).ToArray( );
    if ( metrics.Length != surfaces.Length )
    {
        throw new InvalidOperationException( $"{name}: missing rendered logical measurements." );
    }

    var key = FormattableString.Invariant( $"{scenario.Width}x{scenario.Height}-zoom{scenario.Percent}-font{scenario.Font}-panel{scenario.PanelWidth}x{scenario.PanelHeight}" );
    var measurement = string.Join( '\n', metrics );
    if ( logicalMetrics.TryGetValue( key, out var baseline ) && measurement != baseline )
    {
        throw new InvalidOperationException( $"{name}: logical layout or text changed with raster density.\n{measurement}\nExpected:\n{baseline}" );
    }
    logicalMetrics[key] = measurement;

    var baselineKey = FormattableString.Invariant( $"{scenario.Width}x{scenario.Height}-zoom{scenario.Percent}-font{scenario.Font}" );
    SizingRenderChecks.CheckLauncherLayout( log, baselineKey, scenario.PanelWidth, scenario.PanelHeight, launcherLayouts );

    // The PNG dimensions also prove the requested DPR actually reached Qt.
    foreach ( var surface in surfaces )
    {
        await using var image = File.OpenRead( Path.Combine( directory, surface + ".png" ) );
        byte[] header = new byte[24];
        await image.ReadExactlyAsync( header );
        var width = BinaryPrimitives.ReadInt32BigEndian( header.AsSpan( 16, 4 ) );
        var height = BinaryPrimitives.ReadInt32BigEndian( header.AsSpan( 20, 4 ) );
        if ( Math.Abs( width - scenario.Width * scenario.Dpr ) > 1 ||
            Math.Abs( height - scenario.Height * scenario.Dpr ) > 1 )
        {
            throw new InvalidOperationException( $"{name}/{surface}: raster size {width}x{height} does not match the requested DPR." );
        }
    }
}

await File.WriteAllTextAsync( Path.Combine( output, "report.json" ), JsonSerializer.Serialize( new
{
    cases = cases.Count,
    renders = cases.Count * surfaces.Length,
    passed = true,
    logicalMetricsEqualAcrossDpr = true,
    rasterDensityVerified = true,
    surfaces
}, new JsonSerializerOptions { WriteIndented = true } ) );
Console.WriteLine( $"Sizing render matrix passed: {cases.Count} configurations, {cases.Count * surfaces.Length} renders." );
return 0;

static string Number( double value ) => value.ToString( CultureInfo.InvariantCulture );

static class SizingRenderChecks
{
    public static void CheckLauncherLayout( string log, string baselineKey, int panelWidth, int panelHeight,
        Dictionary<string, JsonElement> launcherLayouts )
    {
        const string layoutPrefix = "Saved launcher layout ";
        var layoutLine = log.Split( '\n' ).Single( line => line.Contains( layoutPrefix, StringComparison.Ordinal ) );
        using var layout = JsonDocument.Parse( layoutLine[(layoutLine.IndexOf( layoutPrefix, StringComparison.Ordinal ) + layoutPrefix.Length)..] );
        var current = layout.RootElement;
        if ( panelWidth == 100 && panelHeight == 100 )
        {
            launcherLayouts[baselineKey] = current.Clone( );
        }
        else if ( launcherLayouts.TryGetValue( baselineKey, out var normal ) )
        {
            if ( current.GetProperty( "text" ).GetInt32( ) != normal.GetProperty( "text" ).GetInt32( ) ||
                current.GetProperty( "row" ).GetInt32( ) != normal.GetProperty( "row" ).GetInt32( ) )
            {
                throw new InvalidOperationException( $"{baselineKey}: panel choices changed text or row size." );
            }

            if ( panelWidth > 100 && current.GetProperty( "preview" ).GetDouble( ) <= normal.GetProperty( "preview" ).GetDouble( ) ||
                panelHeight > 100 && current.GetProperty( "rows" ).GetInt32( ) <= normal.GetProperty( "rows" ).GetInt32( ) )
            {
                throw new InvalidOperationException( $"{baselineKey}: extra panel space did not expand the preview or visible rows." );
            }
        }
    }
}
