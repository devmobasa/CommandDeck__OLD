using System.Collections.Frozen;
using System.Runtime.InteropServices;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck;

public static class ExtensionRuntimeSupport
{
    // ARM64 manifests can be inspected, but execution remains gated on acceptance
    // on real ARM64 hardware. Do not infer support from an ELF parser or loader.
    public static IReadOnlySet<string> SupportedRids { get; } = (OperatingSystem.IsLinux( ) && RuntimeInformation.ProcessArchitecture == Architecture.X64
        ? new[] { ExtensionTargets.LinuxX64 } : []).ToFrozenSet( StringComparer.Ordinal );

    public static ExtensionTarget? Select( ExtensionManifest manifest ) => SupportedRids
        .Select( rid => manifest.Targets.GetValueOrDefault( rid ) )
        .FirstOrDefault( target => target is not null && target.RuntimeProfile == ExtensionTargets.LinuxGlibcInvariantV1 &&
            target.Deployment is ExtensionTargets.NativeAot or ExtensionTargets.SelfContained );

    public static ExtensionTarget Require( ExtensionManifest manifest )
    {
        var target = Select( manifest );
        if ( target is null )
        {
            throw new InvalidOperationException( "This package has no target this machine can run (supported: " + string.Join( ", ", SupportedRids ) + ")." );
        }
        return target;
    }
}
