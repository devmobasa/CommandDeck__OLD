namespace CommandDeck;

// Placement names shared by the search rows and the Hyprland window action.
public static class WindowPlacements
{
    public const string LeftHalf = "left-half";
    public const string RightHalf = "right-half";
    public const string TopHalf = "top-half";
    public const string BottomHalf = "bottom-half";
    public const string FirstThird = "first-third";
    public const string CenterThird = "center-third";
    public const string LastThird = "last-third";
    public const string FirstTwoThirds = "first-two-thirds";
    public const string LastTwoThirds = "last-two-thirds";
    public const string Center = "center";
    public const string Maximize = "maximize";
    public const string AlmostMaximize = "almost-maximize";
}

// A rectangle in Hyprland's global layout coordinates (logical pixels).
internal readonly record struct WindowRect( int X, int Y, int Width, int Height );

// One monitor as `hyprctl -j monitors` reports it: the mode in physical pixels,
// the scale and transform that map it to logical pixels, and the space that bars
// and other exclusive layers reserve on each edge, already in logical pixels.
internal sealed record MonitorLayout(
    int X,
    int Y,
    int PixelWidth,
    int PixelHeight,
    double Scale,
    int Transform,
    int ReservedLeft,
    int ReservedTop,
    int ReservedRight,
    int ReservedBottom );

// Pure placement arithmetic: no compositor calls, so every rectangle is checked
// by a golden table.
internal static class HyprlandWindowGeometry
{
    // Halves, thirds, and the 5% almost-maximize margin are exact sixtieths.
    private const int Grid = 60;

    // Left, top, right, and bottom edges, in sixtieths of the work area.
    private static readonly Dictionary<string, (int Left, int Top, int Right, int Bottom)> Fractions =
        new( StringComparer.Ordinal )
        {
            [WindowPlacements.LeftHalf] = (0, 0, 30, 60),
            [WindowPlacements.RightHalf] = (30, 0, 60, 60),
            [WindowPlacements.TopHalf] = (0, 0, 60, 30),
            [WindowPlacements.BottomHalf] = (0, 30, 60, 60),
            [WindowPlacements.FirstThird] = (0, 0, 20, 60),
            [WindowPlacements.CenterThird] = (20, 0, 40, 60),
            [WindowPlacements.LastThird] = (40, 0, 60, 60),
            [WindowPlacements.FirstTwoThirds] = (0, 0, 40, 60),
            [WindowPlacements.LastTwoThirds] = (20, 0, 60, 60),
            [WindowPlacements.Maximize] = (0, 0, 60, 60),
            [WindowPlacements.AlmostMaximize] = (3, 3, 57, 57)
        };

    public static bool IsPlacement( string placement ) =>
        placement == WindowPlacements.Center || Fractions.ContainsKey( placement );

    // The monitor's logical box minus reserved edges. A 90° or 270° transform
    // (odd values, flipped or not) swaps the mode's width and height.
    public static WindowRect WorkArea( MonitorLayout monitor )
    {
        if ( !(monitor.Scale > 0) || monitor.PixelWidth <= 0 || monitor.PixelHeight <= 0 )
        {
            throw new InvalidOperationException( "Hyprland reported a monitor without a usable size or scale." );
        }

        var rotated = (monitor.Transform & 1) == 1;
        var width = Logical( rotated ? monitor.PixelHeight : monitor.PixelWidth, monitor.Scale );
        var height = Logical( rotated ? monitor.PixelWidth : monitor.PixelHeight, monitor.Scale );
        WindowRect area = new(
            monitor.X + monitor.ReservedLeft,
            monitor.Y + monitor.ReservedTop,
            width - monitor.ReservedLeft - monitor.ReservedRight,
            height - monitor.ReservedTop - monitor.ReservedBottom );

        if ( area.Width <= 0 || area.Height <= 0 )
        {
            throw new InvalidOperationException( "The monitor's reserved space leaves no room for windows." );
        }

        return area;
    }

    // Adjacent placements share edges, so halves and thirds tile without gaps or
    // overlap. Center keeps the window's size, clamped to the work area.
    public static WindowRect Place( string placement, WindowRect area, int currentWidth, int currentHeight )
    {
        if ( placement == WindowPlacements.Center )
        {
            var width = Math.Clamp( currentWidth, 1, area.Width );
            var height = Math.Clamp( currentHeight, 1, area.Height );
            return new WindowRect(
                area.X + ((area.Width - width) / 2),
                area.Y + ((area.Height - height) / 2),
                width,
                height );
        }

        if ( !Fractions.TryGetValue( placement, out var fraction ) )
        {
            throw new InvalidOperationException( $"Unknown window placement '{placement}'." );
        }

        var left = Edge( area.X, area.Width, fraction.Left );
        var top = Edge( area.Y, area.Height, fraction.Top );
        var right = Edge( area.X, area.Width, fraction.Right );
        var bottom = Edge( area.Y, area.Height, fraction.Bottom );
        return new WindowRect( left, top, right - left, bottom - top );
    }

    // Hyprland divides the mode by the scale. A rounded scale such as 1.66666667
    // makes 2160 pixels 1295.9999…; the small bias keeps that at 1296.
    private static int Logical( int pixels, double scale ) =>
        ( int ) Math.Floor( (pixels / scale) + 0.01 );

    private static int Edge( int origin, int length, int sixtieths ) =>
        origin + ( int ) Math.Round( length * sixtieths / ( double ) Grid, MidpointRounding.AwayFromZero );
}
