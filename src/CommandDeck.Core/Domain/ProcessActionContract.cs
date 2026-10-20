using System.Globalization;

namespace CommandDeck;

public static class ProcessActionIds
{
    public const string Terminate = "terminate";
    public const string ForceKill = "force-kill";
    public const string CopyPid = "copy-pid";
}

// The identity a signal action carries: the PID, its start time in clock ticks
// after boot (field 22 of /proc/<pid>/stat), and the owner's user ID.
public static class ProcessActionFields
{
    public const string Pid = "pid";
    public const string StartTime = "startTime";
    public const string Uid = "uid";
}

public static class ProcessResultIds
{
    public const string Browse = "process:browse";
    public const string FilterId = "processes";

    public static string For( int pid, ulong startTicks ) =>
        string.Create( CultureInfo.InvariantCulture, $"process:{pid}:{startTicks}" );
}
