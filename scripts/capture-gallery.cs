#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

if ( !OperatingSystem.IsLinux( ) )
{
    Console.Error.WriteLine( "Gallery capture requires Linux." );
    return 2;
}

return await new GalleryCapture( ).RunAsync( args );

// Renders the documentation gallery from tests/CommandDeck.Gallery scenes:
// shared production QML, demonstration data, an isolated offscreen session,
// and no daemon, provider, account, clipboard, or desktop access.
[SupportedOSPlatform( "linux" )]
sealed partial class GalleryCapture
{
    private const string Usage = "Usage: dotnet scripts/capture-gallery.cs [scene...] [--output DIR] [--width PX --height PX --dpr N --percent N --font-scale N --panel-width N --panel-height N]";
    private const string RequestPreview =
        """{"model":"demo-model","stream":true,"messages":[{"role":"system","content":"Answer briefly."},{"role":"user","content":"Explain why the Moon has phases."}]}""";

    // The four palettes in the theme collage, in reading order.
    private static readonly string[] CollageThemes = ["tokyo-night", "everforest", "gruvbox", "rose-pine"];

    // Searched in order; Yaru is Omarchy's default icon theme family.
    private static readonly string[] IconThemes = ["Yaru-blue", "Yaru", "breeze-dark", "Adwaita", "hicolor"];

    private static readonly Scene[] Scenes =
    [
        new( "search", "search.qml", 3, ["applications.png", "search.png", "calculator.png", "prefixes.png"] ),
        new( "desktop", "desktop.qml", 3,
            ["overview.png", "config.png", "windows.png", "omarchy.png", "herdr.png", "actions.png", "help.png", "time.png", "emoji.png"] ),
        new( "workflows", "workflows.qml", 3,
            ["project-details.png", "project.png", "clipboard.png", "snippet-form.png", "snippet-preview.png",
                "file-selection.png", "ai-preflight.png"] ),
        new( "assistant", "assistant.qml", 3, ["ai-answer.png", "ai-conversation.png", "ai-history.png", "ai-science.png"] ),
        new( "themes", "themes.qml", 3, ["themes.png"] ),
        new( "demo", "demo.qml", 2, ["demo.gif", "demo.mp4"] ),
        new( "whats-new", "whats-new.qml", 2, ["whats-new.gif", "whats-new.mp4"] ),
        new( "settings", "settings.qml", 3,
            ["settings-display.png", "settings-layout.png", "settings-search.png", "settings-folders.png", "settings-quicklinks.png", "settings-quicklink-edit.png"] ),
        new( "social", "social.qml", 2, ["social-preview.png"] ),
        new( "sizing", "sizing.qml", 1, ["launcher.png", "settings.png", "layout.png", "help.png", "actions.png", "confirmation.png", "extension.png"] )
    ];

    private string _repository = "";
    private string _gallery = "";
    private string _output = "";
    private readonly Dictionary<string, double> _sizing = new( StringComparer.Ordinal );

    public async Task<int> RunAsync( string[] arguments )
    {
        var entry = AppContext.GetData( "EntryPointFileDirectoryPath" ) as string;
        if ( entry is null )
        {
            throw new InvalidOperationException( "Capture script directory unavailable." );
        }

        _repository = Path.GetFullPath( Path.Combine( entry, ".." ) );
        _gallery = Path.Combine( _repository, "tests", "CommandDeck.Gallery" );
        _output = Path.Combine( _repository, "docs", "screenshots" );

        var selected = new List<Scene>( );
        for ( var index = 0; index < arguments.Length; index++ )
        {
            if ( arguments[index] == "--output" && index + 1 < arguments.Length )
            {
                _output = Path.GetFullPath( arguments[++index] );
                continue;
            }

            if ( arguments[index] is "--width" or "--height" or "--dpr" or "--percent" or "--font-scale" or "--panel-width" or "--panel-height" )
            {
                var key = arguments[index];
                if ( ++index >= arguments.Length || !TrySetSizing( key, arguments[index] ) )
                {
                    Console.Error.WriteLine( Usage );
                    return 2;
                }
                continue;
            }

            var scene = Scenes.FirstOrDefault( value => value.Name == arguments[index] );
            if ( scene is null )
            {
                Console.Error.WriteLine( Usage );
                Console.Error.WriteLine( "Scenes: " + string.Join( ", ", Scenes.Select( value => value.Name ) ) );
                return 2;
            }

            selected.Add( scene );
        }

        foreach ( var tool in new[] { "qs", "ffmpeg" } )
        {
            if ( !OnPath( tool ) )
            {
                Console.Error.WriteLine( $"Gallery capture requires {tool} on PATH." );
                return 2;
            }
        }

        Directory.CreateDirectory( _output );
        foreach ( var scene in selected.Count > 0 ? selected : [.. Scenes.Where( value => value.Name != "sizing" )] )
        {
            await CaptureAsync( scene );
        }

        return 0;
    }

    private bool TrySetSizing( string key, string text )
    {
        if ( !double.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) ||
            !double.IsFinite( value ) || value <= 0 )
        {
            return false;
        }

        var valid = key switch
        {
            "--width" or "--height" => value <= 7680 && value == Math.Round( value ),
            "--dpr" => value <= 3,
            "--font-scale" => value <= 2,
            "--panel-width" or "--panel-height" => value >= 80 && value <= 140 && value % 5 == 0,
            "--percent" => value >= 75 && value <= 150 && value % 5 == 0,
            _ => false
        };
        if ( valid )
        {
            _sizing[key[2..]] = value;
        }
        return valid;
    }

    private async Task CaptureAsync( Scene scene )
    {
        Console.WriteLine( $"Rendering {scene.Name}" );
        var frames = Directory.CreateTempSubdirectory( "commanddeck-gallery-frames-" ).FullName;
        try
        {
            switch ( scene.Name )
            {
                case "themes":
                    foreach ( var theme in CollageThemes )
                    {
                        await RenderAsync( scene, frames, theme );
                    }

                    await FfmpegAsync( CollageArguments( frames, "themes.png" ) );
                    break;
                case "demo":
                case "whats-new":
                    await RenderAsync( scene, frames, theme: null );
                    await EncodeAnimationAsync( frames, scene.Name );
                    break;
                default:
                    await RenderAsync( scene, _output, theme: null );
                    break;
            }

            foreach ( var name in scene.Outputs )
            {
                if ( !File.Exists( Path.Combine( _output, name ) ) )
                {
                    throw new InvalidOperationException( $"Gallery output missing: {name}" );
                }
            }
        }
        finally
        {
            Directory.Delete( frames, recursive: true );
        }
    }

    private async Task RenderAsync( Scene scene, string output, string? theme )
    {
        var fixture = Directory.CreateTempSubdirectory( "commanddeck-gallery-" ).FullName;
        try
        {
            var palette = theme is null ? null : LoadPalette( theme );
            await PrepareFixtureAsync( fixture, scene );
            ProcessStartInfo start = new( "qs" ) { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add( "--no-color" );
            start.ArgumentList.Add( "-p" );
            start.ArgumentList.Add( fixture );
            Isolate( start, fixture );
            start.Environment["QT_SCALE_FACTOR"] = _sizing.GetValueOrDefault( "dpr", scene.Scale ).ToString( CultureInfo.InvariantCulture );
            start.Environment["COMMANDDECK_GALLERY_SIZING"] = JsonSerializer.Serialize( _sizing );
            start.Environment["COMMANDDECK_SCREENSHOT_OUTPUT"] = output;
            start.Environment["COMMANDDECK_DEMO_IMAGE"] = new Uri( Path.Combine( fixture, "demo-image.png" ) ).AbsoluteUri;
            start.Environment["COMMANDDECK_PREVIEW_JSON"] = RequestPreview;
            start.Environment["COMMANDDECK_PREVIEW_DIGEST"] = Convert.ToHexStringLower( SHA256.HashData( Encoding.UTF8.GetBytes( RequestPreview ) ) );
            start.Environment["COMMANDDECK_GALLERY_ICONS"] = ResolveIcons( palette?["icons"]?.GetValue<string>( ) ).ToJsonString( );
            if ( palette is not null )
            {
                start.Environment["COMMANDDECK_GALLERY_THEME"] = palette.ToJsonString( );
            }

            await RunAsync( start, scene.Name is "demo" or "whats-new" ? TimeSpan.FromMinutes( 3 ) : TimeSpan.FromSeconds( 60 ) );
        }
        finally
        {
            Directory.Delete( fixture, recursive: true );
        }
    }

    private async Task PrepareFixtureAsync( string fixture, Scene scene )
    {
        var components = Directory.CreateDirectory( Path.Combine( fixture, "components" ) ).FullName;
        foreach ( var file in Directory.EnumerateFiles( Path.Combine( _repository, "qml", "common" ) ) )
        {
            File.Copy( file, Path.Combine( components, Path.GetFileName( file ) ) );
        }

        // Qt's offscreen platform has no layer-shell PanelWindow backend. The
        // per-screen window factory is unused in testMode, so the capture copy
        // omits it and the stage hosts the shared content in a plain Window.
        var hostPath = Path.Combine( components, "CommandDeck.qml" );
        var host = await File.ReadAllTextAsync( hostPath );
        var factory = host.LastIndexOf( "  Variants {", StringComparison.Ordinal );
        if ( factory < 0 )
        {
            throw new InvalidOperationException( "Native window factory not found in CommandDeck.qml." );
        }

        await File.WriteAllTextAsync( hostPath, host[..factory] + "}\n" );
        foreach ( var file in Directory.EnumerateFiles( _gallery ).Where( file => file.EndsWith( ".qml", StringComparison.Ordinal ) || file.EndsWith( ".js", StringComparison.Ordinal ) ) )
        {
            File.Copy( file, Path.Combine( fixture, Path.GetFileName( file ) ) );
        }

        File.Copy( Path.Combine( _gallery, scene.File ), Path.Combine( fixture, "shell.qml" ) );

        // The clipboard scene previews an earlier gallery image as its copied picture.
        var image = Path.Combine( _output, "calculator.png" );
        File.Copy( File.Exists( image ) ? image : Path.Combine( _repository, "docs", "screenshots", "calculator.png" ), Path.Combine( fixture, "demo-image.png" ) );
    }

    private static void Isolate( ProcessStartInfo start, string fixture )
    {
        start.Environment["QT_QPA_PLATFORM"] = "offscreen";
        start.Environment["QT_QUICK_BACKEND"] = "software";
        start.Environment["QT_QPA_PLATFORMTHEME"] = "generic";
        start.Environment["QT_QUICK_CONTROLS_STYLE"] = "Basic";
        start.Environment.Remove( "DISPLAY" );
        start.Environment.Remove( "WAYLAND_DISPLAY" );
        start.Environment.Remove( "HYPRLAND_INSTANCE_SIGNATURE" );
        start.Environment["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=" + Path.Combine( fixture, "no-session-bus" );
        foreach ( var key in new[] { "XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME", "XDG_DATA_HOME", "XDG_RUNTIME_DIR" } )
        {
            var directory = Path.Combine( fixture, key.ToLowerInvariant( ) );
            Directory.CreateDirectory( directory );
            File.SetUnixFileMode( directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            start.Environment[key] = directory;
        }
    }

    private JsonObject LoadPalette( string theme )
    {
        var themes = JsonNode.Parse( File.ReadAllText( Path.Combine( _gallery, "themes.json" ) ) )?.AsObject( );
        if ( themes is null )
        {
            throw new InvalidOperationException( "Gallery theme palettes unavailable." );
        }

        var palette = themes[theme]?.DeepClone( ).AsObject( );
        if ( palette is null )
        {
            throw new InvalidOperationException( $"Unknown gallery theme: {theme}" );
        }

        palette["id"] = theme;
        return palette;
    }

    // Maps every icon("name") used by the scenes to a file from the first
    // installed theme that has it. Unresolved names stay names and render as
    // CommandDeck's fallback glyph.
    private JsonObject ResolveIcons( string? preferred )
    {
        var names = Directory.EnumerateFiles( _gallery )
            .SelectMany( file => IconCall( ).Matches( File.ReadAllText( file ) ) )
            .Select( match => match.Groups[1].Value )
            .ToHashSet( StringComparer.Ordinal );
        var themes = preferred is null ? IconThemes : [preferred, .. IconThemes];
        JsonObject icons = [];
        foreach ( var name in names.Order( StringComparer.Ordinal ) )
        {
            var path = themes.Select( theme => FindIcon( theme, name ) ).FirstOrDefault( value => value is not null );
            if ( path is not null )
            {
                icons[name] = path;
            }
        }

        return icons;
    }

    private static string? FindIcon( string theme, string name )
    {
        var root = Path.Combine( "/usr/share/icons", theme );
        if ( !Directory.Exists( root ) )
        {
            return null;
        }

        return Directory.EnumerateFiles( root, name + ".*", SearchOption.AllDirectories )
            .Where( path => path.EndsWith( ".svg", StringComparison.Ordinal ) || path.EndsWith( ".png", StringComparison.Ordinal ) )
            .Where( path => !path.Contains( "symbolic", StringComparison.Ordinal ) )
            .OrderByDescending( IconSize )
            .ThenBy( path => path, StringComparer.Ordinal )
            .FirstOrDefault( );
    }

    private static int IconSize( string path )
    {
        if ( path.Contains( "/scalable/", StringComparison.Ordinal ) )
        {
            return 1024;
        }

        var size = IconDirectorySize( ).Match( path );
        return size.Success ? int.Parse( size.Groups[1].Value, CultureInfo.InvariantCulture ) : 0;
    }

    // Two by two, each tile at half resolution, in CollageThemes order.
    private string[] CollageArguments( string frames, string name )
    {
        List<string> arguments = ["-y", "-loglevel", "error"];
        foreach ( var theme in CollageThemes )
        {
            arguments.AddRange( ["-i", Path.Combine( frames, $"theme-{theme}.png" )] );
        }

        arguments.AddRange( ["-filter_complex",
            "[0]scale=iw/2:-1:flags=lanczos[a];[1]scale=iw/2:-1:flags=lanczos[b];" +
            "[2]scale=iw/2:-1:flags=lanczos[c];[3]scale=iw/2:-1:flags=lanczos[d];" +
            "[a][b][c][d]xstack=inputs=4:layout=0_0|w0_0|0_h0|w0_h0",
            "-frames:v", "1", Path.Combine( _output, name )] );
        return [.. arguments];
    }

    // Animation scenes name frames frame-<index>-<hold ms>.png.
    private async Task EncodeAnimationAsync( string frames, string name )
    {
        var list = new StringBuilder( );
        var duration = 0.0;
        var finalDelay = 0;
        string? last = null;
        foreach ( var frame in Directory.EnumerateFiles( frames, "frame-*.png" ).Order( StringComparer.Ordinal ) )
        {
            var hold = FrameHold( ).Match( Path.GetFileName( frame ) );
            if ( !hold.Success )
            {
                throw new InvalidOperationException( $"Unexpected animation frame: {frame}" );
            }

            var seconds = int.Parse( hold.Groups[1].Value, CultureInfo.InvariantCulture ) / 1000.0;
            duration += seconds;
            finalDelay = ( int ) Math.Round( seconds * 100 );
            list.Append( CultureInfo.InvariantCulture, $"file '{frame}'\nduration {seconds:0.###}\n" );
            last = frame;
        }

        if ( last is null )
        {
            throw new InvalidOperationException( $"The {name} scene rendered no frames." );
        }

        // The concat demuxer ignores the final duration unless the last file repeats.
        list.Append( CultureInfo.InvariantCulture, $"file '{last}'\n" );
        var manifest = Path.Combine( frames, "frames.txt" );
        await File.WriteAllTextAsync( manifest, list.ToString( ) );
        var length = duration.ToString( "0.###", CultureInfo.InvariantCulture );
        await FfmpegAsync( ["-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", manifest, "-fps_mode", "vfr",
            "-vf", "scale=1280:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=96:stats_mode=full[p];[s1][p]paletteuse=dither=none",
            "-t", length, "-final_delay", finalDelay.ToString( CultureInfo.InvariantCulture ),
            "-loop", "0", Path.Combine( _output, name + ".gif" )] );
        await FfmpegAsync( ["-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", manifest,
            "-vf", "fps=30,scale=1920:-2:flags=lanczos", "-c:v", "libx264", "-preset", "slow", "-crf", "22",
            "-t", length, "-pix_fmt", "yuv420p", "-movflags", "+faststart", Path.Combine( _output, name + ".mp4" )] );
    }

    private static async Task FfmpegAsync( string[] arguments )
    {
        ProcessStartInfo start = new( "ffmpeg" ) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach ( var argument in arguments )
        {
            start.ArgumentList.Add( argument );
        }

        await RunAsync( start, TimeSpan.FromMinutes( 2 ) );
    }

    private static async Task RunAsync( ProcessStartInfo start, TimeSpan timeout )
    {
        using var process = Process.Start( start );
        if ( process is null )
        {
            throw new InvalidOperationException( $"{start.FileName} did not start." );
        }

        var stdout = process.StandardOutput.ReadToEndAsync( );
        var stderr = process.StandardError.ReadToEndAsync( );
        try
        {
            using CancellationTokenSource deadline = new( timeout );
            await process.WaitForExitAsync( deadline.Token );
            var output = Quiet( await stdout + await stderr );
            if ( process.ExitCode != 0 )
            {
                Console.Error.WriteLine( output );
                throw new InvalidOperationException( $"{start.FileName} failed with exit code {process.ExitCode}." );
            }

            Console.Write( output );
        }
        catch ( OperationCanceledException error )
        {
            if ( !process.HasExited )
                process.Kill( entireProcessTree: true );
            await process.WaitForExitAsync( );
            Console.Error.WriteLine( Quiet( await stdout + await stderr ) );
            throw new TimeoutException( $"{start.FileName} did not finish within {timeout}.", error );
        }
        finally
        {
            if ( !process.HasExited )
            {
                process.Kill( entireProcessTree: true );
                await process.WaitForExitAsync( );
            }
        }
    }

    // Keeps saved-shot lines, warnings, and errors; drops preview heartbeats.
    private static string Quiet( string output ) =>
        string.Concat( output.Split( '\n' )
            .Where( line => line.Contains( "Saved ", StringComparison.Ordinal ) || line.Contains( "WARN", StringComparison.Ordinal ) || line.Contains( "ERROR", StringComparison.Ordinal ) )
            .Where( line => !line.Contains( "[CD-PREVIEW]", StringComparison.Ordinal ) )
            .Select( line => line.Trim( ) + "\n" ) );

    private static bool OnPath( string tool ) =>
        (Environment.GetEnvironmentVariable( "PATH" ) ?? "").Split( ':' ).Any( directory => File.Exists( Path.Combine( directory, tool ) ) );

    [GeneratedRegex( """icon\(\s*"([^"]+)"\s*\)""" )]
    private static partial Regex IconCall( );

    [GeneratedRegex( """/(\d+)(?:x\d+)?(?:@\d+x)?/""" )]
    private static partial Regex IconDirectorySize( );

    [GeneratedRegex( """^frame-\d+-(\d+)\.png$""" )]
    private static partial Regex FrameHold( );

    private sealed record Scene( string Name, string File, double Scale, string[] Outputs );
}
