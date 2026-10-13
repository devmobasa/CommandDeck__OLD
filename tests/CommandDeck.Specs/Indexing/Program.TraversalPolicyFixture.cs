namespace CommandDeck.Specs;

public static partial class Program
{
    // Privacy rules at several depths, hidden entries, generated trees, nested
    // repository rules with negation, and root inclusion and exclusion globs.
    private static void CreateTraversalPolicyFixture( string root )
    {
        void Write( string relative, string content = "fixture" )
        {
            var path = Path.Combine( root, relative );
            Directory.CreateDirectory( Path.GetDirectoryName( path )! );
            File.WriteAllText( path, content );
        }

        Write( "notes.txt" );
        Write( ".hidden.txt" );
        Write( ".config/app.conf" );
        Write( ".env" );
        Write( "a/b/c/api.key" );
        Write( "a/b/c/report.txt" );
        Write( "a/b/c/d/e/my-secret-plan.txt" );
        Write( "a/b/c/d/e/f/deep.txt" );
        Write( "a/b/c/d/credentials-archive/g/h/buried.txt" );
        Write( "a/credentials/inside.txt" );
        Write( "stash/stuff.txt" );
        Write( "stash/more/stuff.txt" );

        Write( "web/package.json", "{}" );
        Write( "web/dist/bundle.js" );
        Write( "web/node_modules/pkg/index.js" );
        Write( "web/src/app.js" );
        Write( "py/pyproject.toml", "[project]" );
        Write( "py/.venv/lib.py" );
        Write( "py/venv/lib.py" );
        Write( "py/env2/pyvenv.cfg" );
        Write( "py/env2/lib.py" );

        Write( "repo/.git/info/exclude", "excluded-by-info.txt\n" );
        Write( "repo/.gitignore", "*.log\nbuild/\n!keep.log\n" );
        Write( "repo/app.log" );
        Write( "repo/keep.log" );
        Write( "repo/build/out.txt" );
        Write( "repo/excluded-by-info.txt" );
        Write( "repo/src/.gitignore", "local.txt\n" );
        Write( "repo/src/local.txt" );
        Write( "repo/src/main.cs" );
        Directory.CreateDirectory( Path.Combine( root, "repo/nested/.git" ) );
        Write( "repo/nested/.gitignore", "*.tmp\n" );
        Write( "repo/nested/x.tmp" );
        Write( "repo/nested/app.log" );

        Write( "globs/excluded-dir/file.txt" );
        Write( "globs/manual/package.json", "{}" );
        Write( "globs/manual/dist/manual.pdf" );
        Write( "globs/manual/dist/other.js" );
    }

    private static (IndexingOptions Indexing, PrivacyOptions Privacy) TraversalPolicyOptions( string root )
    {
        IndexingOptions indexing = new( )
        {
            Roots =
            [
                new IndexRootOptions
                {
                    Path = root,
                    IncludeContentInSearch = false,
                    ExcludeGlobs = ["globs/excluded-dir"],
                    IncludeGlobs = ["globs/manual/dist/manual.pdf"]
                }
            ]
        };
        PrivacyOptions privacy = new( )
        {
            NeverIndex = [.. new PrivacyOptions( ).NeverIndex, Path.Combine( root, "stash" )]
        };

        return (indexing, privacy);
    }

    private static int CompareTraversalWithFullPolicy(
        IndexExclusionPolicy policy,
        AuthorizedDirectory directory,
        CanonicalPathBoundary boundary,
        IndexRootOptions options )
    {
        var compared = 0;
        foreach ( var entry in Directory.EnumerateFileSystemEntries( directory.Path ).Order( StringComparer.Ordinal ) )
        {
            var isDirectory = Directory.Exists( entry );
            var traversal = policy.MatchChild( directory, entry, isDirectory, out var child );
            var full = policy.Match( boundary, options, entry, entry, isDirectory );
            Equal( $"{entry}: {DescribeExclusion( full )}", $"{entry}: {DescribeExclusion( traversal )}" );
            compared++;

            if ( child is not null )
            {
                compared += CompareTraversalWithFullPolicy( policy, child, boundary, options );
            }
            else if ( isDirectory )
            {
                compared += AssertExcludedBelow( policy, entry, boundary, options );
            }
        }

        return compared;
    }

    private static int AssertExcludedBelow(
        IndexExclusionPolicy policy,
        string directory,
        CanonicalPathBoundary boundary,
        IndexRootOptions options )
    {
        var compared = 0;
        foreach ( var entry in Directory.EnumerateFileSystemEntries( directory ) )
        {
            var isDirectory = Directory.Exists( entry );
            var full = policy.Match( boundary, options, entry, entry, isDirectory );
            Equal( $"{entry}: excluded", $"{entry}: {(full is null ? "admitted" : "excluded")}" );
            compared++;

            if ( isDirectory )
            {
                compared += AssertExcludedBelow( policy, entry, boundary, options );
            }
        }

        return compared;
    }

    private static string DescribeExclusion( IndexExclusion? exclusion ) =>
        exclusion is null ? "admitted" : $"{exclusion.Reason} {exclusion.Rule}";
}
