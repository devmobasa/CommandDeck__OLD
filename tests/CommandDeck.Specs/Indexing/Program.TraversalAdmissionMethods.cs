namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ReplacedDirectoryCannotReachContentExtractorAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );
        var directory = Path.Combine( root, "notes" );
        var outside = Path.Combine( sandbox, "outside" );
        var bin = Path.Combine( sandbox, "bin" );
        var calls = Path.Combine( sandbox, "extractor-calls" );
        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        try
        {
            Directory.CreateDirectory( directory );
            Directory.CreateDirectory( outside );
            Directory.CreateDirectory( bin );
            foreach ( var name in new[] { "one.pdf", "two.pdf", "three.pdf" } )
            {
                await File.WriteAllTextAsync( Path.Combine( directory, name ), "inside fixture" );
                await File.WriteAllTextAsync( Path.Combine( outside, name ), "outside fixture" );
            }

            var executable = Path.Combine( bin, "pdftotext" );
            var quotedCalls = "'" + calls.Replace( "'", "'\\''", StringComparison.Ordinal ) + "'";
            await File.WriteAllTextAsync( executable,
                "#!/bin/sh\nprintf 'called\\n' >> " + quotedCalls + "\nexit 7\n" );
            File.SetUnixFileMode( executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            Environment.SetEnvironmentVariable( "PATH", bin + Path.PathSeparator + originalPath );

            IndexingOptions options = new( )
            {
                Roots = [new IndexRootOptions { Path = root, IncludeContentInSearch = true }],
                EnablePdfExtraction = true
            };
            using FileIndexSource source = new( options, new PrivacyOptions { NeverIndex = [] } );
            await using var scan = source.ScanAsync( null, default ).GetAsyncEnumerator( );
            True( await scan.MoveNextAsync( ) );
            Equal( 1, File.ReadAllLines( calls ).Length );

            Directory.Move( directory, Path.Combine( root, "moved" ) );
            Directory.CreateSymbolicLink( directory, outside );
            while ( await scan.MoveNextAsync( ) )
            {
                throw new InvalidOperationException( "A replaced directory emitted another row." );
            }

            Equal( 1, File.ReadAllLines( calls ).Length );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task NativeTimestampsOutsideDateTimeRangeRemainScannableAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) || !Directory.Exists( "/dev/shm" ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux tmpfs." );
        }

        // tmpfs retains native timestamps beyond DateTime's range; some disk
        // filesystems clamp them before the indexer can observe the regression.
        var root = Path.Combine( "/dev/shm", $"commanddeck-native-time-{Guid.NewGuid( ):N}" );
        var directory = Path.Combine( root, "notes" );
        var file = Path.Combine( directory, "note.txt" );
        const long seconds = 253_402_300_800;
        try
        {
            Directory.CreateDirectory( directory );
            await File.WriteAllTextAsync( file, "ordinary file" );
            var timestamp = await ProcessRunner.RunAsync( "touch",
                ["-m", "-d", $"@{seconds}", "--", file, directory], null, null, TimeSpan.FromSeconds( 2 ), default );
            Equal( 0, timestamp.ExitCode );
            Equal( seconds, LinuxMountIdentity.GetVersion( directory ).ModificationSeconds );

            IndexingOptions options = new( )
            {
                Roots = [new IndexRootOptions
                {
                    Path = root, IncludeContentInSearch = false,
                    ExcludeGeneratedDirectories = false, RespectGitIgnore = false
                }]
            };
            using FileIndexSource source = new( options, new PrivacyOptions { NeverIndex = [] } );
            List<IndexedItem> items = [];
            await foreach ( var item in source.ScanAsync( null, default ) )
            {
                items.Add( item );
            }

            Equal( file, items.Single( ).Path );
            Equal( seconds * 1000, items.Single( ).ModifiedUnixMilliseconds );
            Equal( file, (await source.IndexDirectoryAsync( directory, default )).Single( ).Path );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ActiveTraversalObservesChangedIgnoreRulesAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        var root = Path.Combine( sandbox, "root" );
        try
        {
            Directory.CreateDirectory( Path.Combine( root, ".git" ) );
            var directory = Path.Combine( root, "notes" );
            Directory.CreateDirectory( directory );
            var ignore = Path.Combine( root, ".gitignore" );
            foreach ( var name in new[] { "one.txt", "two.txt", "three.txt" } )
            {
                await File.WriteAllTextAsync( Path.Combine( directory, name ), "ordinary file" );
            }

            IndexingOptions options = new( )
            {
                Roots = [new IndexRootOptions { Path = root, IncludeContentInSearch = false, IncludeHidden = false }]
            };
            foreach ( var rule in new[] { "*.txt", "notes/" } )
            {
                await File.WriteAllTextAsync( ignore, string.Empty );
                using FileIndexSource source = new( options, new PrivacyOptions { NeverIndex = [] } );
                await using var scan = source.ScanAsync( null, default ).GetAsyncEnumerator( );
                True( await scan.MoveNextAsync( ) );
                True( scan.Current.Path!.EndsWith( ".txt", StringComparison.Ordinal ) );

                await File.WriteAllTextAsync( ignore, rule + "\n" );
                True( !await scan.MoveNextAsync( ), $"New '{rule}' rules must exclude remaining files in the active scan." );
            }
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }
}
