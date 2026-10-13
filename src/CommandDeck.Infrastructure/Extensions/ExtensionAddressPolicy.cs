using System.Net;
using System.Net.Sockets;

namespace CommandDeck;

internal static class ExtensionAddressPolicy
{
    private static readonly IPNetwork[] NonPublicV4 =
    [
        IPNetwork.Parse( "0.0.0.0/8" ), IPNetwork.Parse( "10.0.0.0/8" ), IPNetwork.Parse( "100.64.0.0/10" ),
        IPNetwork.Parse( "127.0.0.0/8" ), IPNetwork.Parse( "169.254.0.0/16" ), IPNetwork.Parse( "172.16.0.0/12" ),
        IPNetwork.Parse( "192.0.0.0/16" ), IPNetwork.Parse( "192.88.99.0/24" ), IPNetwork.Parse( "192.168.0.0/16" ),
        IPNetwork.Parse( "198.18.0.0/15" ), IPNetwork.Parse( "198.51.100.0/24" ), IPNetwork.Parse( "203.0.113.0/24" ), IPNetwork.Parse( "224.0.0.0/3" )
    ];
    private static readonly IPNetwork PublicV6 = IPNetwork.Parse( "2000::/3" );
    private static readonly IPNetwork[] NonPublicV6 = [IPNetwork.Parse( "2001:db8::/32" ), IPNetwork.Parse( "2001::/24" ), IPNetwork.Parse( "2002::/16" )];
    private static readonly IPNetwork[] PrivateV4 = [IPNetwork.Parse( "10.0.0.0/8" ), IPNetwork.Parse( "172.16.0.0/12" ), IPNetwork.Parse( "192.168.0.0/16" )];
    private static readonly IPNetwork PrivateV6 = IPNetwork.Parse( "fc00::/7" );

    public static bool PublicAddress( IPAddress address )
    {
        if ( address.IsIPv4MappedToIPv6 )
        {
            return false;
        }

        return address.AddressFamily == AddressFamily.InterNetwork ? !NonPublicV4.Any( network => network.Contains( address ) ) :
            PublicV6.Contains( address ) && !NonPublicV6.Any( network => network.Contains( address ) );
    }

    public static bool PrivateAddress( IPAddress address )
    {
        if ( address.IsIPv4MappedToIPv6 )
        {
            return false;
        }
        if ( IPAddress.IsLoopback( address ) )
        {
            return true;
        }

        return address.AddressFamily == AddressFamily.InterNetwork ? PrivateV4.Any( network => network.Contains( address ) ) : PrivateV6.Contains( address );
    }
}
