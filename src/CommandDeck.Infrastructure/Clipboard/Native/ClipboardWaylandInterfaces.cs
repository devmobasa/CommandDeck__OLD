using System.Runtime.InteropServices;

namespace CommandDeck;

internal static unsafe class ClipboardWaylandInterfaces
{
    // These small, immutable ABI tables have the lifetime of the clipboard child.
    // libwayland retains their pointers in live proxies, including event signatures.
    private static readonly nint Library = NativeLibrary.Load( "libwayland-client.so.0" );
    private static readonly Dictionary<string, nint> Custom = Build( );

    internal static nint Get( string name ) => Custom.TryGetValue( name, out var value ) ? value : GetCore( name );

    private static nint GetCore( string name )
    {
        if ( name is not ("wl_registry" or "wl_callback" or "wl_seat") )
        {
            throw new InvalidOperationException( "Unsupported core Wayland interface." );
        }

        return NativeLibrary.GetExport( Library, name + "_interface" );
    }

    private static Dictionary<string, nint> Build( )
    {
        Dictionary<string, nint> interfaces = new( StringComparer.Ordinal );
        foreach ( var definition in ClipboardWaylandDefinitions.Interfaces )
        {
            var pointer = ( WaylandInterface* ) NativeMemory.AllocZeroed( ( nuint ) sizeof( WaylandInterface ) );
            pointer->Name = Marshal.StringToCoTaskMemUTF8( definition.Name );
            pointer->Version = definition.Version;
            interfaces.Add( definition.Name, ( nint ) pointer );
        }

        foreach ( var definition in ClipboardWaylandDefinitions.Interfaces )
        {
            var pointer = ( WaylandInterface* ) interfaces[definition.Name];
            pointer->RequestCount = definition.Requests.Length;
            pointer->Requests = BuildMessages( definition.Requests, interfaces );
            pointer->EventCount = definition.Events.Length;
            pointer->Events = BuildMessages( definition.Events, interfaces );
        }

        return interfaces;
    }

    private static nint BuildMessages( WaylandMessageDefinition[] definitions, Dictionary<string, nint> interfaces )
    {
        var messages = ( WaylandMessage* ) NativeMemory.AllocZeroed( ( nuint ) definitions.Length, ( nuint ) sizeof( WaylandMessage ) );
        for ( var index = 0; index < definitions.Length; index++ )
        {
            var definition = definitions[index];
            messages[index].Name = Marshal.StringToCoTaskMemUTF8( definition.Name );
            messages[index].Signature = Marshal.StringToCoTaskMemUTF8( definition.Signature );
            var types = ( nint* ) NativeMemory.AllocZeroed( ( nuint ) definition.Types.Length, ( nuint ) sizeof( nint ) );
            for ( var argument = 0; argument < definition.Types.Length; argument++ )
            {
                var name = definition.Types[argument];
                if ( name is not null )
                {
                    types[argument] = interfaces.TryGetValue( name, out var custom ) ? custom : GetCore( name );
                }
            }

            messages[index].Types = ( nint ) types;
        }

        return ( nint ) messages;
    }
}
