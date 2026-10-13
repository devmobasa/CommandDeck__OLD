namespace CommandDeck;

public static class SystemdUnitActionIds
{
    public const string Logs = "logs";
    public const string Status = "status";
    public const string Edit = "edit";
    public const string Start = "start";
    public const string Stop = "stop";
    public const string Restart = "restart";
    public const string CopyUnit = "copy-unit";

    public static bool IsSupported( string id ) => id is
        Logs or Status or Edit or Start or Stop or Restart;

    public static bool ChangesUnitState( string id ) => id is Start or Stop or Restart;

    public static bool IsDestructive( string id ) => id is Stop or Restart;
}

public static class SystemdUnitActionFields
{
    public const string Unit = "unit";
    public const string Operation = "operation";
}

public static class SystemdUnitResultIds
{
    public static string ForUnit( string unit ) => "systemd:" + unit;
}
