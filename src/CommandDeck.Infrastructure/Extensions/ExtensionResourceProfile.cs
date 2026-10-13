using System.Globalization;

namespace CommandDeck;

/// <summary>One resource policy used both to configure and verify the systemd boundary.</summary>
internal sealed record ExtensionResourceProfile( long MemoryBytes, int Tasks, int CpuQuotaPercent, int? RuntimeMaxSeconds )
{
    public static ExtensionResourceProfile Interactive { get; } = new( 512L << 20, 64, 100, 1800 );
    public static ExtensionResourceProfile Search { get; } = new( 128L << 20, 32, 50, 5 );
    public static ExtensionResourceProfile PackageSlice { get; } = new( 512L << 20, 64, 100, null );
    public static ExtensionResourceProfile GlobalSlice { get; } = new( 1L << 30, 256, 200, null );

    public string[] SystemdProperties( )
    {
        var memory = MemoryBytes >= 1L << 30 ? (MemoryBytes >> 30).ToString( CultureInfo.InvariantCulture ) + "G" :
            (MemoryBytes >> 20).ToString( CultureInfo.InvariantCulture ) + "M";
        List<string> properties =
        [
            "MemoryMax=" + memory, "MemorySwapMax=0", "TasksMax=" + Tasks.ToString( CultureInfo.InvariantCulture ),
            "CPUQuota=" + CpuQuotaPercent.ToString( CultureInfo.InvariantCulture ) + "%"
        ];
        if ( RuntimeMaxSeconds is { } seconds )
        {
            properties.Add( "RuntimeMaxSec=" + seconds.ToString( CultureInfo.InvariantCulture ) );
        }

        return [.. properties];
    }

    public void Verify( string root )
    {
        if ( File.ReadAllText( Path.Combine( root, "memory.max" ) ).Trim( ) != MemoryBytes.ToString( CultureInfo.InvariantCulture ) ||
             File.ReadAllText( Path.Combine( root, "pids.max" ) ).Trim( ) != Tasks.ToString( CultureInfo.InvariantCulture ) ||
             File.ReadAllText( Path.Combine( root, "cpu.max" ) ).StartsWith( "max", StringComparison.Ordinal ) ||
             File.ReadAllText( Path.Combine( root, "memory.swap.max" ) ).Trim( ) != "0" )
        {
            throw new PlatformNotSupportedException( "The extension resource controllers could not enforce the required limits." );
        }
    }
}

internal static class ExtensionSyscallFilter
{
    public static string Deny( bool allowDebug ) => allowDebug ?
        "~@module @raw-io @reboot @swap @obsolete @cpu-emulation" :
        "~@debug @module @raw-io @reboot @swap @obsolete @cpu-emulation";
}
