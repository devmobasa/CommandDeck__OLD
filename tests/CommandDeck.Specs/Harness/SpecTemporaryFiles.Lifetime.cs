using System.Runtime.InteropServices;

namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed partial class SpecTemporaryFiles
    {
        internal static IDisposable? HandleTermination( string[] args )
        {
            // This child deliberately owns SIGTERM to test graceful extension stop.
            if ( !OperatingSystem.IsLinux( ) || args.Contains( "--extension-disposal-fixture", StringComparer.Ordinal ) )
            {
                return null;
            }

            return new TerminationSignals( );
        }

        private sealed class TerminationSignals : IDisposable
        {
            private readonly PosixSignalRegistration _interrupt = PosixSignalRegistration.Create( PosixSignal.SIGINT, Exit );
            private readonly PosixSignalRegistration _terminate = PosixSignalRegistration.Create( PosixSignal.SIGTERM, Exit );

            private static void Exit( PosixSignalContext context )
            {
                context.Cancel = true;
                // ProcessExit runs the same idempotent cleanup as normal disposal.
                Environment.Exit( context.Signal == PosixSignal.SIGINT ? 130 : 143 );
            }

            public void Dispose( )
            {
                _interrupt.Dispose( );
                _terminate.Dispose( );
            }
        }

        private static void DeleteOwnedTree( string directory, bool recovering = false )
        {
            List<FileStream> leases = [];
            List<string> runs = [directory];
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach ( var child in Directory.EnumerateDirectories( directory, "run-*", options ) )
                {
                    if ( Guid.TryParseExact( Path.GetFileName( child )[4..], "N", out _ ) && File.Exists( Path.Combine( child, "owner.lock" ) ) )
                    {
                        if ( recovering )
                        {
                            leases.Add( new FileStream( Path.Combine( child, "owner.lock" ), FileMode.Open, FileAccess.ReadWrite, FileShare.None ) );
                        }
                        runs.Add( child );
                    }
                }

                // An exiting owner removes its entire synthetic tree. Recovery
                // first acquires every nested lease to preserve surviving children.
                foreach ( var run in runs )
                {
                    var sockets = new DirectoryInfo( Path.Combine( "/tmp", "cds-" + Path.GetFileName( run )[4..] ) );
                    if ( sockets.Exists && sockets.LinkTarget is null )
                    {
                        sockets.Delete( recursive: true );
                    }
                }
                Directory.Delete( directory, recursive: true );
            }
            finally
            {
                foreach ( var lease in leases )
                {
                    lease.Dispose( );
                }
            }
        }

        private static void RecoverStaleRuns( string parent )
        {
            foreach ( var candidate in new DirectoryInfo( parent ).EnumerateDirectories( "run-*" ) )
            {
                var id = candidate.Name[4..];
                if ( !Guid.TryParseExact( id, "N", out _ ) || candidate.LinkTarget is not null ||
                    candidate.LastWriteTimeUtc > DateTime.UtcNow.AddDays( -1 ) )
                {
                    continue;
                }

                // Only reclaim our marked, old, unlocked directories. An active
                // run keeps this lease open even if its timestamps are old.
                var marker = Path.Combine( candidate.FullName, "owner.lock" );
                if ( !File.Exists( marker ) || new FileInfo( marker ).LinkTarget is not null )
                {
                    continue;
                }

                try
                {
                    using var lease = new FileStream( marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None );
                    DeleteOwnedTree( candidate.FullName, recovering: true );
                }
                catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
                {
                    // Locked runs and inaccessible directories belong to someone
                    // else; leave them for their owner or a later recovery.
                }
            }
        }
    }
}
