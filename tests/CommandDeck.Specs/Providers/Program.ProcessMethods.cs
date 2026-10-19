using System.Diagnostics;
using System.Globalization;

namespace CommandDeck.Specs;

public static partial class Program
{
    private const uint ProcessOwner = 1000;
    private const string BridgeExecutable = "/opt/commanddeck/commanddeck";

    // Records pins and signals instead of signalling anything.
    private sealed class RecordingPinner
    {
        public bool Exited { get; set; }
        public bool SignalReachesProcess { get; set; } = true;
        public int Pins { get; private set; }
        public List<(int Pid, int Signal)> Signals { get; } = [];

        public IPinnedProcess? Pin( int pid )
        {
            Pins++;
            return Exited ? null : new Pinned( this, pid );
        }

        private sealed class Pinned( RecordingPinner owner, int pid ) : IPinnedProcess
        {
            public bool Signal( int signal )
            {
                if ( owner.SignalReachesProcess )
                {
                    owner.Signals.Add( (pid, signal) );
                }

                return owner.SignalReachesProcess;
            }

            public void Dispose( )
            {
            }
        }
    }

    private static void WriteFakeProcess(
        string root, int pid, string name, char state = 'S', uint uid = ProcessOwner, uint effectiveUid = ProcessOwner,
        ulong start = 5000, uint flags = 0x400100, long residentKilobytes = 1024, string? executable = null )
    {
        var directory = Path.Combine( root, pid.ToString( CultureInfo.InvariantCulture ) );
        Directory.CreateDirectory( directory );
        File.WriteAllText( Path.Combine( directory, "stat" ), string.Create( CultureInfo.InvariantCulture,
            $"{pid} ({name}) {state} 1 {pid} {pid} 0 -1 {flags} 100 0 0 0 150 50 0 0 20 0 1 0 {start} 123456 256 18446744073709551615\n" ) );
        File.WriteAllText( Path.Combine( directory, "status" ), string.Create( CultureInfo.InvariantCulture,
            $"Name:\t{name}\nState:\t{state}\nUid:\t{uid}\t{effectiveUid}\t{uid}\t{uid}\nVmRSS:\t{residentKilobytes} kB\n" ) );
        var link = Path.Combine( directory, "exe" );
        if ( File.Exists( link ) || Directory.Exists( link ) || new FileInfo( link ).LinkTarget is not null )
        {
            File.Delete( link );
        }

        if ( executable is not null )
        {
            File.CreateSymbolicLink( link, executable );
        }
    }

    private static string FakeProcessTable( string prefix )
    {
        var root = Directory.CreateTempSubdirectory( prefix ).FullName;
        WriteFakeProcess( root, 100, "firefox", residentKilobytes: 512_000, executable: "/usr/lib/firefox/firefox" );
        WriteFakeProcess( root, 101, "sshd", uid: 0, effectiveUid: 0 );
        WriteFakeProcess( root, 102, "kworker/0:1", flags: 0x00208040 );
        WriteFakeProcess( root, 103, "defunct", state: 'Z' );
        WriteFakeProcess( root, 104, "odd) Z 1 (name\u001b", residentKilobytes: 2048 );
        WriteFakeProcess( root, 105, "commanddeck" );
        WriteFakeProcess( root, 106, "sudo", effectiveUid: 0 );
        WriteFakeProcess( root, 107, "commanddeck", executable: BridgeExecutable );
        WriteFakeProcess( root, 108, "averyveryverylo", executable: "/usr/bin/averyveryverylongname" );
        Directory.CreateDirectory( Path.Combine( root, "self" ) );
        return root;
    }

    private static async Task ProcessListShowsOnlyTheUsersOwnLiveProcessesAsync( )
    {
        var root = FakeProcessTable( "commanddeck-process-list-" );
        try
        {
            LinuxProcessTable table = new( root, ProcessOwner, selfPid: 105, selfExecutable: BridgeExecutable );
            SearchCoordinator search = new( [new ProcessProvider( table )], new SearchOptions( ) );
            async Task<IReadOnlyList<SearchResult>> FindAsync( string query ) =>
                await search.SearchAsync( new SearchRequest { Query = query, Limit = 50 }, default );

            // Other users, setuid, kernel threads, zombies, and CommandDeck itself are left out.
            var all = await FindAsync( "kill:" );
            Equal( "process:100:5000,process:104:5000,process:108:5000", string.Join( ',', all.Select( static result => result.Id ) ) );
            var firefox = all[0];
            Equal( ResultKinds.Process, firefox.Kind );
            Equal( "firefox", firefox.Title );
            Equal( "PID 100 · 500 MB · CPU 2s", firefox.Subtitle );
            Equal( "odd) Z 1 (name?", all[1].Title );
            Equal( "averyveryverylongname", all[2].Title );
            foreach ( (var id, var title) in new[] { (ProcessActionIds.Terminate, "Terminate PID 100"), (ProcessActionIds.ForceKill, "Force kill PID 100") } )
            {
                var action = firefox.Actions.Single( action => action.Id == id );
                Equal( title, action.Title );
                Equal( ActionKinds.ProcessSignal, action.Kind );
                True( action.RequiresConfirmation && action.IsDestructive );
                Equal( "100|5000|1000", string.Join( '|', action.Arguments[ProcessActionFields.Pid],
                    action.Arguments[ProcessActionFields.StartTime], action.Arguments[ProcessActionFields.Uid] ) );
                Equal( AgentActionRisks.Confirm, AgentLauncherTools.ClassifyRisk( firefox, action ) );
            }

            Equal( "process:100:5000", (await FindAsync( "kill: fire" )).Single( ).Id );
            Equal( "process:104:5000", (await FindAsync( "proc: 104" )).Single( ).Id );

            // Plain search offers one row that opens the list, and never lists processes.
            var browse = (await FindAsync( "kill" )).Single( );
            Equal( ProcessResultIds.Browse, browse.Id );
            Equal( ActionKinds.ApplyQuery, browse.Actions.Single( ).Kind );
            Equal( "|processes", browse.Actions[0].Arguments["query"] + "|" + browse.Actions[0].Arguments["filterId"] );
            var named = (await FindAsync( "kill firefox" )).Single( );
            Equal( "Kill Process: firefox", named.Title );
            Equal( "firefox", named.Actions[0].Arguments["query"] );
            Equal( ProcessResultIds.Browse, (await FindAsync( "force quit" )).Single( ).Id );
            Equal( 0, (await FindAsync( "firefox" )).Count );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static async Task ProcessSignalsRequireConfirmationAndRecheckIdentityAsync( )
    {
        var root = FakeProcessTable( "commanddeck-process-signal-" );
        try
        {
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            SqliteCatalog catalog = new( options.Storage );
            await catalog.InitializeAsync( default );
            LinuxProcessTable table = new( root, ProcessOwner, selfPid: 105, selfExecutable: BridgeExecutable );
            RecordingPinner pinner = new( );
            LinuxActionExecutor executor = new( processTable: table, processPinner: pinner.Pin );
            await using var runtime = InvocationFixture( options, catalog, catalog.UsageHistory, executor,
                new SearchCoordinator( [new ProcessProvider( table )], options.Search ) );
            var row = (await runtime.SearchAsync( new SearchRequest { Query = "kill: firefox", Limit = 20 }, default )).Single( );

            // Unconfirmed, nothing is pinned or signalled.
            True( !(await runtime.InvokeAsync( row.Id, ProcessActionIds.Terminate, false, default )).Succeeded );
            Equal( 0, pinner.Pins );

            var terminated = await runtime.InvokeAsync( row.Id, ProcessActionIds.Terminate, true, default );
            Equal( "Sent SIGTERM to firefox (PID 100).", terminated.Message );
            True( (await runtime.InvokeAsync( row.Id, ProcessActionIds.ForceKill, true, default )).Succeeded );
            Equal( "100:15,100:9", string.Join( ',', pinner.Signals.Select( static signal => $"{signal.Pid}:{signal.Signal}" ) ) );
            var usage = await catalog.UsageHistory.GetUsageSignalsAsync( [row.Id], default );
            True( !usage.TryGetValue( row.Id, out var signal ) || signal.UseCount == 0, "transient PIDs never train usage" );

            // A reused PID, a changed owner, or an exited process is never signalled.
            foreach ( (var change, var message) in new (Action, string)[]
            {
                (( ) => WriteFakeProcess( root, 100, "firefox", start: 9000 ),
                    "PID 100 no longer belongs to the process you chose. Nothing was signalled. Search again."),
                (( ) => WriteFakeProcess( root, 100, "firefox", effectiveUid: 0 ),
                    "PID 100 no longer belongs to the process you chose. Nothing was signalled. Search again."),
                (( ) => WriteFakeProcess( root, 100, "firefox", state: 'Z' ),
                    "PID 100 no longer belongs to the process you chose. Nothing was signalled. Search again."),
                (( ) => pinner.Exited = true, "Process 100 has already exited."),
                (( ) => pinner.SignalReachesProcess = false, "Process 100 has already exited.")
            } )
            {
                WriteFakeProcess( root, 100, "firefox", executable: "/usr/lib/firefox/firefox" );
                pinner.Exited = false;
                pinner.SignalReachesProcess = true;
                change( );
                var refused = await runtime.InvokeAsync( row.Id, ProcessActionIds.Terminate, true, default );
                Equal( message, refused.Message );
                Equal( 2, pinner.Signals.Count );
            }

            // An identity that disagrees with its row, or names CommandDeck, is refused before pinning.
            var pins = pinner.Pins;
            foreach ( (var id, var pid) in new[] { ("process:100:5000", "101"), ("process:105:5000", "105"), ("process:1:5000", "1") } )
            {
                var action = row.Actions.Single( action => action.Id == ProcessActionIds.Terminate );
                CommandAction forged = new( )
                {
                    Id = action.Id,
                    Title = action.Title,
                    Kind = action.Kind,
                    Arguments = new( action.Arguments, StringComparer.Ordinal ) { [ProcessActionFields.Pid] = pid }
                };
                var outcome = await executor.ExecuteAsync( new SearchResult( row ) { Id = id, Actions = [forged] }, forged, "", default );
                Equal( "The process action is invalid. Search again.", outcome.Message );
            }

            Equal( pins, pinner.Pins );
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    // Signals only a child this spec starts, through the real pidfd path.
    private static async Task ProcessTerminateReachesARealChildThroughAPidfdAsync( )
    {
        if ( !OperatingSystem.IsLinux( ) || !File.Exists( "/usr/bin/sleep" ) )
        {
            throw new SpecSkippedException( "This fixture requires Linux and /usr/bin/sleep." );
        }

        using var child = Process.Start( new ProcessStartInfo( "/usr/bin/sleep", "30" ) { UseShellExecute = false } )!;
        try
        {
            LinuxProcessTable table = new( );
            var pid = child.Id.ToString( CultureInfo.InvariantCulture );
            var row = (await new ProcessProvider( table ).SearchAsync( QueryParser.Parse( "kill: " + pid ),
                new SearchRequest { Query = "kill: " + pid, Limit = 10 }, default ))
                .Single( result => result.Metadata[ProcessActionFields.Pid] == pid );
            Equal( "sleep", row.Title );
            var terminate = row.Actions.Single( action => action.Id == ProcessActionIds.Terminate );
            LinuxActionExecutor executor = new( processTable: table );

            var start = ulong.Parse( terminate.Arguments[ProcessActionFields.StartTime], CultureInfo.InvariantCulture );
            CommandAction stale = new( )
            {
                Id = terminate.Id,
                Title = terminate.Title,
                Kind = terminate.Kind,
                Arguments = new( terminate.Arguments, StringComparer.Ordinal )
                {
                    [ProcessActionFields.StartTime] = (start + 1).ToString( CultureInfo.InvariantCulture )
                }
            };
            var refused = await executor.ExecuteAsync(
                new SearchResult( row ) { Id = ProcessResultIds.For( child.Id, start + 1 ), Actions = [stale] }, stale, "", default );
            True( !refused.Succeeded );
            True( !child.HasExited, "a different start time is never signalled" );

            var outcome = await executor.ExecuteAsync( row, terminate, "", default );
            True( outcome.Succeeded, outcome.Message );
            using CancellationTokenSource timeout = new( TimeSpan.FromSeconds( 5 ) );
            await child.WaitForExitAsync( timeout.Token );
            Equal( 128 + 15, child.ExitCode );
            True( !(await executor.ExecuteAsync( row, terminate, "", default )).Succeeded );
        }
        finally
        {
            if ( !child.HasExited )
            {
                child.Kill( );
            }
        }
    }
}
