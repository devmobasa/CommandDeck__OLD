using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionNativeAndAddressPolicyMatchReference( )
    {
        Equal( 24, Unsafe.SizeOf<LinuxOpenHow>( ) );
        Equal( 0x10000, LinuxConstants.DirectoryFlag( Architecture.X64 ) );
        Equal( 0x4000, LinuxConstants.DirectoryFlag( Architecture.Arm64 ) );
        var random = new Random( 61723 );
        for ( var index = 0; index < 100000; index++ )
        {
            var v4 = new byte[4];
            var v6 = new byte[16];
            random.NextBytes( v4 );
            random.NextBytes( v6 );
            Check( new( v4 ) );
            Check( new( v6 ) );
            Check( new IPAddress( v4 ).MapToIPv6( ) );
        }
        foreach ( var cidr in new[] { "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
            "192.0.0.0/16", "192.88.99.0/24", "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/3",
            "2000::/3", "2001:db8::/32", "2001::/24", "2002::/16", "fc00::/7", "::1/128" } )
        {
            var network = IPNetwork.Parse( cidr );
            var first = network.BaseAddress.GetAddressBytes( );
            var last = first.ToArray( );
            for ( var bit = network.PrefixLength; bit < last.Length * 8; bit++ )
            {
                last[bit / 8] |= ( byte ) (1 << (7 - bit % 8));
            }
            Check( new( first ) );
            Check( new( last ) );
            var before = first.ToArray( );
            var after = last.ToArray( );
            for ( var index = before.Length - 1; index >= 0; index-- )
            {
                if ( before[index]-- != 0 )
                {
                    break;
                }
            }
            for ( var index = after.Length - 1; index >= 0; index-- )
            {
                if ( after[index]++ != 255 )
                {
                    break;
                }
            }
            Check( new( before ) );
            Check( new( after ) );
        }
        static void Check( IPAddress address )
        {
            Equal( ReferencePublicAddress( address ), ExtensionAddressPolicy.PublicAddress( address ) );
            Equal( ReferencePrivateAddress( address ), ExtensionAddressPolicy.PrivateAddress( address ) );
        }
    }

    // Frozen reference from the clean e1a46ad snapshot. Changes to network policy are separate work.
    internal static bool ReferencePublicAddress( IPAddress address )
    {
        if ( address.IsIPv4MappedToIPv6 )
        {
            return false;
        }
        var bytes = address.GetAddressBytes( );
        if ( bytes.Length == 16 )
        {
            return (bytes[0] & 0xe0) == 0x20 && !IPAddress.Parse( "2001:db8::" ).GetAddressBytes( ).AsSpan( 0, 4 ).SequenceEqual( bytes.AsSpan( 0, 4 ) )
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0) && !(bytes[0] == 0x20 && bytes[1] == 0x02);
        }
        return PublicV4( bytes );
    }
    private static bool PublicV4( byte[] bytes ) => bytes[0] is not (0 or 10 or 127) && bytes[0] < 224 &&
            !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) && !(bytes[0] == 169 && bytes[1] == 254) &&
            !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) && !(bytes[0] == 192 && (bytes[1] is 0 or 168 || bytes[1] == 88 && bytes[2] == 99)) &&
            !(bytes[0] == 198 && (bytes[1] is 18 or 19 || bytes[1] == 51 && bytes[2] == 100)) && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
    private static bool ReferencePrivateAddress( IPAddress address )
    {
        if ( address.IsIPv4MappedToIPv6 )
        {
            return false;
        }
        if ( IPAddress.IsLoopback( address ) )
        {
            return true;
        }
        var bytes = address.GetAddressBytes( );
        return bytes.Length == 16 ? (bytes[0] & 0xfe) == 0xfc : bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168;
    }

}
