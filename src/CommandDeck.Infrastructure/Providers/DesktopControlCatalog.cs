namespace CommandDeck;

// A built-in command that runs one literal argv. Its first argument, and the
// program it starts when that is a launcher, must be on PATH for the row to appear.
internal sealed record DesktopControl(
    string Id,
    string Title,
    string Category,
    string Keywords,
    string Icon,
    IReadOnlyList<string> Argv,
    string? Program = null )
{
    public string Tool => Program ?? Argv[0];

    public IReadOnlyList<string> RequiredTools => Program is null ? [Argv[0]] : [Program, Argv[0]];
}

// Audio, media, radio, and color controls. wpctl targets the default PipeWire
// sink and source, playerctl the active MPRIS player, and rfkill the soft-block
// switch that Omarchy's own Bluetooth power script uses.
internal static class DesktopControlCatalog
{
    private const string Sink = "@DEFAULT_AUDIO_SINK@";
    private const string Source = "@DEFAULT_AUDIO_SOURCE@";
    private const string AudioKeywords = "volume sound audio speaker output";
    private const string MediaKeywords = "media music player song playerctl";
    private const int VolumeStep = 5;

    public static IReadOnlyList<DesktopControl> System { get; } =
    [
        new( "volume-up", "Volume Up", "Audio", AudioKeywords + " louder raise increase", "audio-volume-high",
            ["wpctl", "set-volume", "-l", "1.0", Sink, $"{VolumeStep}%+"] ),
        new( "volume-down", "Volume Down", "Audio", AudioKeywords + " quieter lower decrease", "audio-volume-low",
            ["wpctl", "set-volume", Sink, $"{VolumeStep}%-"] ),
        new( "volume-mute", "Toggle Mute", "Audio", AudioKeywords + " mute unmute silence", "audio-volume-muted",
            ["wpctl", "set-mute", Sink, "toggle"] ),
        SetVolume( 0 ),
        SetVolume( 25 ),
        SetVolume( 50 ),
        SetVolume( 75 ),
        SetVolume( 100 ),
        new( "microphone-mute", "Toggle Microphone Mute", "Microphone", "mic microphone input mute unmute",
            "microphone-sensitivity-muted", ["wpctl", "set-mute", Source, "toggle"] ),
        new( "media-play-pause", "Play / Pause Media", "Media", MediaKeywords + " play pause resume",
            "media-playback-start", ["playerctl", "play-pause"] ),
        new( "media-next", "Next Track", "Media", MediaKeywords + " next skip forward",
            "media-skip-forward", ["playerctl", "next"] ),
        new( "media-previous", "Previous Track", "Media", MediaKeywords + " previous back rewind",
            "media-skip-backward", ["playerctl", "previous"] ),
        new( "wifi-toggle", "Toggle Wi-Fi", "Wireless", "wifi wireless wlan network radio on off",
            "network-wireless", ["rfkill", "toggle", "wlan"] ),
        new( "bluetooth-toggle", "Toggle Bluetooth", "Wireless", "bluetooth bt wireless radio on off",
            "bluetooth-active", ["rfkill", "toggle", "bluetooth"] )
    ];

    // hyprpicker freezes the screen as it starts, so it has to start after the
    // launcher overlay has closed. A one-shot user timer delays it by 300 ms
    // without a shell; systemd-run returns once the timer exists.
    public static DesktopControl ColorPicker { get; } = new(
        "color-picker", "Pick Color", "Color", "colour picker eyedropper pipette hex rgb screen pixel hyprpicker",
        "color-select",
        ["systemd-run", "--user", "--quiet", "--collect", "--on-active=300ms", "--timer-property=AccuracySec=10ms",
            "--", "hyprpicker", "--autocopy"],
        Program: "hyprpicker" );

    private static DesktopControl SetVolume( int percent ) => new(
        $"volume-{percent}",
        $"Set Volume to {percent}%",
        "Audio",
        AudioKeywords + " set level percent",
        percent == 0 ? "audio-volume-muted" : "audio-volume-medium",
        ["wpctl", "set-volume", Sink, $"{percent}%"] );
}
