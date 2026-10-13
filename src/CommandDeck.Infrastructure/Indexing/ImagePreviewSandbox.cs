namespace CommandDeck;

/// <summary>Host-owned, network-isolated runtime for decoding a single read-only snapshot.</summary>
internal static class ImagePreviewSandbox
{
    internal const string Executable = "/usr/bin/bwrap";
    internal const string Decoder = "/usr/bin/magick";
    internal const string Input = "/input";

    internal static bool IsAvailable( ) => File.Exists( Executable ) && File.Exists( Decoder );

    internal static IReadOnlyList<string> BuildArguments( string snapshot, IReadOnlyList<string> decoderArguments ) =>
    [
        "--unshare-all", "--unshare-user", "--disable-userns", "--die-with-parent", "--new-session",
        "--cap-drop", "ALL", "--clearenv",
        "--ro-bind", "/usr", "/usr", "--ro-bind-try", "/lib", "/lib", "--ro-bind-try", "/lib64", "/lib64",
        "--ro-bind-try", "/etc/ld.so.cache", "/etc/ld.so.cache",
        "--ro-bind-try", "/etc/ImageMagick-7", "/etc/ImageMagick-7",
        "--ro-bind", snapshot, Input, "--proc", "/proc", "--dev", "/dev",
        "--size", "16777216", "--tmpfs", "/tmp", "--remount-ro", "/",
        "--setenv", "HOME", "/tmp", "--setenv", "TMPDIR", "/tmp",
        "--setenv", "PATH", "/usr/bin", "--setenv", "MAGICK_THREAD_LIMIT", "1",
        "--chdir", "/tmp", "--", Decoder, .. decoderArguments
    ];
}
