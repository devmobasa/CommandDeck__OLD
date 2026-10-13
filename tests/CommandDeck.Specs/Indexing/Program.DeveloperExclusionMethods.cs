namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task NestedRootsAndSymlinksPreserveExclusionPolicyAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-exclusion-roots-{Guid.NewGuid( ):N}" );
        var nested = Path.Combine( root, "node_modules/kept" );
        Directory.CreateDirectory( nested );
        try
        {
            var kept = Path.Combine( nested, "source.txt" );
            var blocked = Path.Combine( root, "node_modules/blocked.txt" );
            await File.WriteAllTextAsync( kept, "keep" );
            await File.WriteAllTextAsync( blocked, "block" );
            File.CreateSymbolicLink( Path.Combine( root, "alias.txt" ), blocked );
            IndexingOptions options = new( )
            {
                Roots = [new IndexRootOptions { Path = root, FollowSymbolicLinks = true },
                    new IndexRootOptions { Path = nested }]
            };
            using FileIndexSource source = new( options, new PrivacyOptions { NeverIndex = [] } );
            List<string> full = [];
            await foreach ( var item in source.ScanAsync( null, CancellationToken.None ) )
            {
                full.Add( item.Path! );
            }
            Equal( 1, full.Count );
            Equal( kept, full[0] );
            var scoped = await source.IndexDirectoryAsync( root, CancellationToken.None );
            Equal( 1, scoped.Count );
            Equal( kept, scoped[0].Path );
            Equal( 0, (await source.IndexPathAsync( Path.Combine( root, "alias.txt" ), CancellationToken.None )).Count );
            Equal( 1, (await source.IndexPathAsync( kept, CancellationToken.None )).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task DeveloperDirectoryDefaultsAreProjectScopedAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-exclusions-{Guid.NewGuid( ):N}" );
        string[] markers =
        [
            "js/package.json", "rust/Cargo.toml", "dotnet/App.csproj", "python/pyproject.toml",
            "maven/pom.xml", "gradle/build.gradle.kts", "php/composer.json",
            "custom-environment/pyvenv.cfg"
        ];
        string[] excluded =
        [
            "node_modules/library/index.js", "js/node_modules/library/index.js", "js/.next/server.js",
            "js/dist/bundle.js", "rust/target/debug/app", "dotnet/bin/Debug/app.dll",
            "dotnet/obj/project.assets.json", "python/__pycache__/module.pyc", "python/.venv/lib/module.py",
            "maven/target/classes/Main.class", "gradle/build/classes/Main.class", "gradle/.gradle/cache.bin",
            "php/vendor/library/source.php", "custom-environment/lib/module.py"
        ];
        string[] included =
        [
            "js/src/main.js", "rust/src/main.rs", "dotnet/Program.cs", "python/main.py",
            "maven/src/Main.java", "gradle/gradle/wrapper/gradle-wrapper.properties", "php/src/main.php",
            "personal/bin/tool", "personal/build/notes.txt", "personal/target/notes.txt",
            "personal/dist/notes.txt", "personal/vendor/notes.txt"
        ];
        try
        {
            foreach ( var relative in markers.Concat( excluded ).Concat( included ) )
            {
                var path = Path.Combine( root, relative );
                Directory.CreateDirectory( Path.GetDirectoryName( path )! );
                await File.WriteAllTextAsync( path, "fixture" );
            }
            using FileIndexSource source = new( new IndexingOptions
            {
                Roots = [new IndexRootOptions { Path = root, IncludeHidden = true }]
            }, new PrivacyOptions { NeverIndex = [] } );
            List<string> scanned = [];
            await foreach ( var item in source.ScanAsync( null, CancellationToken.None ) )
            {
                if ( item.Kind == ResultKinds.File )
                {
                    scanned.Add( item.Path! );
                }
            }
            foreach ( var relative in excluded )
            {
                var path = Path.Combine( root, relative );
                True( !scanned.Contains( path, StringComparer.Ordinal ), $"Generated path was scanned: {relative}" );
                Equal( 0, (await source.IndexPathAsync( path, CancellationToken.None )).Count );
            }
            foreach ( var relative in included )
            {
                var path = Path.Combine( root, relative );
                True( scanned.Contains( path, StringComparer.Ordinal ), $"Source path was hidden: {relative}" );
                True( (await source.IndexPathAsync( path, CancellationToken.None )).Count > 0 );
            }
            True( source.SnapshotDiagnostics( ).Any( value => value.RelativePath == "node_modules" ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task DirectoryExclusionsApplyToDirectUpdatesAndPreviewsAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-root-glob-{Guid.NewGuid( ):N}" );
        var path = Path.Combine( root, "node_modules/library/index.txt" );
        Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        await File.WriteAllTextAsync( path, "must stay out of search and previews" );
        try
        {
            foreach ( var pattern in new[] { "node_modules", "node_modules/", "*/node_modules/*" } )
            {
                IndexingOptions indexing = new( )
                {
                    Roots = [new IndexRootOptions { Path = root, IncludeHidden = true, ExcludeGlobs = [pattern] }]
                };
                PrivacyOptions privacy = new( )
                {
                    NeverIndex = []
                };
                using FileIndexSource source = new( indexing, privacy );
                Equal( 0, (await source.IndexPathAsync( path, CancellationToken.None )).Count );
                Equal( 0, (await source.IndexDirectoryAsync( Path.GetDirectoryName( path )!, CancellationToken.None )).Count );
                FilePreviewService previews = new( indexing, privacy );
                var preview = await previews.ReadAsync( path, CancellationToken.None );
                True( preview.Preview is null );
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }
}
