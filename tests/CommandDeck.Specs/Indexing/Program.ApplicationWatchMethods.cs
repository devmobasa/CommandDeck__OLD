using System.Collections.Concurrent;

namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed record WatchedApplications(
        string Staging, string Root, TrackingSearchCatalog Catalog, IndexCoordinator Indexer,
        ConcurrentQueue<string> Diagnostics, CommandDeckOptions Options );

    // A package file keeps its archive timestamp, older than the last scan.
    private static readonly DateTime PackagedModificationTime = new( 2026, 1, 5, 12, 0, 0, DateTimeKind.Utc );

    private static string DesktopEntryText( string name, string exec, string extra = "" ) =>
        $"[Desktop Entry]\nType=Application\nName={name}\nExec={exec}\n{extra}";

    // Writes beside the directory, then renames into place as a package manager does.
    private static async Task InstallDesktopEntryAsync( string staging, string target, string contents )
    {
        var temporary = Path.Combine( staging, Guid.NewGuid( ).ToString( "N" ) );
        await File.WriteAllTextAsync( temporary, contents );
        File.SetLastWriteTimeUtc( temporary, PackagedModificationTime );
        File.Move( temporary, target, overwrite: true );
    }

    private static async Task WithWatchedApplicationsAsync( Func<WatchedApplications, Task> body )
    {
        var root = CreateIncrementalSpecRoot( );
        var staging = Directory.CreateDirectory( Path.Combine( root, "staging" ) ).FullName;
        var userData = Path.Combine( root, "user" );
        var systemData = Path.Combine( root, "system" );
        Directory.CreateDirectory( Path.Combine( userData, "applications" ) );
        Directory.CreateDirectory( Path.Combine( systemData, "applications" ) );
        var oldDataHome = Environment.GetEnvironmentVariable( "XDG_DATA_HOME" );
        var oldDataDirectories = Environment.GetEnvironmentVariable( "XDG_DATA_DIRS" );
        try
        {
            Environment.SetEnvironmentVariable( "XDG_DATA_HOME", userData );
            Environment.SetEnvironmentVariable( "XDG_DATA_DIRS", systemData );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Indexing = new IndexingOptions
                {
                    Roots = [],
                    IndexOmarchyCommands = false,
                    IncrementalDebounceMilliseconds = 40
                }
            };
            ConcurrentQueue<string> diagnostics = new( );
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            await using TrackingSearchCatalog tracking = new( catalog, catalog );
            await using var indexer = new IndexCoordinator( tracking, tracking, options,
                diagnostic: value => diagnostics.Enqueue( value.Message ) );
            await indexer.StartAsync( default );
            await indexer.ReindexAsync( progress: null, default );
            async Task<bool> SettledAsync( ) =>
                (await indexer.GetIncrementalStatusAsync( default )) is { ReconciliationRequired: false, Reconciling: false };
            True( await WaitUntilAsync( SettledAsync, TimeSpan.FromSeconds( 5 ) ) );
            await indexer.ApplicationRefresh;
            diagnostics.Clear( );

            await body( new WatchedApplications( staging, root, tracking, indexer, diagnostics, options ) );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "XDG_DATA_HOME", oldDataHome );
            Environment.SetEnvironmentVariable( "XDG_DATA_DIRS", oldDataDirectories );
            DeleteIncrementalSpecRoot( root );
        }
    }

    private static Task InstalledApplicationBecomesSearchableAsync( ) =>
        WithWatchedApplicationsAsync( async fixture =>
        {
            (var staging, var root, var catalog) = (fixture.Staging, fixture.Root, fixture.Catalog);
            var scans = catalog.ReplaceAllCount;
            var target = Path.Combine( root, "system", "applications", "org.gnome.SoundRecorder.desktop" );
            await InstallDesktopEntryAsync( staging, target, DesktopEntryText( "Sound Recorder", "gnome-sound-recorder" ) );
            True( File.GetLastWriteTimeUtc( target ) == PackagedModificationTime, "the installed file keeps its old time" );
            True( await WaitForCatalogResultAsync( catalog, "Sound Recorder",
                static result => result.Id == "app:org.gnome.SoundRecorder.desktop", TimeSpan.FromSeconds( 5 ) ),
                "an installed application is found without a restart" );
            True( await WaitForCatalogResultAsync( catalog, "gnome-sound-recorder",
                static result => result.Title == "Sound Recorder", TimeSpan.FromSeconds( 5 ) ) );
            Equal( scans, catalog.ReplaceAllCount );

            // An upgrade replaces the file, again with an old timestamp.
            await InstallDesktopEntryAsync( staging, target, DesktopEntryText( "Voice Recorder", "gnome-sound-recorder" ) );
            True( await WaitForCatalogResultAsync( catalog, "Voice Recorder",
                static result => result.Id == "app:org.gnome.SoundRecorder.desktop", TimeSpan.FromSeconds( 5 ) ),
                "a replaced desktop file updates its row" );
            True( (await SearchAsync( catalog, "Sound Recorder" )).All( static result => result.Title != "Sound Recorder" ) );

            // So does a file rewritten in place that keeps its old timestamp.
            await File.WriteAllTextAsync( target, DesktopEntryText( "Audio Recorder", "gnome-sound-recorder" ) );
            File.SetLastWriteTimeUtc( target, PackagedModificationTime );
            True( await WaitForCatalogResultAsync( catalog, "Audio Recorder",
                static result => result.Id == "app:org.gnome.SoundRecorder.desktop", TimeSpan.FromSeconds( 5 ) ) );
            Equal( scans, catalog.ReplaceAllCount );
        } );

    private static Task RemovedApplicationLeavesSearchAsync( ) =>
        WithWatchedApplicationsAsync( async fixture =>
        {
            (var staging, var root, var catalog) = (fixture.Staging, fixture.Root, fixture.Catalog);
            var scans = catalog.ReplaceAllCount;
            var system = Path.Combine( root, "system", "applications" );
            await InstallDesktopEntryAsync( staging, Path.Combine( system, "fixture-editor.desktop" ),
                DesktopEntryText( "Fixture Editor", "fixture-editor" ) );
            await InstallDesktopEntryAsync( staging, Path.Combine( system, "fixture-viewer.desktop" ),
                DesktopEntryText( "Fixture Viewer", "fixture-viewer" ) );
            True( await WaitForCatalogResultAsync( catalog, "Fixture Viewer",
                static result => result.Kind == ResultKinds.Application, TimeSpan.FromSeconds( 5 ) ) );
            True( (await SearchAsync( catalog, "Fixture Editor" )).Count == 1 );

            File.Delete( Path.Combine( system, "fixture-editor.desktop" ) );
            True( await WaitUntilAsync( async ( ) => (await SearchAsync( catalog, "Fixture Editor" )).Count == 0,
                TimeSpan.FromSeconds( 5 ) ), "an uninstalled application leaves search" );

            // A user entry with the same desktop ID hides the system one.
            await InstallDesktopEntryAsync( staging, Path.Combine( root, "user", "applications", "fixture-viewer.desktop" ),
                DesktopEntryText( "Fixture Viewer", "fixture-viewer", "Hidden=true\n" ) );
            True( await WaitUntilAsync( async ( ) => (await SearchAsync( catalog, "Fixture Viewer" )).Count == 0,
                TimeSpan.FromSeconds( 5 ) ), "a masking user entry removes the system entry" );
            Equal( scans, catalog.ReplaceAllCount );
        } );

    private static Task ApplicationRefreshLeavesAPurgedCatalogToTheFullScanAsync( ) =>
        WithWatchedApplicationsAsync( async fixture =>
        {
            True( (await fixture.Indexer.PurgeProjectionAsync( default )).Succeeded );
            await InstallDesktopEntryAsync( fixture.Staging, Path.Combine( fixture.Root, "system", "applications",
                "fixture-player.desktop" ), DesktopEntryText( "Fixture Player", "fixture-player" ) );
            True( await WaitUntilAsync( ( ) => Task.FromResult( fixture.Diagnostics.Contains(
                "event=index.applications.skipped reason=full-scan-required" ) ), TimeSpan.FromSeconds( 5 ) ),
                "the watcher saw the change and left it to the full scan" );
            Equal( 0L, (await fixture.Catalog.GetStatsAsync( default )).ItemCount );

            await fixture.Indexer.ReindexAsync( progress: null, default );
            True( (await SearchAsync( fixture.Catalog, "Fixture Player" )).Count == 1 );
        } );

    // The fixtures above reindex during setup; this one starts on a current
    // catalog, as a restarted daemon does.
    private static Task ApplicationInstalledWhileStoppedAppearsAfterStartAsync( ) =>
        WithWatchedApplicationsAsync( async fixture =>
        {
            await fixture.Indexer.DisposeAsync( );
            await InstallDesktopEntryAsync( fixture.Staging, Path.Combine( fixture.Root, "system", "applications",
                "fixture-offline.desktop" ), DesktopEntryText( "Fixture Offline", "fixture-offline" ) );
            True( (await SearchAsync( fixture.Catalog, "Fixture Offline" )).Count == 0 );
            var scans = fixture.Catalog.ReplaceAllCount;
            await using var restarted = new IndexCoordinator( fixture.Catalog, fixture.Catalog, fixture.Options );
            await restarted.StartAsync( default );
            True( await WaitForCatalogResultAsync( fixture.Catalog, "Fixture Offline",
                static result => result.Kind == ResultKinds.Application, TimeSpan.FromSeconds( 5 ) ),
                "an application installed while stopped appears after start" );
            Equal( scans, fixture.Catalog.ReplaceAllCount );
        } );
}
