namespace CommandDeck.Specs;

public static partial class Program
{
    private static Task<int>? TryRunClipboardFixtureAsync( string[] args ) => args switch
    {
        ["--type", "text/plain;charset=utf-8"] when Environment.GetEnvironmentVariable( ActionPayloadFixtureDirectory ) is not null =>
            Task.FromResult( RunActionPayloadFixture( "stdin", null ) ),
        ["-t", "service", "-S", "both", "--", "xdg-open", var url] when Environment.GetEnvironmentVariable( ActionPayloadFixtureDirectory ) is not null =>
            Task.FromResult( RunActionPayloadFixture( "url", url ) ),
        ["--worker-cancellation-fixture", var root] => Task.FromResult( RunWorkerCancellationFixture( root ) ),
        ["--process-runner-stdin-fixture", var root] => Task.FromResult( RunProcessRunnerStdinFixture( root ) ),
        ["--process-runner-descendant-fixture", var root] => Task.FromResult( RunProcessRunnerDescendantFixture( root ) ),
        ["--process-memory-fallback-parent-fixture"] => RunProcessMemoryFallbackParentAsync( ),
        ["--process-memory-allocation-fixture"] => RunProcessMemoryAllocationFixtureAsync( ),
        ["--process-idle-parent-fixture", var root, var maximum] => RunProcessIdleParentFixtureAsync( root, maximum ),
        ["--clipboard-html-aot", var executable] => RunClipboardHtmlAotAsync( executable ),
        ["--clipboard-wayland-server"] => Task.FromResult( RunClipboardWaylandServer( ) ),
        ["--clipboard-action-worker-fixture", "clipboard-action-worker", "--resolve"] => Task.FromResult( ClipboardActionWorker.RunChild( ) ),
        ["--clipboard-native-aot", var executable] => RunClipboardNativeAotAsync( executable ),
        ["--clipboard-idle-parent-fixture", var root, var mode] => RunClipboardIdleParentFixtureAsync( root, mode ),
        ["--clipboard-idle-helper-fixture", var root, ..] => RunClipboardIdleHelperFixtureAsync( root, args ),
        ["--clipboard-native-fixture", ..] => RunClipboardNativeFixtureAsync( args ),
        ["--clipboard-transport-parent-fixture"] => RunClipboardTransportParentFixtureAsync( ),
        ["--clipboard-transport-aot", var executable] => RunClipboardTransportAotProbeAsync( executable ),
        ["--clipboard-transport-fixture", ..] when args.Length == 8 => RunClipboardTransportFixtureAsync( args ),
        ["--clipboard-copy-block-fixture", var root] => Task.FromResult( RunClipboardCopyBlockFixture( root ) ),
        ["--clipboard-migration-crash-fixture", var root, var stage] => Task.FromResult( RunClipboardMigrationCrashFixture( root, stage ) ),
        ["--clipboard-ledger-crash-fixture", var root, var stage] => RunClipboardLedgerCrashFixtureAsync( root, stage ),
        _ => null
    };

    private static int RunClipboardWaylandServer( )
    {
        using ClipboardWaylandServerFixture server = new( );
        return server.Run( );
    }
}
