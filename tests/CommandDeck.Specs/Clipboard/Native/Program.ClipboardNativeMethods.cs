using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardWaylandMetadataPreservesAbiAndPinnedXml( )
    {
        var root = FindRepositoryRoot( );
        var source = File.ReadAllBytes( Path.Combine( root, "third_party/wayland/ext-data-control-v1.xml" ) );
        var digest = Convert.ToHexStringLower( SHA256.HashData( source ) );
        Equal( "293deba5dfdb974fbcf8dcf891a30a51a36a8bba0047dc6bdf6a03194089b7fb", digest );
        True( File.ReadAllText( Path.Combine( root, "src/CommandDeck.Infrastructure/Clipboard/Native/ClipboardWaylandDefinitions.g.cs" ) ).Contains( digest, StringComparison.Ordinal ),
            "generated metadata identifies its reviewed protocol source" );
        Equal( 4, ClipboardWaylandDefinitions.Interfaces.Length );
        var definitions = ClipboardWaylandDefinitions.Interfaces.ToDictionary( definition => definition.Name, StringComparer.Ordinal );
        var device = definitions["ext_data_control_device_v1"];
        Equal( "set_selection", device.Requests[0].Name );
        Equal( "?o", device.Requests[0].Signature );
        Equal( "ext_data_control_source_v1", device.Requests[0].Types.Single( ) );
        Equal( "data_offer", device.Events[0].Name );
        Equal( "n", device.Events[0].Signature );
        Equal( "ext_data_control_offer_v1", device.Events[0].Types.Single( ) );
        Equal( "?o", device.Events[1].Signature );
        var manager = definitions["ext_data_control_manager_v1"];
        Equal( "get_data_device", manager.Requests[1].Name );
        Equal( "no", manager.Requests[1].Signature );
        Equal( "wl_seat", manager.Requests[1].Types[1] );
        Equal( "sh", definitions["ext_data_control_source_v1"].Events[0].Signature );
        Equal( "sh", definitions["ext_data_control_offer_v1"].Requests[0].Signature );
        Equal( 8, IntPtr.Size );
        Equal( 8, Marshal.SizeOf<WaylandArgument>( ) );
        Equal( 8, Marshal.SizeOf<PollDescriptor>( ) );
        Equal( 24, Marshal.SizeOf<WaylandMessage>( ) );
        Equal( 40, Marshal.SizeOf<WaylandInterface>( ) );
        Throws<InvalidOperationException>( ( ) => ClipboardWaylandInterfaces.Get( "unreviewed_interface" ) );
    }
}
