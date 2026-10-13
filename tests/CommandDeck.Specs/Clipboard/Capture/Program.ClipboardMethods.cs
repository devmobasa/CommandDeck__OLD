using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardFallbackRejectsUnverifiableCaptureAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }
        var directory = Directory.CreateTempSubdirectory( "commanddeck-clipboard-watch-" ).FullName;
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        var originalProbe = Environment.GetEnvironmentVariable( "COMMANDDECK_CLIPBOARD_WATCH_PROBE" );
        try
        {
            var watcher = Path.Combine( directory, "wl-paste" );
            await File.WriteAllTextAsync( watcher, "#!/bin/sh\n: > \"$COMMANDDECK_CLIPBOARD_WATCH_PROBE\"\nexit 99\n" );
            File.SetUnixFileMode( watcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( "PATH", directory + Path.PathSeparator + originalPath );
            var probe = Path.Combine( directory, "watcher-was-started" );
            Environment.SetEnvironmentVariable( "COMMANDDECK_CLIPBOARD_WATCH_PROBE", probe );
            using ClipboardHistoryStore store = new( Path.Combine( directory, "history.db" ), new ClipboardHistoryOptions( ) );
            await using ClipboardCapture capture = new( store, ( ) => new System.Diagnostics.ProcessStartInfo( Path.Combine( directory, "missing-native-helper" ) ) );
            var executable = Path.Combine( FindRepositoryRoot( ), "src/CommandDeck.App/bin/Release/net11.0/linux-x64/commanddeck" );
            capture.Start( executable, [] );
            await Task.Delay( 100 );
            Equal( 0, store.Search( "", 10 ).Count );
            store.Enabled = true;
            var deadline = DateTime.UtcNow.AddSeconds( 5 );
            while ( !capture.Status.Contains( "native helper stopped", StringComparison.Ordinal ) && DateTime.UtcNow < deadline )
            {
                await Task.Delay( 30 );
            }
            True( capture.Status.Contains( "native helper stopped", StringComparison.Ordinal ), "unavailable native capture reports failure without an unsafe fallback" );
            Equal( 0, store.Search( "", 10 ).Count );
            True( !File.Exists( probe ), "fallback must not request payloads before control-inventory admission" );
            store.Enabled = false;
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Environment.SetEnvironmentVariable( "COMMANDDECK_CLIPBOARD_WATCH_PROBE", originalProbe );
            Directory.Delete( directory, recursive: true );
        }
    }

    private sealed class ClipboardTestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow( ) => Now;
    }

    private static async Task ClipboardHistoryIsOptInAndBoundedAsync( )
    {
        var directory = Directory.CreateTempSubdirectory( "commanddeck-clipboard-" ).FullName;
        var database = Path.Combine( directory, "history.db" );
        try
        {
            ClipboardTestClock clock = new( );
            using ( ClipboardHistoryStore store = new( database, new ClipboardHistoryOptions { MaximumEntries = 10, RetentionHours = 1 }, clock ) )
            {
                store.Capture( "must not be captured" );
                Equal( 0, store.Search( "", 100 ).Count );
                True( !store.Enabled, "capture defaults off" );
                store.Enabled = true;
                store.Capture( "Pinned text" );
                var pinned = store.Search( "Pinned", 100 ).Single( );
                True( store.Pin( pinned.Id, true ), "pin succeeds" );
                for ( var i = 0; i < 20; i++ )
                {
                    store.Capture( "ordinary " + i );
                }
                Equal( 10, store.Search( "", 100 ).Count );
                store.Capture( new string( 'x', 32 * 1024 + 1 ) );
                Equal( 10, store.Search( "", 100 ).Count );
                clock.Now = clock.Now.AddHours( 2 );
                Equal( 1, store.Search( "", 100 ).Count );
                True( store.Search( "Pinned", 100 )[0].Pinned, "pins survive retention" );
                store.Enabled = false;
                store.Capture( "paused" );
                Equal( 0, store.Search( "paused", 100 ).Count );
                ClipboardHistoryProvider provider = new( store );
                var rows = await provider.SearchAsync( QueryParser.Parse( "clip: pinned" ), new SearchRequest { Query = "clip: pinned" }, default );
                var copy = rows.Single( row => row.Id == "clipboard:" + pinned.Id ).Actions[0];
                store.Delete( pinned.Id );
                True( !(await provider.ExecuteAsync( copy, default )).Succeeded, "deleted entries cannot be copied from stale results" );
                store.Enabled = true;
                store.Capture( "persisted" );
            }
            using ( ClipboardHistoryStore reopened = new( database, new ClipboardHistoryOptions( ), clock ) )
            {
                True( reopened.Enabled, "explicit opt-in persists" );
                Equal( "persisted", reopened.Search( "", 10 ).Single( ).Text );
                reopened.Clear( );
                Equal( 0, reopened.Search( "", 10 ).Count );
            }
            if ( OperatingSystem.IsLinux( ) )
            {
                Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode( database ) );
            }
            using StringWriter output = new( );
            using MemoryStream sensitive = new( Encoding.UTF8.GetBytes( "secret" ) );
            await ClipboardCapture.WriteFrameAsync( sensitive, output, "sensitive", default );
            Equal( "", output.ToString( ) );
            Equal( 0L, sensitive.Position );
            using MemoryStream text = new( Encoding.UTF8.GetBytes( "line one\nline two" ) );
            await ClipboardCapture.WriteFrameAsync( text, output, "data", default );
            Equal( "line one\nline two", Encoding.UTF8.GetString( Convert.FromBase64String( output.ToString( ).Trim( ) ) ) );
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }
}
