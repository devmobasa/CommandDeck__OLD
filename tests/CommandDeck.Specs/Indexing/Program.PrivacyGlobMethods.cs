using System.IO.Enumeration;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void StarOnlyPrivacyGlobsMatchLikeTheRuntimeMatcher( )
    {
        string[] patterns =
        [
            .. new PrivacyOptions( ).NeverIndex
                .Where( static pattern => pattern.Contains( '*', StringComparison.Ordinal ) )
                .Select( static pattern => pattern.Replace( "**", "*", StringComparison.Ordinal ) ),
            "*", "a*", "*a", "*a*", "a*b", "*a*b*", "a*a*a", "*/x/*", "ab*ab", "*.*", "a**b"
        ];
        string[] values =
        [
            "", "/", "a", "aa", "ab", "abab", "aaa", "credentials", "/home/user/.env",
            "/home/user/.env.local", "/home/user/.envrc", "/x/credentials/y", "/x/my-secret-plan.txt",
            "/x/key", "/x/a.pem", "/x/a.pem/b", "/x/Cookies", "/x/Login Data", "/x/.env.", "/.env"
        ];
        foreach ( var pattern in patterns )
        {
            foreach ( var value in values )
            {
                AssertSameGlobVerdict( pattern, value );
            }
        }

        // Random star patterns and values over a small alphabet reach the
        // overlapping and backtracking cases.
        Random random = new( 20_260_929 );
        for ( var trial = 0; trial < 20_000; trial++ )
        {
            var pattern = RandomText( random, "ab/.*", random.Next( 1, 8 ) );
            if ( !pattern.Contains( '*', StringComparison.Ordinal ) )
            {
                continue;
            }

            AssertSameGlobVerdict( pattern, RandomText( random, "ab/.", random.Next( 0, 15 ) ) );
        }

        static void AssertSameGlobVerdict( string pattern, string value )
        {
            var expected = FileSystemName.MatchesSimpleExpression( pattern, value, ignoreCase: false );
            var actual = PathPolicy.MatchesStarPattern( value, pattern.Split( '*' ) );
            Equal( $"'{pattern}' ~ '{value}': {expected}", $"'{pattern}' ~ '{value}': {actual}" );
        }

        static string RandomText( Random random, string alphabet, int length ) =>
            new( [.. Enumerable.Range( 0, length ).Select( _ => alphabet[random.Next( alphabet.Length )] )] );
    }
}
