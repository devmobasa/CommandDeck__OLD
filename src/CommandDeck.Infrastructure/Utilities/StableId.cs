using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

public static class StableId
{
    public static string Create( string prefix, string key )
    {
        var hash = SHA256.HashData( Encoding.UTF8.GetBytes( key ) );
        return $"{prefix}:{Convert.ToHexString( hash.AsSpan( 0, 12 ) ).ToLowerInvariant( )}";
    }
}
