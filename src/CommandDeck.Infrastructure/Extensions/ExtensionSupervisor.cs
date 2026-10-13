using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CommandDeck;

/// <summary>Starts only confined sessions. The daemon supplies all executable and resource policy inputs.</summary>
public sealed class ExtensionSupervisor( string hostExecutable, IReadOnlyList<string> hostArguments ) : IExtensionLauncher
{
    private const string GlobalSlice = "commanddeck-extensions.slice";
    private static readonly SemaphoreSlim HierarchyGate = new( 1, 1 );

    public async ValueTask<IExtensionProcess> StartAsync( ExtensionLaunchRequest request, CancellationToken cancellationToken )
    {
        var artifactDirectory = request.ArtifactDirectory;
        var entrypoint = request.Entrypoint;
        var installationKey = request.InstallationKey;
        var debugSocket = request.DebugSocket;
        if ( request.Profile is not (ExtensionLaunchProfile.Interactive or ExtensionLaunchProfile.Search) || request.Profile == ExtensionLaunchProfile.Search && debugSocket is not null )
        {
            throw new ArgumentException( "Invalid extension launch profile.", nameof( request ) );
        }
        var profile = request.Profile switch
        {
            ExtensionLaunchProfile.Interactive => ExtensionResourceProfile.Interactive,
            ExtensionLaunchProfile.Search => ExtensionResourceProfile.Search,
            _ => throw new ArgumentException( "Invalid extension launch profile.", nameof( request ) )
        };

        if ( !OperatingSystem.IsLinux( ) || !File.Exists( "/usr/bin/bwrap" ) ||
             !File.Exists( "/usr/bin/systemd-run" ) || !File.Exists( "/sys/fs/cgroup/cgroup.controllers" ) )
        {
            throw new PlatformNotSupportedException( "Extensions require Bubblewrap and a systemd user cgroup v2 hierarchy." );
        }
        _ = ExtensionSandbox.BuildArguments( artifactDirectory, entrypoint );
        var identity = Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( installationKey ) ) )[..24].ToLowerInvariant( );
        var packageSlice = $"commanddeck-extensions-p{identity}.slice";
        await PrepareHierarchyAsync( packageSlice, cancellationToken ).ConfigureAwait( false );
        var unit = $"commanddeck-extension-{Guid.NewGuid( ):N}.service";
        using var owner = Process.GetCurrentProcess( );
        List<string> arguments =
        [
            "--user", "--quiet", "--wait", "--pipe", "--collect", "--unit=" + unit, "--slice=" + packageSlice,
            .. profile.SystemdProperties( ).SelectMany( property => new[] { "-p", property } ),
            "-p", "TimeoutStopSec=500ms", "-p", "KillMode=control-group",
            "-p", "NoNewPrivileges=yes", "-p", "LockPersonality=yes", "-p", "LimitCORE=0",
            "-p", "SystemCallFilter=" + ExtensionSyscallFilter.Deny( debugSocket is not null ),
            "-p", "SystemCallErrorNumber=EPERM", "-p", "RestrictAddressFamilies=AF_UNIX AF_NETLINK",
            "--", "/usr/bin/env", "-i", "PATH=/usr/bin:/bin", "LANG=C.UTF-8", "DOTNET_EnableDiagnostics=0", hostExecutable,
            .. hostArguments, "extension-worker", "--artifact", artifactDirectory, "--entrypoint", entrypoint,
            "--owner-pid", owner.Id.ToString( CultureInfo.InvariantCulture ),
            "--owner-start", ExtensionSandbox.ReadProcessStartTicks( owner.Id ).ToString( CultureInfo.InvariantCulture )
        ];
        if ( debugSocket is not null )
        {
            arguments.AddRange( ["--debug-socket", debugSocket] );
        }
        ProcessStartInfo info = new( "/usr/bin/systemd-run" )
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = "/",
            CreateNoWindow = true
        };
        info.Environment.Clear( );
        // Only the trusted systemd client sees the user's runtime socket. env -i clears the service environment.
        info.Environment["XDG_RUNTIME_DIR"] = Environment.GetEnvironmentVariable( "XDG_RUNTIME_DIR" );
        info.Environment["PATH"] = "/usr/bin:/bin";
        foreach ( var argument in arguments )
        {
            info.ArgumentList.Add( argument );
        }
        var process = Process.Start( info );
        if ( process is null )
        {
            throw new IOException( "The extension service could not start." );
        }

        try
        {
            var controlGroup = await WaitForAccountingAsync( unit, process, profile, cancellationToken ).ConfigureAwait( false );
            var child = new ExtensionProcess( process, unit, controlGroup, cancellationToken, request.Profile );
            child.StartDiagnostics( );
            return child;
        }
        catch
        {
            using CancellationTokenSource cleanup = new( TimeSpan.FromMilliseconds( 250 ) );
            try
            {
                await StopUnitAsync( unit, cleanup.Token ).ConfigureAwait( false );
            }
            catch ( OperationCanceledException ) when ( cleanup.IsCancellationRequested ) { }

            try
            {
                if ( !process.HasExited )
                {
                    process.Kill( entireProcessTree: true );
                }
            }
            catch ( InvalidOperationException ) { }

            process.Dispose( );
            throw;
        }
    }

    private static async Task PrepareHierarchyAsync( string packageSlice, CancellationToken cancellationToken )
    {
        await HierarchyGate.WaitAsync( cancellationToken ).ConfigureAwait( false );
        try
        {
            await ManagerAsync( ["set-property", "--runtime", GlobalSlice, .. ExtensionResourceProfile.GlobalSlice.SystemdProperties( )], cancellationToken ).ConfigureAwait( false );
            await ManagerAsync( ["set-property", "--runtime", packageSlice, .. ExtensionResourceProfile.PackageSlice.SystemdProperties( )], cancellationToken ).ConfigureAwait( false );
            await ManagerAsync( ["start", packageSlice], cancellationToken ).ConfigureAwait( false );
            var globalGroup = (await ManagerAsync( ["show", "--value", "-p", "ControlGroup", GlobalSlice], cancellationToken ).ConfigureAwait( false )).Trim( );
            var packageGroup = (await ManagerAsync( ["show", "--value", "-p", "ControlGroup", packageSlice], cancellationToken ).ConfigureAwait( false )).Trim( );
            VerifyAccounting( globalGroup, ExtensionResourceProfile.GlobalSlice );
            VerifyAccounting( packageGroup, ExtensionResourceProfile.PackageSlice );
        }
        finally
        {
            HierarchyGate.Release( );
        }
    }

    private static async Task<string> WaitForAccountingAsync( string unit, Process process, ExtensionResourceProfile profile, CancellationToken cancellationToken )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        timeout.CancelAfter( TimeSpan.FromSeconds( 3 ) );
        while ( true )
        {
            timeout.Token.ThrowIfCancellationRequested( );
            if ( process.HasExited )
            {
                throw new IOException( "The restricted extension service exited during startup." );
            }
            var group = (await ManagerAsync( ["show", "--value", "-p", "ControlGroup", unit], timeout.Token, allowFailure: true ).ConfigureAwait( false )).Trim( );
            if ( group.Length > 0 )
            {
                VerifyAccounting( group, profile );
                return group;
            }
            await Task.Delay( 20, timeout.Token ).ConfigureAwait( false );
        }
    }

    private static void VerifyAccounting( string group, ExtensionResourceProfile profile )
    {
        if ( !group.StartsWith( "/user.slice/", StringComparison.Ordinal ) || group.Contains( "..", StringComparison.Ordinal ) )
        {
            throw new PlatformNotSupportedException( "The extension resource group is unavailable." );
        }
        profile.Verify( "/sys/fs/cgroup" + group );
    }

    internal static async ValueTask<string> ManagerAsync( IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool allowFailure = false )
    {
        var result = await ProcessRunner.RunBoundedAsync( "/usr/bin/systemctl", ["--user", .. arguments], "/", TimeSpan.FromSeconds( 2 ),
            new ProcessExecutionLimits
            {
                MaximumStandardOutputBytes = 16384,
                MaximumStandardErrorBytes = 4096,
                MaximumMemoryBytes = long.MaxValue,
                EnvironmentVariables = new Dictionary<string, string> { ["XDG_RUNTIME_DIR"] = Environment.GetEnvironmentVariable( "XDG_RUNTIME_DIR" ) ?? "" }
            }, cancellationToken ).ConfigureAwait( false );
        if ( result.ExitCode != 0 && !allowFailure )
        {
            throw new PlatformNotSupportedException( "The systemd user manager could not enforce the extension resource profile." );
        }
        return result.StandardOutput;
    }

    internal static async ValueTask StopUnitAsync( string unit, CancellationToken cancellationToken ) =>
        _ = await ManagerAsync( ["stop", unit], cancellationToken, allowFailure: true ).ConfigureAwait( false );
}
