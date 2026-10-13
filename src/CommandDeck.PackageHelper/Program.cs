using System.Runtime.InteropServices;
using Tmds.DBus.Protocol;

namespace CommandDeck.PackageHelper;

internal static class Program
{
    public static async Task<int> Main( string[] args )
    {
        if ( args.Length == 1 && string.Equals( args[0], "--version", StringComparison.Ordinal ) )
        {
            Console.WriteLine( PackageHelperProtocol.ProtocolVersion );
            return 0;
        }

        if ( args.Length == 1 && string.Equals( args[0], "--self-test", StringComparison.Ordinal ) )
        {
            return SelfTest( );
        }

        if ( args.Length > 0 )
        {
            Console.Error.WriteLine(
                "Usage: commanddeck-package-helper [--version | --self-test]" );
            return 2;
        }

        return await RunServiceAsync( ).ConfigureAwait( false );
    }

    private static async Task<int> RunServiceAsync( )
    {
        if ( !Environment.IsPrivilegedProcess )
        {
            Console.Error.WriteLine( "The CommandDeck package helper must run as root." );
            return 1;
        }

        string address;
        try
        {
            var value = DBusAddress.System;
            if ( value is null )
            {
                throw new InvalidOperationException( "The system bus address is not set." );
            }

            address = value;
        }
        catch ( InvalidOperationException exception )
        {
            Console.Error.WriteLine( $"The system bus is unavailable: {exception.Message}" );
            return 1;
        }

        using CancellationTokenSource shutdown = new( );
        using var terminate = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context => Stop( context, shutdown ) );
        using var interrupt = PosixSignalRegistration.Create(
            PosixSignal.SIGINT,
            context => Stop( context, shutdown ) );

        using DBusConnection connection = new( address );
        PackageProgressPublisher publisher = new( connection );
        PackageHelperCore core = new( new PackageHelperOptions
        {
            Authority = new PolkitAuthority( connection ),
            Runner = RunPacmanAsync,
            Progress = publisher.Publish
        } );
        connection.AddMethodHandler( new PackageHelperService( core ) );

        try
        {
            await connection.ConnectAsync( ).ConfigureAwait( false );
            await connection
                .RequestNameAsync( PackageHelperProtocol.BusName, RequestNameOptions.None )
                .ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is DBusExceptionBase or IOException )
        {
            Console.Error.WriteLine( $"The system bus is unavailable: {exception.Message}" );
            return 1;
        }

        await core.RunWorkerAsync( shutdown.Token ).ConfigureAwait( false );
        return 0;
    }

    private static void Stop( PosixSignalContext context, CancellationTokenSource shutdown )
    {
        context.Cancel = true;
        shutdown.Cancel( );
    }

    private static Task<PackageProcessResult> RunPacmanAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken ) =>
        BoundedProcess.RunAsync(
            fileName,
            arguments,
            BoundedProcess.DefaultOutputLimitBytes,
            timeout,
            cancellationToken );

    private static int SelfTest( )
    {
        List<string> failures = [];
        Check(
            failures,
            "install argv",
            "-S --needed --noconfirm --noprogressbar --color never -- alpha beta",
            string.Join( ' ', PackageHelperCore.BuildCommitArguments(
                PackageOperations.Install,
                ["alpha", "beta"] ) ) );
        Check(
            failures,
            "remove argv",
            "-Rns --noconfirm --noprogressbar --color never -- alpha",
            string.Join( ' ', PackageHelperCore.BuildCommitArguments(
                PackageOperations.Remove,
                ["alpha"] ) ) );
        Check(
            failures,
            "install preview argv",
            "-S --print --print-format %n|%v|%r --needed -- alpha",
            string.Join( ' ', PacmanTransactionDigest.BuildPrintArguments(
                PackageOperations.Install,
                ["alpha"] ) ) );
        Check(
            failures,
            "remove preview argv",
            "-Rs --print --print-format %n|%v -- alpha",
            string.Join( ' ', PacmanTransactionDigest.BuildPrintArguments(
                PackageOperations.Remove,
                ["alpha"] ) ) );
        Check(
            failures,
            "verify argv",
            "-Q -- alpha",
            string.Join( ' ', PackageHelperCore.BuildVerifyArguments( ["alpha"] ) ) );
        Check(
            failures,
            "normalized names",
            "alpha beta",
            string.Join( ' ', AgentContractValidator.NormalizePackageNames( ["beta", "alpha", "beta"] ) ) );
        Check(
            failures,
            "rejected flag name",
            "rejected",
            AgentContractValidator.IsValidPackageName( "--root" ) ? "accepted" : "rejected" );
        Check(
            failures,
            "digest is stable",
            PacmanTransactionDigest.Compute( "install", "b|2|extra\na|1|core\n" ),
            PacmanTransactionDigest.Compute( "install", "a|1|core\nb|2|extra\n" ) );

        foreach ( var failure in failures )
        {
            Console.Error.WriteLine( failure );
        }

        if ( failures.Count > 0 )
        {
            return 1;
        }

        Console.WriteLine( "commanddeck-package-helper self-test passed." );
        return 0;
    }

    private static void Check( List<string> failures, string name, string expected, string actual )
    {
        if ( !string.Equals( expected, actual, StringComparison.Ordinal ) )
        {
            failures.Add( $"self-test {name}: expected '{expected}', got '{actual}'." );
        }
    }
}
