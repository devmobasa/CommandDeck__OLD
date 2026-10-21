using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ClipboardHandoffRecovery( )
    {
        var directory = PrivateHandoffDirectory( );
        var path = Path.Combine( directory, "history.handoffs" );
        try
        {
            Guid first;
            Guid second;
            using ( ClipboardHandoffJournal journal = new( path, "boot-one" ) )
            {
                first = journal.TryAdmit( Guid.NewGuid( ), "worker:1:100", "manager:2:200" )!.Attempt;
                second = journal.TryAdmit( Guid.NewGuid( ), "worker:3:300", "manager:2:200" )!.Attempt;
                True( journal.TryArm( first ), "permit arms once after admission" );
                True( !journal.TryArm( first ), "duplicate permit rejected" );
                True( journal.TryArm( second ), "second permit arms" );
                True( journal.TryAdmit( Guid.NewGuid( ), "worker:4", "manager:2" ) is null, "two charged slots bound admission" );
            }

            using ( ClipboardHandoffJournal restarted = new( path, "boot-one" ) )
            {
                True( !restarted.TryArm( first ), "restart cannot replay dispatch" );
                restarted.WorkerReaped( first );
                restarted.WorkerReaped( second );
                restarted.Observe( first, ClipboardHandoffEvidence.MissingOrInconsistent );
                restarted.Observe( second, ClipboardHandoffEvidence.StartJobPending );
                Equal( 2, restarted.Snapshot( ).Count( record => record.Charged ) );
                True( restarted.TryAdmit( Guid.NewGuid( ), "worker:4", "manager:2" ) is null, "absence and reaping do not reset accounting" );
                restarted.Observe( first, ClipboardHandoffEvidence.TerminalEmptyUnit );
                var terminal = restarted.Snapshot( ).Single( record => record.Attempt == first );
                Equal( ClipboardEffectOutcome.Unknown, terminal.Outcome );
                True( !terminal.Charged, "unknown past effect retires with no future work" );
                var next = restarted.TryAdmit( Guid.NewGuid( ), "worker:4", "manager:2" );
                True( next is not null, "positive terminal evidence restores one slot" );
                True( next!.UnitName != terminal.UnitName, "retired unit name never reused" );
                True( restarted.TryArm( next.Attempt ), "new attempt arms" );
            }

            using ( ClipboardHandoffJournal rebooted = new( path, "boot-two" ) )
            {
                Equal( 0, rebooted.Snapshot( ).Count( record => record.Charged ) );
                True( !rebooted.TryArm( second ), "boot boundary retires without replay" );
                True( rebooted.TryAdmit( Guid.NewGuid( ), "worker:1", "manager:1" ) is not null, "boot restores first slot" );
                True( rebooted.TryAdmit( Guid.NewGuid( ), "worker:2", "manager:1" ) is not null, "boot restores second slot" );
                True( rebooted.TryAdmit( Guid.NewGuid( ), "worker:3", "manager:1" ) is null, "boot preserves capacity ceiling" );
            }
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }

    private static void ClipboardHandoffRequiresReaping( )
    {
        var directory = PrivateHandoffDirectory( );
        var path = Path.Combine( directory, "history.handoffs" );
        try
        {
            Guid notSent;
            using ( ClipboardHandoffJournal journal = new( path, "boot-one" ) )
            {
                var record = journal.TryAdmit( Guid.NewGuid( ), "worker:1", "manager:2" )!;
                True( journal.TryArm( record.Attempt ), "arm" );
                journal.Observe( record.Attempt, ClipboardHandoffEvidence.ExecConfirmed );
                True( journal.Snapshot( ).Single( ).Charged, "successful exec cannot release a stuck worker" );
                journal.Observe( record.Attempt, ClipboardHandoffEvidence.StartJobPending );
                Equal( ClipboardEffectOutcome.ExecConfirmed, journal.Snapshot( ).Single( ).Outcome );
                journal.WorkerReaped( record.Attempt );
                True( !journal.Snapshot( ).Single( ).Charged, "exec plus reaping retires" );
                notSent = journal.TryAdmit( Guid.NewGuid( ), "worker:3", "manager:2" )!.Attempt;
            }

            using ( ClipboardHandoffJournal restarted = new( path, "boot-one" ) )
            {
                True( !restarted.TryArm( notSent ), "old unarmed attempt also cannot replay" );
                restarted.WorkerReaped( notSent );
                var record = restarted.Snapshot( ).Single( );
                Equal( ClipboardEffectOutcome.NotSent, record.Outcome );
                True( !record.Charged, "pre-permit death is recoverable after reaping" );
                restarted.WorkerReaped( notSent );
                restarted.Observe( notSent, ClipboardHandoffEvidence.MissingOrInconsistent );
                Equal( record, restarted.Snapshot( ).Single( ) );
            }
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }

    private static void ClipboardHandoffRejectsCorruption( )
    {
        var directory = PrivateHandoffDirectory( );
        var path = Path.Combine( directory, "history.handoffs" );
        try
        {
            using ( ClipboardHandoffJournal journal = new( path, "boot-one" ) )
            {
                journal.TryAdmit( Guid.NewGuid( ), "worker:1", "manager:2" );
            }

            using ( SqliteConnection database = new( $"Data Source={path};Pooling=False" ) )
            {
                database.Open( );
                using var command = database.CreateCommand( );
                command.CommandText = "UPDATE slots SET checksum='invalid'";
                command.ExecuteNonQuery( );
            }

            var before = File.ReadAllBytes( path );
            Throws<IOException>( ( ) => { using var rejected = new ClipboardHandoffJournal( path, "boot-one" ); } );
            Throws<IOException>( ( ) => { using var rejected = new ClipboardHandoffJournal( path, "boot-two" ); } );
            True( before.SequenceEqual( File.ReadAllBytes( path ) ), "corrupt journal is preserved even across boot" );
            if ( OperatingSystem.IsLinux( ) )
            {
                Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode( path ) );
            }
        }
        finally
        {
            Directory.Delete( directory, recursive: true );
        }
    }

    private static string PrivateHandoffDirectory( )
    {
        var directory = SpecTemporaryFiles.CreateDirectory( "commanddeck-handoff-" ).FullName;
        if ( OperatingSystem.IsLinux( ) )
        {
            File.SetUnixFileMode( directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
        }
        return directory;
    }
}
