using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardPayloadLedgerRecoversKilledWriterAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        foreach ( var stage in new[] { "reserved", "writing", "files-published", "entry-committed" } )
        {
            var root = Directory.CreateTempSubdirectory( "commanddeck-payload-crash-" ).FullName;
            File.SetUnixFileMode( root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            try
            {
                await KillClipboardLedgerFixtureAsync( root, stage );
                using SqliteConnection connection = new( $"Data Source={Path.Combine( root, "history.db" )};Pooling=False" );
                connection.Open( );
                using var command = connection.CreateCommand( );
                command.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON";
                command.ExecuteNonQuery( );
                using ClipboardPayloadDirectory directory = new( Path.Combine( root, "history.data" ) );
                ClipboardPayloadLedger ledger = new( connection, directory, new object( ) );
                var committed = stage == "entry-committed";
                Equal( committed ? 5L : 32768L, ledger.ChargedBytes );
                ledger.Recover( ( _, damaged ) => Equal( 0, damaged.Count ) );
                Equal( committed ? 0 : 1, ledger.Cleanup( ) );
                Equal( committed ? 5L : 0L, ledger.ChargedBytes );
                command.CommandText = "SELECT count(*) FROM publication_probe";
                Equal( committed ? 1L : 0L, ( long ) command.ExecuteScalar( )! );
                if ( committed )
                {
                    var file = directory.Inventory( ).Single( );
                    using var lease = directory.OpenRead( file.Id );
                    using StreamReader reader = new( lease.Stream );
                    Equal( "hello", reader.ReadToEnd( ) );
                }
                else
                {
                    Equal( 0, directory.Inventory( ).Count );
                }

                ledger.Recover( ( _, damaged ) => Equal( 0, damaged.Count ) );
                Equal( 0, ledger.Cleanup( ) );
                Equal( committed ? 5L : 0L, ledger.ChargedBytes );
            }
            finally
            {
                Directory.Delete( root, recursive: true );
            }
        }
    }

    private static async Task KillClipboardLedgerFixtureAsync( string root, string stage, string mode = "--clipboard-ledger-crash-fixture" )
    {
        var executable = Environment.ProcessPath;
        if ( executable is null )
        {
            throw new InvalidOperationException( "Spec executable is unavailable." );
        }

        using Process child = new( );
        child.StartInfo = new( executable )
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if ( Path.GetFileNameWithoutExtension( executable ) == "dotnet" )
        {
            child.StartInfo.ArgumentList.Add( typeof( Program ).Assembly.Location );
        }

        foreach ( var argument in new[] { mode, root, stage } )
        {
            child.StartInfo.ArgumentList.Add( argument );
        }

        True( child.Start( ) );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 15 ) );
        var stderr = child.StandardError.ReadToEndAsync( deadline.Token );
        try
        {
            Equal( stage, await child.StandardOutput.ReadLineAsync( deadline.Token ) );
        }
        finally
        {
            if ( !child.HasExited )
            {
                child.Kill( entireProcessTree: true );
            }

            await child.WaitForExitAsync( ).WaitAsync( TimeSpan.FromSeconds( 5 ) );
        }

        Equal( "", await stderr );
        True( child.ExitCode != 0, "fixture was terminated without disposal or normal shutdown" );
    }

    private static async Task<int> RunClipboardLedgerCrashFixtureAsync( string root, string stage )
    {
        if ( !OperatingSystem.IsLinux( ) || !Path.GetFileName( root ).StartsWith( "commanddeck-payload-crash-", StringComparison.Ordinal ) ||
            !Directory.Exists( root ) || Directory.EnumerateFileSystemEntries( root ).Any( ) ||
            stage is not ("reserved" or "writing" or "files-published" or "entry-committed") )
        {
            return 2;
        }

        var path = Path.Combine( root, "history.db" );
        using ( var created = SafeRegularFile.CreatePrivateWriteStream( path ) )
        {
            created.Flush( true );
        }

        using SqliteConnection connection = new( $"Data Source={path};Pooling=False" );
        connection.Open( );
        using var command = connection.CreateCommand( );
        command.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; CREATE TABLE publication_probe(value INTEGER)";
        command.ExecuteNonQuery( );
        using ClipboardPayloadDirectory directory = new( Path.Combine( root, "history.data" ) );
        ClipboardPayloadLedger ledger = new( connection, directory, new object( ) );
        ledger.CreateSchema( );
        using var reservation = ledger.Reserve( ClipboardOfferPlan.Create( ["text/plain"], ClipboardConsent.Text ), 64L * 1024 * 1024 );
        if ( stage != "reserved" )
        {
            using var writer = ledger.OpenWriter( reservation, 0 );
            await writer.WriteAsync( "hello"u8.ToArray( ), default );
            if ( stage == "writing" )
            {
                await WaitForFixtureKillAsync( stage );
            }

            var payload = writer.Complete( );
            if ( stage == "entry-committed" )
            {
                ledger.Publish( reservation, new HashSet<Guid> { payload.Id }, ( ) => true, ( database, _ ) =>
                {
                    using var insert = database.CreateCommand( );
                    insert.CommandText = "INSERT INTO publication_probe VALUES(1)";
                    insert.ExecuteNonQuery( );
                } );
            }
        }

        await WaitForFixtureKillAsync( stage );
        return 0;
    }

    private static async Task WaitForFixtureKillAsync( string stage )
    {
        Console.WriteLine( stage );
        Console.Out.Flush( );
        await Task.Delay( Timeout.InfiniteTimeSpan );
    }
}
