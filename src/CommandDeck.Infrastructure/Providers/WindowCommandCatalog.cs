namespace CommandDeck;

// A window command for the window focused when the launcher opened. Parameter
// is the placement, monitor direction, or workspace the operation needs.
internal sealed record WindowCommand(
    string Id,
    string Title,
    string Keywords,
    string Icon,
    string Operation,
    string? Parameter = null,
    bool NeedsSecondMonitor = false );

internal static class WindowCommandCatalog
{
    private const string Arrange = "window snap move resize arrange position";
    private const string Icon = "preferences-system-windows";
    private const int Workspaces = 10;

    public static IReadOnlyList<WindowCommand> All { get; } =
    [
        Place( WindowPlacements.LeftHalf, "Left Half", "left half" ),
        Place( WindowPlacements.RightHalf, "Right Half", "right half" ),
        Place( WindowPlacements.TopHalf, "Top Half", "top upper half" ),
        Place( WindowPlacements.BottomHalf, "Bottom Half", "bottom lower half" ),
        Place( WindowPlacements.FirstThird, "First Third", "left third" ),
        Place( WindowPlacements.CenterThird, "Center Third", "middle third" ),
        Place( WindowPlacements.LastThird, "Last Third", "right third" ),
        Place( WindowPlacements.FirstTwoThirds, "First Two Thirds", "left two-thirds" ),
        Place( WindowPlacements.LastTwoThirds, "Last Two Thirds", "right two-thirds" ),
        Place( WindowPlacements.Center, "Center Window", "middle centre" ),
        Place( WindowPlacements.Maximize, "Maximize Window", "fill full size large" ),
        Place( WindowPlacements.AlmostMaximize, "Almost Maximize Window", "nearly fill large" ),
        new( "toggle-floating", "Toggle Floating", "window float floating tiled pop out", Icon,
            HyprlandActionOperations.ToggleFloating ),
        new( "tile", "Tile Window", "window tile tiled unfloat dock restore", Icon,
            HyprlandActionOperations.TileWindow ),
        new( "toggle-fullscreen", "Toggle Fullscreen", "window fullscreen full screen", "view-fullscreen",
            HyprlandActionOperations.ToggleFullscreen ),
        new( "next-monitor", "Move Window to Next Monitor", "window send display screen monitor output", Icon,
            HyprlandActionOperations.MoveWindowToMonitor, HyprlandActionOperations.NextMonitor, NeedsSecondMonitor: true ),
        new( "previous-monitor", "Move Window to Previous Monitor", "window send display screen monitor output", Icon,
            HyprlandActionOperations.MoveWindowToMonitor, HyprlandActionOperations.PreviousMonitor, NeedsSecondMonitor: true ),
        .. Enumerable.Range( 1, Workspaces ).Select( static number => new WindowCommand(
            $"workspace-{number}",
            $"Move Window to Workspace {number}",
            "window send workspace desktop",
            Icon,
            HyprlandActionOperations.MoveWindowToWorkspace,
            HyprlandWindowState.Number( number ) ) )
    ];

    private static WindowCommand Place( string placement, string title, string keywords ) =>
        new( placement, title, Arrange + " " + keywords, Icon, HyprlandActionOperations.PlaceWindow, placement );
}
