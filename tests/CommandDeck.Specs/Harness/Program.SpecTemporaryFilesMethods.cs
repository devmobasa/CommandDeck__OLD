using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task RunSpecLifetimeFixtureAsync( )
    {
        var directory = Path.GetTempPath( ).TrimEnd( Path.DirectorySeparatorChar );
        await using var database = new SqliteConnection( $"Data Source={Path.Combine( directory, "storage.db" )}" );
        await database.OpenAsync( );
        await using var command = database.CreateCommand( );
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE fixture(value); INSERT INTO fixture VALUES(1);";
        await command.ExecuteNonQueryAsync( );

        var socket = SpecTemporaryFiles.SocketPath( directory );
        using Socket listener = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        listener.Bind( new UnixDomainSocketEndPoint( socket ) );
        listener.Listen( );
        Console.WriteLine( directory );
        Console.WriteLine( socket );
        await Task.Delay( Timeout.InfiniteTimeSpan );
    }

    private static Process StartSpecLifetimeFixture( string parent )
    {
        ProcessStartInfo start = new( Environment.ProcessPath! )
        {
            WorkingDirectory = "/",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if ( Path.GetFileNameWithoutExtension( Environment.ProcessPath ) == "dotnet" )
        {
            start.ArgumentList.Add( typeof( Program ).Assembly.Location );
        }
        start.ArgumentList.Add( "--spec-lifetime-fixture" );
        // Like systemd services, the child gets no checkout working directory or
        // inherited TMPDIR. Its owned disk root must be sufficient on its own.
        start.Environment.Clear( );
        start.Environment["COMMANDDECK_SPEC_TEMP_ROOT"] = parent;
        var process = Process.Start( start );
        if ( process is null )
        {
            throw new InvalidOperationException( "Could not start storage fixture." );
        }

        return process;
    }

    private static async Task<(string Directory, string Socket)> ReadSpecLifetimePathsAsync( Process process )
    {
        using CancellationTokenSource readiness = new( TimeSpan.FromSeconds( 30 ) );
        var directory = await process.StandardOutput.ReadLineAsync( readiness.Token );
        var socket = await process.StandardOutput.ReadLineAsync( readiness.Token );
        if ( directory is null || socket is null )
        {
            throw new InvalidOperationException( "Storage fixture failed: " + await process.StandardError.ReadToEndAsync( readiness.Token ) );
        }

        return (directory, socket);
    }

    private static async Task SignalSpecFixtureAsync( Process process, string signal )
    {
        var result = await ProcessRunner.RunAsync( "/usr/bin/kill", ["-" + signal, process.Id.ToString( System.Globalization.CultureInfo.InvariantCulture )],
            null, null, TimeSpan.FromSeconds( 30 ), CancellationToken.None );
        Equal( 0, result.ExitCode );
        await process.WaitForExitAsync( ).WaitAsync( TimeSpan.FromSeconds( 30 ) );
    }

    private static async Task SpecTemporaryStorageLifetimeAsync( )
    {
        var parent = SpecTemporaryFiles.CreateDirectory( "storage-lifetime" ).FullName;
        var longParent = Path.Combine( parent, new string( 'x', 160 ) );
        Directory.CreateDirectory( longParent );
        try
        {
            foreach ( var signal in new[] { "INT", "TERM" } )
            {
                using var process = StartSpecLifetimeFixture( longParent );
                try
                {
                    var paths = await ReadSpecLifetimePathsAsync( process );
                    True( paths.Directory.StartsWith( longParent + "/", StringComparison.Ordinal ) );
                    True( File.Exists( Path.Combine( paths.Directory, "storage.db-wal" ) ) );
                    True( System.Text.Encoding.UTF8.GetByteCount( paths.Socket ) <= 107 );
                    using Socket client = new( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
                    await client.ConnectAsync( new UnixDomainSocketEndPoint( paths.Socket ) );
                    using var nested = StartSpecLifetimeFixture( paths.Directory );
                    try
                    {
                        var child = await ReadSpecLifetimePathsAsync( nested );
                        await SignalSpecFixtureAsync( process, signal );
                        Equal( signal == "INT" ? 130 : 143, process.ExitCode );
                        True( !Directory.Exists( paths.Directory ) );
                        True( !Directory.Exists( Path.GetDirectoryName( paths.Socket ) ) );
                        True( !Directory.Exists( child.Directory ) && !File.Exists( child.Socket ) );
                        True( !Directory.Exists( Path.GetDirectoryName( child.Socket ) ) );
                    }
                    finally
                    {
                        if ( !nested.HasExited )
                        {
                            await SignalSpecFixtureAsync( nested, "TERM" );
                        }
                    }
                }
                finally
                {
                    if ( !process.HasExited )
                    {
                        process.Kill( entireProcessTree: true );
                        await process.WaitForExitAsync( );
                    }
                }
            }
        }
        finally
        {
            Directory.Delete( parent, recursive: true );
        }
    }

    private static async Task SpecTemporaryStorageRecoversCrashAsync( )
    {
        var parent = SpecTemporaryFiles.CreateDirectory( "storage-recovery" ).FullName;
        using var live = StartSpecLifetimeFixture( parent );
        using var crashed = StartSpecLifetimeFixture( parent );
        string? liveSockets = null;
        string? crashedSockets = null;
        string? nestedSockets = null;
        try
        {
            var active = await ReadSpecLifetimePathsAsync( live );
            var stale = await ReadSpecLifetimePathsAsync( crashed );
            liveSockets = Path.GetDirectoryName( active.Socket );
            crashedSockets = Path.GetDirectoryName( stale.Socket );
            using ( var nested = StartSpecLifetimeFixture( stale.Directory ) )
            {
                try
                {
                    var nestedPaths = await ReadSpecLifetimePathsAsync( nested );
                    nestedSockets = Path.GetDirectoryName( nestedPaths.Socket );
                    await SignalSpecFixtureAsync( crashed, "KILL" );
                    Directory.SetLastWriteTimeUtc( stale.Directory, DateTime.UtcNow.AddDays( -2 ) );
                    using var probe = StartSpecLifetimeFixture( parent );
                    try
                    {
                        await ReadSpecLifetimePathsAsync( probe );
                        True( Directory.Exists( stale.Directory ) && File.Exists( nestedPaths.Socket ),
                            "recovery preserves a crashed parent's surviving child" );
                    }
                    finally
                    {
                        await SignalSpecFixtureAsync( probe, "TERM" );
                    }
                    await SignalSpecFixtureAsync( nested, "KILL" );
                }
                finally
                {
                    if ( !nested.HasExited )
                    {
                        nested.Kill( entireProcessTree: true );
                        await nested.WaitForExitAsync( );
                    }
                }
            }
            True( Directory.Exists( stale.Directory ) );
            Directory.SetLastWriteTimeUtc( active.Directory, DateTime.UtcNow.AddDays( -2 ) );
            Directory.SetLastWriteTimeUtc( stale.Directory, DateTime.UtcNow.AddDays( -2 ) );
            var unmarked = Path.Combine( parent, "run-" + Guid.NewGuid( ).ToString( "N" ) );
            Directory.CreateDirectory( unmarked );
            Directory.SetLastWriteTimeUtc( unmarked, DateTime.UtcNow.AddDays( -2 ) );
            var link = Path.Combine( parent, "run-" + Guid.NewGuid( ).ToString( "N" ) );
            Directory.CreateSymbolicLink( link, active.Directory );

            using var recovery = StartSpecLifetimeFixture( parent );
            try
            {
                await ReadSpecLifetimePathsAsync( recovery );
                True( !Directory.Exists( stale.Directory ) && !Directory.Exists( crashedSockets ) );
                True( !Directory.Exists( nestedSockets ) );
                True( Directory.Exists( active.Directory ) && File.Exists( active.Socket ) );
                True( Directory.Exists( unmarked ) && new DirectoryInfo( link ).LinkTarget == active.Directory );
                await SignalSpecFixtureAsync( recovery, "TERM" );
            }
            finally
            {
                if ( !recovery.HasExited )
                {
                    recovery.Kill( entireProcessTree: true );
                    await recovery.WaitForExitAsync( );
                }
            }
            await SignalSpecFixtureAsync( live, "TERM" );
        }
        finally
        {
            foreach ( var process in new[] { live, crashed } )
            {
                if ( !process.HasExited )
                {
                    process.Kill( entireProcessTree: true );
                    await process.WaitForExitAsync( );
                }
            }
            foreach ( var sockets in new[] { liveSockets, crashedSockets, nestedSockets } )
            {
                if ( sockets is not null && Directory.Exists( sockets ) )
                {
                    Directory.Delete( sockets, recursive: true );
                }
            }
            Directory.Delete( parent, recursive: true );
        }
    }
}
