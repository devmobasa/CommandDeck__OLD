using System.Diagnostics;
using System.Text.Json.Nodes;

static class WorkspaceCaptureProbeEvidence
{
    public static async Task VerifyAsync( string directory, int count )
    {
        var log = await File.ReadAllLinesAsync( Path.Combine( directory, "grid.log" ) );
        var events = log.Where( line => line.Contains( "PROBE {", StringComparison.Ordinal ) )
            .Select( line => JsonNode.Parse( line[(line.IndexOf( "PROBE {", StringComparison.Ordinal ) + 6)..] )!.AsObject( ) ).ToArray( );
        foreach ( var name in new[] { "lock-requested", "locked", "selection-cleared", "dismissed", "teardown" } )
        {
            var item = events.Single( value => value["event"]!.GetValue<string>( ) == name );
            if ( item["count"]!.GetValue<int>( ) != 0 || item["frames"]!.AsArray( ).Count != 0 )
            {
                throw new InvalidOperationException( "Retained capture on " + name );
            }
        }

        var rendered = (await File.ReadAllLinesAsync( Path.Combine( directory, "source.log" ) ))
            .Where( line => line.Contains( "RENDERED ", StringComparison.Ordinal ) )
            .Select( line => line[(line.IndexOf( "RENDERED ", StringComparison.Ordinal ) + 9)..].Split( ' ' ) )
            .Select( parts => (Number: int.Parse( parts[0] ), Counter: int.Parse( parts[1] ), Time: long.Parse( parts[2] )) ).ToArray( );
        foreach ( var sample in new[] { "sample-1", "sample-2" } )
        {
            var item = events.Single( value => value["event"]!.GetValue<string>( ) == sample );
            var frames = item["frames"]!.AsArray( );
            if ( frames.Count != count || frames.Any( value => value!["content"]!.GetValue<bool>( ) != true ) )
            {
                throw new InvalidOperationException( "Missing native images at " + sample );
            }

            for ( var index = 0; index < count; index++ )
            {
                var frame = frames[index]!;
                var x = ( int ) (frame["x"]!.GetValue<double>( ) + frame["width"]!.GetValue<double>( ) / 2);
                var y = ( int ) (frame["y"]!.GetValue<double>( ) + frame["height"]!.GetValue<double>( ) / 4);
                var actual = await Pixel( Path.Combine( directory, "grid-" + sample[^1] + ".png" ), x, y );
                var expected = rendered.Last( value => value.Number == index + 1 && value.Time <= item["time"]!.GetValue<long>( ) );
                if ( actual.Red != expected.Counter || actual.Green != (index + 1) * 32 || actual.Blue != 0 )
                {
                    throw new InvalidOperationException( $"Wrong frame/source at {sample}, cell {index}: {actual}, expected counter {expected.Counter}." );
                }
            }
        }

        var rss = (await File.ReadAllLinesAsync( Path.Combine( directory, "rss.csv" ) )).Skip( 1 )
            .Select( line => long.Parse( line.Split( ',' )[1] ) ).ToArray( );
        var report = new JsonObject
        {
            ["windows"] = count,
            ["firstContentMilliseconds"] = events.Single( value => value["event"]!.GetValue<string>( ) == "first-content" )["elapsed"]!.DeepClone( ),
            ["peakRssBytes"] = rss.Max( ),
            ["finalRssBytes"] = rss[^1],
            ["matchedLastRenderedFrames"] = true,
            ["lifecycleCleared"] = true,
            ["gpuMemoryMeasured"] = false
        };
        await File.WriteAllTextAsync( Path.Combine( directory, "evidence.json" ), report.ToJsonString( ) );
    }

    private static async Task<(int Red, int Green, int Blue)> Pixel( string file, int x, int y )
    {
        var start = new ProcessStartInfo( "magick" ) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach ( var argument in new[] { file, "-format", $"%[pixel:p{{{x},{y}}}]", "info:" } )
        {
            start.ArgumentList.Add( argument );
        }

        using var process = Process.Start( start );
        if ( process is null )
        {
            throw new InvalidOperationException( "Could not start ImageMagick for synthetic pixel verification." );
        }

        var stdout = process.StandardOutput.ReadToEndAsync( );
        var stderr = process.StandardError.ReadToEndAsync( );
        await process.WaitForExitAsync( );
        if ( process.ExitCode != 0 )
        {
            throw new InvalidOperationException( await stderr );
        }

        var text = await stdout;
        var components = text[(text.IndexOf( '(' ) + 1)..text.IndexOf( ')' )].Split( ',' );
        return (int.Parse( components[0] ), int.Parse( components[1] ), int.Parse( components[2] ));
    }
}
