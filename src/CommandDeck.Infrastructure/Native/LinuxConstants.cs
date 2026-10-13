using System.Runtime.InteropServices;

namespace CommandDeck;

internal static class LinuxConstants
{
    // Linux's unified syscall table uses 437 for openat2 on x86_64 and aarch64.
    public const long OpenAt2 = 437;
    public const int AtCurrentDirectory = -100;
    public const int OPath = 0x200000;
    // asm-generic/fcntl.h supplies x64; arm64 overrides O_DIRECTORY in asm/fcntl.h.
    public static int ODirectory => DirectoryFlag( RuntimeInformation.ProcessArchitecture );
    internal static int DirectoryFlag( Architecture architecture ) => architecture switch
    {
        Architecture.X64 => 0x10000,
        Architecture.Arm64 => 0x4000,
        _ => throw new PlatformNotSupportedException( "Linux directory flags support x64 and arm64 only." )
    };
    public const int ONonBlocking = 0x800;
    public const int OCloseOnExec = 0x80000;
    public const ulong ResolveNoMountTraversal = 0x01;
    public const ulong ResolveNoSymlinks = 0x04;
    public const ulong ResolveBeneath = 0x08;
    public const int SetDescriptorFlags = 2;
    public const int SetStatusFlags = 4;
    public const int DescriptorCloseOnExec = 1;
    public const int TryAgain = 11;
    public const int InputOutputError = 5;
}

[StructLayout( LayoutKind.Sequential )]
internal struct LinuxOpenHow
{
    public ulong Flags;
    public ulong Mode;
    public ulong Resolve;
}
