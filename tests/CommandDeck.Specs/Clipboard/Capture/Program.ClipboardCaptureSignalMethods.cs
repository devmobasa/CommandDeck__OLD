using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardCaptureRevokesOnPolicyChangeAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-signal-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new ClipboardHistoryOptions( ) );
            store.Enabled = true;
            var starts = 0;
            await using ClipboardCapture capture = new( store, ( ) =>
            {
                Interlocked.Increment( ref starts );
                return ClipboardIdleHelperStart( root );
            } );
            capture.Start( "unused-injected-idle-helper", [] );
            var helper = await WaitForIdleHelperAsync( 1, "Capturing text" );

            await RevokedAsync( helper, ( ) => store.SetCapturePolicy( store.Policy.Revision, ClipboardConsent.Text, paused: true ) );
            store.SetCapturePolicy( store.Policy.Revision, ClipboardConsent.Text, paused: false );
            helper = await WaitForIdleHelperAsync( 2, "Capturing text" );
            await RevokedAsync( helper, ( ) => EnableRichFixture( store ) );
            helper = await WaitForIdleHelperAsync( 3, "Capturing text and rich content" );
            await RevokedAsync( helper, ( ) => store.Enabled = false );
            store.Enabled = true;
            helper = await WaitForIdleHelperAsync( 4, "Capturing text" );
            await RevokedAsync( helper, ( ) => store.Clear( ) );
            _ = await WaitForIdleHelperAsync( 5, "Capturing text" );

            async Task<int> WaitForIdleHelperAsync( int expectedStarts, string status )
            {
                var started = Stopwatch.GetTimestamp( );
                while ( (Volatile.Read( ref starts ) != expectedStarts || capture.Status != status) &&
                    Stopwatch.GetElapsedTime( started ) < TimeSpan.FromSeconds( 10 ) )
                {
                    await Task.Delay( 10 );
                }

                Equal( expectedStarts, Volatile.Read( ref starts ) );
                Equal( status, capture.Status );
                return int.Parse( await File.ReadAllTextAsync( Path.Combine( root, "helper.pid" ) ), CultureInfo.InvariantCulture );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardCaptureWaitsForPolicyChangesWhileOffAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-off-" ).FullName;
        try
        {
            using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new ClipboardHistoryOptions( ) );
            var starts = 0;
            await using ClipboardCapture capture = new( store, ( ) =>
            {
                Interlocked.Increment( ref starts );
                return ClipboardIdleHelperStart( root );
            } );
            capture.Start( "unused-injected-idle-helper", [] );
            Equal( "Capture is disabled", capture.Status );
            await Task.Delay( 100 );

            await StartsPromptlyAsync( ( ) => Volatile.Read( ref starts ) == 1, ( ) => store.Enabled = true );
            await ClipboardEventuallyAsync( ( ) => capture.Status == "Capturing text" );
            var helper = int.Parse( await File.ReadAllTextAsync( Path.Combine( root, "helper.pid" ) ), CultureInfo.InvariantCulture );
            await RevokedAsync( helper, ( ) => store.SetCapturePolicy( store.Policy.Revision, ClipboardConsent.Text, paused: true ) );
            await ClipboardEventuallyAsync( ( ) => capture.Status == "Capture is paused" );
            await Task.Delay( 100 );

            Equal( 1, Volatile.Read( ref starts ) );
            await StartsPromptlyAsync( ( ) => Volatile.Read( ref starts ) == 2,
                ( ) => store.SetCapturePolicy( store.Policy.Revision, ClipboardConsent.Text, paused: false ) );
            await ClipboardEventuallyAsync( ( ) => capture.Status == "Capturing text" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardCaptureStartsWhenMigrationReviewIsAcknowledgedAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-review-" ).FullName;
        var path = Path.Combine( root, "history.db" );
        try
        {
            WriteLegacyClipboard( path, 12, 0, DateTimeOffset.UtcNow.AddDays( -10 ).ToUnixTimeSeconds( ), enabled: true );
            using ClipboardHistoryStore store = new( path, new ClipboardHistoryOptions( ) );
            True( store.MigrationReview.Required, "expired legacy rows hold capture for review" );
            var starts = 0;
            await using ClipboardCapture capture = new( store, ( ) =>
            {
                Interlocked.Increment( ref starts );
                return ClipboardIdleHelperStart( root );
            } );
            capture.Start( "unused-injected-idle-helper", [] );
            Equal( "Clipboard migration review is required", capture.Status );
            await Task.Delay( 100 );

            Equal( 0, Volatile.Read( ref starts ) );
            await StartsPromptlyAsync( ( ) => Volatile.Read( ref starts ) == 1,
                ( ) => store.AcknowledgeMigration( store.Policy.Revision, store.MigrationReview.StoreRevision, confirmExpiredDeletion: true ) );
            await ClipboardEventuallyAsync( ( ) => capture.Status == "Capturing text" );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task StartsPromptlyAsync( Func<bool> started, Action change )
    {
        var changed = Stopwatch.GetTimestamp( );
        change( );
        await ClipboardEventuallyAsync( started );
        var elapsed = Stopwatch.GetElapsedTime( changed );
        True( elapsed < TimeSpan.FromMilliseconds( 500 ), $"capture started {elapsed.TotalMilliseconds:F0} ms after the policy change" );
    }

    private static async Task ClipboardCaptureRetriesStoppedHelperWithBackoffAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            return;
        }

        foreach ( var configured in new[] { false, true } )
        {
            var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-retry-" ).FullName;
            try
            {
                using ClipboardHistoryStore store = new( Path.Combine( root, "history.db" ), new ClipboardHistoryOptions( ) );
                store.Enabled = true;
                System.Collections.Concurrent.ConcurrentQueue<long> attempts = new( );
                await using ClipboardCapture capture = new( store, ( ) =>
                {
                    attempts.Enqueue( Stopwatch.GetTimestamp( ) );
                    if ( !configured )
                    {
                        return new ProcessStartInfo( Path.Combine( root, "missing-native-helper" ) );
                    }

                    var start = ClipboardIdleHelperStart( root );
                    start.ArgumentList.Add( "--exit-after-configure" );
                    return start;
                } );
                capture.Start( "unused-injected-helper", [] );
                await ClipboardEventuallyAsync( ( ) => attempts.Count >= 3 );
                var times = attempts.ToArray( );
                var pause = Stopwatch.GetElapsedTime( times[1], times[2] );
                True( pause >= TimeSpan.FromMilliseconds( 1800 ),
                    $"a repeatedly stopped helper retried after {pause.TotalMilliseconds:F0} ms (configured: {configured})" );

                // A policy change ends the backoff wait at once.
                store.Enabled = false;
                await ClipboardEventuallyAsync( ( ) => capture.Status == "Capture is disabled" );
                var count = attempts.Count;
                store.Enabled = true;
                await ClipboardEventuallyAsync( ( ) => attempts.Count == count + 1 );
            }
            finally
            {
                Directory.Delete( root, recursive: true );
            }
        }
    }

    // Revocation must end the session and reap its helper promptly.
    private static async Task RevokedAsync( int helper, Action change )
    {
        var started = Stopwatch.GetTimestamp( );
        change( );
        while ( Directory.Exists( $"/proc/{helper}" ) && Stopwatch.GetElapsedTime( started ) < TimeSpan.FromSeconds( 5 ) )
        {
            await Task.Delay( 5 );
        }

        var elapsed = Stopwatch.GetElapsedTime( started );
        True( !Directory.Exists( $"/proc/{helper}" ), "revoked capture reaps its helper" );
        True( elapsed < TimeSpan.FromSeconds( 1 ), $"revocation took {elapsed.TotalMilliseconds:F0} ms" );
    }

    private static ProcessStartInfo ClipboardIdleHelperStart( string root )
    {
        var start = ClipboardTransportFixtureStart( acknowledge: true );
        start.ArgumentList.RemoveAt( start.ArgumentList.Count - 1 );
        start.ArgumentList.RemoveAt( start.ArgumentList.Count - 1 );
        start.ArgumentList.Add( "--clipboard-idle-helper-fixture" );
        start.ArgumentList.Add( root );
        return start;
    }

    // Speaks only the ready/configure handshake, then waits without offering selections.
    private static async Task<int> RunClipboardIdleHelperFixtureAsync( string root, string[] args )
    {
        var epoch = ulong.Parse( ReadArgumentValue( args, "--host-epoch" ) ?? "0", CultureInfo.InvariantCulture );
        using AnonymousPipeClientStream control = new( PipeDirection.In, ReadArgumentValue( args, "--control-read" ) ?? "" );
        using AnonymousPipeClientStream acknowledgements = new( PipeDirection.Out, ReadArgumentValue( args, "--control-write" ) ?? "" );
        using var input = Console.OpenStandardInput( );
        using var output = Console.OpenStandardOutput( );
        await File.WriteAllTextAsync( Path.Combine( root, "helper.pid" ), Environment.ProcessId.ToString( CultureInfo.InvariantCulture ) );

        await ClipboardHelperFraming.WriteAsync( output, ClipboardCaptureProtocol.Status( epoch, ClipboardCaptureStatus.Ready ), default );
        while ( await ClipboardHelperFraming.ReadAsync( input, default ) is { } frame )
        {
            if ( frame.Header.Type != ClipboardHelperFrameType.Configure )
            {
                return 2;
            }

            var consent = ClipboardCaptureProtocol.ReadConfiguration( frame );
            await ClipboardHelperFraming.WriteAsync( output,
                ClipboardCaptureProtocol.Status( epoch, ClipboardCaptureStatus.Configured, ( uint ) consent ), default );
            if ( args.Contains( "--exit-after-configure", StringComparer.Ordinal ) )
            {
                await Task.Delay( 100 );
                return 0;
            }
        }

        return 0;
    }
}
