using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardCaptureSleepsWhileIdleAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        foreach ( var mode in new[] { "disabled", "paused", "capturing" } )
        {
            var root = Directory.CreateTempSubdirectory( "commanddeck-capture-idle-" ).FullName;
            var start = ClipboardPrivateFixtureStart( "--clipboard-idle-parent-fixture", root );
            start.ArgumentList.Add( root );
            start.ArgumentList.Add( mode );
            start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
            using var parent = Process.Start( start );
            if ( parent is null )
            {
                throw new IOException( "Capture fixture did not start." );
            }

            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 15 ) );
            var errors = parent.StandardError.ReadToEndAsync( timeout.Token );
            try
            {
                Equal( "ready", await parent.StandardOutput.ReadLineAsync( timeout.Token ) );
                await ProcessSleepsWhileIdleAsync( parent.Id, "clipboard capture (" + mode + ")", timeout.Token );
                await parent.StandardInput.WriteLineAsync( "stop" );
                await parent.WaitForExitAsync( timeout.Token );
                Equal( 0, parent.ExitCode );
                Equal( "", await errors );
            }
            finally
            {
                if ( !parent.HasExited )
                {
                    parent.Kill( entireProcessTree: true );
                    await parent.WaitForExitAsync( );
                }

                Directory.Delete( root, recursive: true );
            }
        }
    }

    private static async Task<int> RunClipboardIdleParentFixtureAsync( string root, string mode )
    {
        using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new ClipboardHistoryOptions( ) );
        if ( mode != "disabled" )
        {
            store.SetCapturePolicy( store.Policy.Revision, ClipboardConsent.Text, paused: mode == "paused" );
        }

        await using ClipboardCapture capture = new( store, ( ) => ClipboardIdleHelperStart( root ) );
        capture.Start( "unused-injected-idle-helper", [] );
        if ( mode == "capturing" )
        {
            await ClipboardEventuallyAsync( ( ) => capture.Status == "Capturing text" );
        }

        Console.WriteLine( "ready" );
        _ = await Console.In.ReadLineAsync( );
        return 0;
    }
}
