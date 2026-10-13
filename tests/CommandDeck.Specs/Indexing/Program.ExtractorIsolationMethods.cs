using System.Diagnostics;
using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task FileTypeExtractionLimitsOverrideDefaultsAsync( )
    {
        var root = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-type-limits-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        var truncatedPath = Path.Combine( root, "truncated.txt" );
        var rejectedPath = Path.Combine( root, "rejected.txt" );
        await File.WriteAllTextAsync( truncatedPath, "abcdefghij" );
        await File.WriteAllTextAsync( rejectedPath, "abcdefghij" );
        IndexingOptions options = new( )
        {
            Roots =
            [
                new IndexRootOptions
                {
                    Path = root,
                    FileTypePolicies =
                    [
                        new FileTypeIndexPolicyOptions
                        {
                            Pattern = "truncated.txt",
                            MaximumFileBytes = 32,
                            MaximumExtractedBytes = 5
                        },
                        new FileTypeIndexPolicyOptions
                        {
                            Pattern = "rejected.txt",
                            MaximumFileBytes = 5,
                            MaximumExtractedBytes = 32
                        }
                    ]
                }
            ],
            MaximumTextFileBytes = 64,
            MaximumExtractedBytes = 64,
            IndexDesktopApplications = false,
            IndexOmarchyCommands = false
        };
        using FileIndexSource source = new( options, new PrivacyOptions { NeverIndex = [] } );
        try
        {
            var truncated = (await source.IndexPathAsync(
                truncatedPath,
                CancellationToken.None )).Single( value => value.Kind == ResultKinds.File );
            var rejected = (await source.IndexPathAsync(
                rejectedPath,
                CancellationToken.None )).Single( value => value.Kind == ResultKinds.File );
            Equal( "abcde", truncated.Body );
            True( rejected.Body is null );

            var resolved = FileIndexPolicyResolver.Resolve(
                options.Roots.Single( ),
                truncatedPath );
            Equal( 32L, resolved.MaximumFileBytes );
            Equal( 5, resolved.MaximumExtractedBytes );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task HostileExtractorsAreIsolatedAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-hostile-extractor-{Guid.NewGuid( ):N}" );
        var bin = Path.Combine( root, "bin" );
        Directory.CreateDirectory( bin );
        var extractor = Path.Combine( bin, "pdftotext" );
        await File.WriteAllTextAsync( extractor, """
            #!/bin/sh
            input=''
            for argument in "$@"; do
              case "$argument" in
                *.pdf) input="$argument" ;;
              esac
            done
            case "$input" in
              *safe*) printf '%s' "${COMMANDDECK_EXTRACTOR_SECRET-unset}" ;;
              *flood*) exec /usr/bin/yes flood ;;
              *hang*) sleep 5 ;;
              *memory*) value=$(head -c 134217728 /dev/zero | tr '\000' x); printf '%s' "$value" ;;
              *) printf 'ordinary' ;;
            esac
            """.ReplaceLineEndings( "\n" ) );
        File.SetUnixFileMode(
            extractor,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        var pandoc = Path.Combine( bin, "pandoc" );
        await File.WriteAllTextAsync( pandoc, """
            #!/bin/sh
            case "$3" in
              *office-ok*) printf 'PandocSearchMarker' ;;
              *) printf 'malformed' >&2; exit 7 ;;
            esac
            """.ReplaceLineEndings( "\n" ) );
        File.SetUnixFileMode(
            pandoc,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );

        string[] names = ["safe.pdf", "flood.pdf", "hang-a.pdf", "hang-b.pdf", "memory.pdf"];
        foreach ( var name in names )
        {
            await File.WriteAllTextAsync( Path.Combine( root, name ), "fixture" );
        }
        await File.WriteAllTextAsync( Path.Combine( root, "office-ok.docx" ), "fixture" );
        await File.WriteAllTextAsync( Path.Combine( root, "office-fail.odt" ), "fixture" );
        await File.WriteAllTextAsync( Path.Combine( root, "office-missing.epub" ), "fixture" );

        var originalPath = Environment.GetEnvironmentVariable( "PATH" );
        var originalSecret = Environment.GetEnvironmentVariable( "COMMANDDECK_EXTRACTOR_SECRET" );
        Environment.SetEnvironmentVariable(
            "PATH",
            bin + Path.PathSeparator + (originalPath ?? "/usr/bin:/bin") );
        Environment.SetEnvironmentVariable( "COMMANDDECK_EXTRACTOR_SECRET", "must-not-leak" );
        IndexingOptions options = new( )
        {
            Roots =
            [
                new IndexRootOptions
                {
                    Path = root,
                    FileTypePolicies =
                    [
                        new FileTypeIndexPolicyOptions
                        {
                            Pattern = "safe.pdf",
                            MaximumExtractedBytes = 64,
                            ExtractorTimeoutMilliseconds = 500,
                            ExtractorMemoryLimitBytes = 64 * 1024 * 1024
                        },
                        new FileTypeIndexPolicyOptions
                        {
                            Pattern = "flood.pdf",
                            MaximumExtractedBytes = 1_024
                        },
                        new FileTypeIndexPolicyOptions
                        {
                            Pattern = "hang-*.pdf",
                            ExtractorTimeoutMilliseconds = 100
                        },
                        new FileTypeIndexPolicyOptions
                        {
                            Pattern = "memory.pdf",
                            ExtractorMemoryLimitBytes = 32 * 1024 * 1024
                        }
                    ]
                }
            ],
            EnablePdfExtraction = true,
            EnablePandocExtraction = true,
            MaximumDocumentFileBytes = 1024,
            MaximumExtractedBytes = 4_096,
            ExtractorTimeoutMilliseconds = 500,
            ExtractorMemoryLimitBytes = 64 * 1024 * 1024,
            ExtractorStandardErrorBytes = 1_024,
            MaximumConcurrentExtractors = 1,
            IndexDesktopApplications = false,
            IndexOmarchyCommands = false
        };
        using FileIndexSource source = new( options, new PrivacyOptions { NeverIndex = [] } );
        try
        {
            var safe = await ExtractedFileAsync( source, Path.Combine( root, "safe.pdf" ) );
            Equal( "unset", safe.Body );

            var serial = Stopwatch.StartNew( );
            var firstHang = ExtractedFileAsync(
                source,
                Path.Combine( root, "hang-a.pdf" ) );
            var secondHang = ExtractedFileAsync(
                source,
                Path.Combine( root, "hang-b.pdf" ) );
            var hangs = await Task.WhenAll( firstHang, secondHang );
            serial.Stop( );
            True( hangs.All( static value => value.Body is null ) );
            True( serial.Elapsed >= TimeSpan.FromMilliseconds( 170 ) );

            var flood = await ExtractedFileAsync( source, Path.Combine( root, "flood.pdf" ) );
            var memory = await ExtractedFileAsync( source, Path.Combine( root, "memory.pdf" ) );
            True( flood.Body is null );
            True( memory.Body is null );

            var office = await ExtractedFileAsync(
                source,
                Path.Combine( root, "office-ok.docx" ) );
            Equal( "PandocSearchMarker", office.Body );
            var failedOffice = await ExtractedFileAsync(
                source,
                Path.Combine( root, "office-fail.odt" ) );
            True( failedOffice.Body is null );
            True( source.SnapshotWorkItems( ).Any( item =>
                item.Reason == "extractor-nonzero-exit" ) );
            File.Delete( pandoc );
            var missingOffice = await ExtractedFileAsync(
                source,
                Path.Combine( root, "office-missing.epub" ) );
            True( missingOffice.Body is null );
            True( source.SnapshotWorkItems( ).Any( item =>
                item.Reason == "extractor-missing:pandoc" ) );

            var bounded = await ProcessRunner.RunBoundedAsync(
                "/usr/bin/yes",
                ["direct-flood"],
                workingDirectory: null,
                TimeSpan.FromSeconds( 2 ),
                new ProcessExecutionLimits
                {
                    MaximumStandardOutputBytes = 1_024,
                    MaximumStandardErrorBytes = 1_024,
                    MaximumMemoryBytes = 64 * 1024 * 1024,
                    ClearEnvironment = true
                },
                CancellationToken.None );
            True( bounded.OutputLimitExceeded );
            True( Encoding.UTF8.GetByteCount( bounded.StandardOutput ) <= 1_024 );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", originalPath );
            Environment.SetEnvironmentVariable( "COMMANDDECK_EXTRACTOR_SECRET", originalSecret );
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task SpecialFilesCannotBlockContentExtractionAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-special-extraction-" ).FullName;
        string[] pipes = [Path.Combine( root, "pipe.txt" ), Path.Combine( root, "pipe.pdf" )];
        foreach ( var pipe in pipes )
        {
            var created = await ProcessRunner.RunAsync(
                "mkfifo", [pipe], null, null, TimeSpan.FromSeconds( 2 ), default );
            Equal( 0, created.ExitCode );
        }
        await File.WriteAllTextAsync( Path.Combine( root, "ordinary.txt" ), "OrdinarySearchMarker" );

        IndexingOptions options = new( )
        {
            Roots = [new IndexRootOptions { Path = root }],
            EnablePdfExtraction = true,
            IndexDesktopApplications = false,
            IndexOmarchyCommands = false
        };
        using FileIndexSource source = new( options, new PrivacyOptions { NeverIndex = [] } );
        try
        {
            var scanned = await Task.Run( async ( ) =>
            {
                List<IndexedItem> items = [];
                await foreach ( var item in source.ScanAsync( null, CancellationToken.None ) )
                {
                    items.Add( item );
                }
                return items;
            } ).WaitAsync( TimeSpan.FromSeconds( 10 ) );
            Equal( "OrdinarySearchMarker", scanned.Single( item => item.Title == "ordinary.txt" ).Body );
            True( scanned.Single( item => item.Title == "pipe.txt" ).Body is null );

            var updated = await Task.Run( ( ) => source.IndexPathAsync(
                pipes[0], CancellationToken.None ).AsTask( ) ).WaitAsync( TimeSpan.FromSeconds( 10 ) );
            True( updated.Single( item => item.Kind == ResultKinds.File ).Body is null );

            var failures = source.SnapshotWorkItems( )
                .Where( item => item.Reason == "unreadable-or-special-file" )
                .Select( item => item.RelativePath )
                .Order( StringComparer.Ordinal );
            True( failures.SequenceEqual( ["pipe.pdf", "pipe.txt"] ),
                "special files are rejected before text reads or external extractors open them" );
        }
        finally
        {
            // Release any reader still blocked on a FIFO so a regression cannot strand the run.
            foreach ( var pipe in pipes )
            {
                try
                {
                    using FileStream release = new( pipe, FileMode.Open, FileAccess.ReadWrite );
                }
                catch ( IOException )
                {
                }
            }
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task<IndexedItem> ExtractedFileAsync(
        FileIndexSource source,
        string path ) =>
        (await source.IndexPathAsync( path, CancellationToken.None ))
            .Single( value => value.Kind == ResultKinds.File );
}
