using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RepositoryIgnorePatternsAgreeWithGitAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-gitignore-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( root );
        string[] paths =
        [
            "root.txt", "nested/root.txt", "a.log", "keep.log", "sub/a.log", "sub/keep.log",
            "cache/a.txt", "cache/keep.txt", "sub/cache/a.txt", "filecache", "abc", "abc.txt", "1.txt",
            "A.txt", "é.txt", "sub/é.txt", "a/b.c", "a/x/b1.c", "a/x/y/b2.h", "a/x/y/b22.h",
            "space file.txt", "trailing.txt ", "#literal.txt", "!literal.txt", "file?.txt",
            "logs/a.log", "logs/keep.log", "logs/nested/keep.log", "literal[.txt", "line\nbreak.txt"
        ];
        string[] ruleSets =
        [
            "/root.txt", "root.txt", "*.log\n!keep.log", "cache/\n!cache/keep.txt",
            "cache/*\n!cache/keep.txt", "**/cache/**", "a/**/b?.[ch]", "a/**/b*.?",
            "logs/**\n!logs/keep.log", "[[:digit:]].txt", "[[:alpha:]].txt", "[!a-z].txt", "[^A-Z].txt",
            "[a-z].txt", "?.txt", "??.txt", "space\\ file.txt", "trailing.txt\\ ",
            "trailing.txt   ", "\\#literal.txt\n\\!literal.txt", "file\\?.txt", "literal[[]*.txt",
            "invalid[", "abc\\", "**", "**/", "/**/root.txt", "sub/**.txt", "a/***b.c",
            "a/***/b*.c", "[[:space:]]*", "**/*break.txt", "[[:punct:]]*.txt",
            "a/**\n!a/x/\n!a/x/y/\n!a/x/y/b2.h"
        ];
        try
        {
            Equal( 0, (await RunIgnoreGitAsync( root, ["init", "--quiet"], null )).ExitCode );
            foreach ( var path in paths )
            {
                var full = Path.Combine( root, path );
                Directory.CreateDirectory( Path.GetDirectoryName( full )! );
                await File.WriteAllTextAsync( full, "fixture" );
            }
            using FileIndexSource source = new( new IndexingOptions
            {
                Roots = [new IndexRootOptions
                {
                    Path = root, IncludeHidden = true, IncludeContentInSearch = false,
                    ExcludeGeneratedDirectories = false
                }]
            }, new PrivacyOptions { NeverIndex = [] } );
            foreach ( var rules in ruleSets )
            {
                await File.WriteAllTextAsync( Path.Combine( root, ".gitignore" ), rules + "\n" );
                var git = await RunIgnoreGitAsync( root,
                    ["check-ignore", "--no-index", "-z", "--stdin"], string.Join( '\0', paths ) + '\0' );
                True( git.ExitCode is 0 or 1, git.Error );
                var ignored = git.Output.Split( '\0', StringSplitOptions.RemoveEmptyEntries ).ToHashSet( StringComparer.Ordinal );
                foreach ( var path in paths )
                {
                    var actual = await source.IndexPathAsync( Path.Combine( root, path ), CancellationToken.None );
                    True( !ignored.Contains( path ) == (actual.Count > 0),
                        $"Git disagreement for '{path}' with rules '{rules}'" );
                }
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task NestedRepositoryIgnoresAndOverridesAreConsistentAsync( )
    {
        var root = Path.Combine( Path.GetTempPath( ), $"commanddeck-nested-ignore-{Guid.NewGuid( ):N}" );
        Directory.CreateDirectory( Path.Combine( root, "nested" ) );
        try
        {
            Equal( 0, (await RunIgnoreGitAsync( root, ["init", "--quiet"], null )).ExitCode );
            await File.WriteAllTextAsync( Path.Combine( root, ".gitignore" ), "*.log\nblocked/\n" );
            await File.WriteAllTextAsync( Path.Combine( root, "nested/.gitignore" ), "!keep.log\n/local.txt\n" );
            Directory.CreateDirectory( Path.Combine( root, ".git/info" ) );
            await File.WriteAllTextAsync( Path.Combine( root, ".git/info/exclude" ), "*.tmp\n" );
            string[] paths = ["nested/keep.log", "nested/other.log", "nested/local.txt", "nested/temp.tmp", "nested/source.txt"];
            foreach ( var relative in paths )
            {
                await File.WriteAllTextAsync( Path.Combine( root, relative ), "fixture" );
            }
            Equal( 0, (await RunIgnoreGitAsync( root, ["add", "--force", "nested/other.log"], null )).ExitCode );
            var options = new IndexingOptions
            {
                Roots = [new IndexRootOptions { Path = Path.Combine( root, "nested" ), IncludeHidden = true }]
            };
            using FileIndexSource source = new( options, new PrivacyOptions { NeverIndex = [] } );
            var scanned = await source.IndexDirectoryAsync( Path.Combine( root, "nested" ), CancellationToken.None );
            True( scanned.Any( value => value.Title == "keep.log" ) );
            True( scanned.Any( value => value.Title == "source.txt" ) );
            True( scanned.All( value => value.Title is not ("other.log" or "local.txt" or "temp.tmp") ) );

            IndexingOptions overridden = new( )
            {
                Roots = [new IndexRootOptions
                {
                    Path = root, IncludeHidden = true,
                    IncludeGlobs = ["nested/other.log", "blocked/keep.txt"],
                    ExcludeGlobs = ["blocked/denied.txt"]
                }]
            };
            Directory.CreateDirectory( Path.Combine( root, "blocked" ) );
            foreach ( var file in new[] { "keep.txt", "other.txt", "denied.txt" } )
            {
                await File.WriteAllTextAsync( Path.Combine( root, "blocked", file ), "fixture" );
            }
            using FileIndexSource included = new( overridden, new PrivacyOptions { NeverIndex = [] } );
            var overriddenItems = await included.IndexDirectoryAsync( root, CancellationToken.None );
            True( overriddenItems.Any( value => value.Title == "other.log" ) );
            True( overriddenItems.Any( value => value.Title == "keep.txt" ) );
            True( overriddenItems.All( value => value.Title is not ("other.txt" or "denied.txt") ) );
            Equal( 0, (await included.IndexPathAsync( Path.Combine( root, "blocked/other.txt" ), CancellationToken.None )).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunIgnoreGitAsync(
        string directory, IReadOnlyList<string> arguments, string? input )
    {
        ProcessStartInfo start = new( "git" )
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        foreach ( var variable in start.Environment.Keys.Where( static key => key.StartsWith( "GIT_", StringComparison.Ordinal ) ).ToArray( ) )
        {
            start.Environment.Remove( variable );
        }
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["GIT_TEMPLATE_DIR"] = "";
        start.Environment["LC_ALL"] = "C";
        foreach ( var argument in arguments )
        {
            start.ArgumentList.Add( argument );
        }
        using var process = Process.Start( start )!;
        using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 5 ) );
        var output = process.StandardOutput.ReadToEndAsync( timeout.Token );
        var error = process.StandardError.ReadToEndAsync( timeout.Token );
        if ( input is not null )
        {
            await process.StandardInput.WriteAsync( input.AsMemory( ), timeout.Token );
        }
        process.StandardInput.Close( );
        await process.WaitForExitAsync( timeout.Token );
        return (process.ExitCode, await output, await error);
    }
}
