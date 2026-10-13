using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardRetentionPrunesAtNextExpiryAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux." );
        }

        var root = Directory.CreateTempSubdirectory( "commanddeck-clipboard-retention-" ).FullName;
        var database = Path.Combine( root, "history.db" );
        try
        {
            ClipboardManualClock clock = new( new DateTimeOffset( 2026, 9, 29, 12, 0, 0, TimeSpan.Zero ) );
            using ClipboardHistoryStore store = new( database, new ClipboardHistoryOptions { MaximumEntries = 10, RetentionHours = 1 }, clock );
            store.Enabled = true;
            for ( var index = 0; index < 12; index++ )
            {
                store.Capture( "expiring " + index );
            }

            // The entry bound holds at insert, without a scheduled or read-time prune.
            Equal( 10L, ClipboardDatabaseScalar( database, "SELECT count(*) FROM entries" ) );
            clock.Advance( TimeSpan.FromMinutes( 58 ) );
            store.Capture( "recent" );
            Equal( 10L, ClipboardDatabaseScalar( database, "SELECT count(*) FROM entries" ) );

            await using ClipboardCapture capture = new( store, ( ) => ClipboardIdleHelperStart( root ) );
            capture.Start( "unused-injected-idle-helper", [] );
            await ClipboardEventuallyAsync( ( ) => capture.Status == "Capturing text" );
            Equal( 1, clock.PendingTimers );
            Equal( TimeSpan.FromMinutes( 2 ) + TimeSpan.FromSeconds( 1 ), clock.NextDelay );

            // An entry is pruned once the cutoff passes its copy second: 2 min 1 s after the recent copy.
            clock.Advance( TimeSpan.FromMinutes( 2 ) );
            Equal( 1, clock.PendingTimers );
            Equal( 10L, ClipboardDatabaseScalar( database, "SELECT count(*) FROM entries" ) );
            clock.Advance( TimeSpan.FromSeconds( 1 ) );
            await ClipboardEventuallyAsync( ( ) => ClipboardDatabaseScalar( database, "SELECT count(*) FROM entries" ) == 1 );
            Equal( "recent", ClipboardDatabaseScalarText( database, "SELECT text FROM entries" ) );

            // The next check is capped at five minutes even though "recent" expires in 58.
            await ClipboardEventuallyAsync( ( ) => clock.PendingTimers == 1 );
            Equal( TimeSpan.FromMinutes( 5 ), clock.NextDelay );
            clock.Advance( TimeSpan.FromMinutes( 5 ) );
            await ClipboardEventuallyAsync( ( ) => clock.PendingTimers == 1 );
            Equal( 1L, ClipboardDatabaseScalar( database, "SELECT count(*) FROM entries" ) );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ClipboardEventuallyAsync( Func<bool> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression( nameof( condition ) )] string? expression = null )
    {
        var started = Stopwatch.GetTimestamp( );
        while ( !condition( ) && Stopwatch.GetElapsedTime( started ) < TimeSpan.FromSeconds( 10 ) )
        {
            await Task.Delay( 10 );
        }

        True( condition( ), expression );
    }

    // Wall-clock time and one-shot timers that move only when the spec advances them.
    private sealed class ClipboardManualClock( DateTimeOffset start ) : TimeProvider
    {
        private readonly object _gate = new( );
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = start;

        internal int PendingTimers
        {
            get
            {
                lock ( _gate )
                {
                    return _timers.Count;
                }
            }
        }

        internal TimeSpan NextDelay
        {
            get
            {
                lock ( _gate )
                {
                    return _timers.Min( timer => timer.Due ) - _now;
                }
            }
        }

        public override DateTimeOffset GetUtcNow( )
        {
            lock ( _gate )
            {
                return _now;
            }
        }

        public override ITimer CreateTimer( TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period )
        {
            ManualTimer timer = new( this, callback, state );
            timer.Change( dueTime, period );
            return timer;
        }

        internal void Advance( TimeSpan duration )
        {
            List<ManualTimer> due;
            lock ( _gate )
            {
                _now += duration;
                due = _timers.Where( timer => timer.Due <= _now ).ToList( );
                _timers.RemoveAll( due.Contains );
            }

            foreach ( var timer in due )
            {
                timer.Fire( );
            }
        }

        private sealed class ManualTimer( ClipboardManualClock clock, TimerCallback callback, object? state ) : ITimer
        {
            internal DateTimeOffset Due
            {
                get; private set;
            }

            public bool Change( TimeSpan dueTime, TimeSpan period )
            {
                lock ( clock._gate )
                {
                    clock._timers.Remove( this );
                    if ( dueTime != Timeout.InfiniteTimeSpan )
                    {
                        Due = clock._now + dueTime;
                        clock._timers.Add( this );
                    }
                }

                return true;
            }

            internal void Fire( ) => callback( state );

            public void Dispose( )
            {
                lock ( clock._gate )
                {
                    clock._timers.Remove( this );
                }
            }

            public ValueTask DisposeAsync( )
            {
                Dispose( );
                return ValueTask.CompletedTask;
            }
        }
    }
}
