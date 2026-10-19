namespace CommandDeck;

internal static class HyprlandActionOperations
{
    internal const string OperationKey = "operation";
    internal const string ArgumentKey = "argument";
    // The placement, monitor direction, or workspace a window command needs.
    internal const string ParameterKey = "parameter";
    internal const string SwitchWorkspace = "switch-workspace";
    internal const string FocusWindow = "focus-window";
    internal const string CloseWindow = "close-window";
    internal const string PlaceWindow = "place-window";
    internal const string ToggleFloating = "toggle-floating";
    internal const string TileWindow = "tile-window";
    internal const string ToggleFullscreen = "toggle-fullscreen";
    internal const string MoveWindowToMonitor = "move-window-monitor";
    internal const string MoveWindowToWorkspace = "move-window-workspace";
    internal const string NextMonitor = "next";
    internal const string PreviousMonitor = "previous";

    internal static bool IsWindowCommand( string operation ) => operation is
        PlaceWindow or ToggleFloating or TileWindow or ToggleFullscreen or
        MoveWindowToMonitor or MoveWindowToWorkspace;
}
