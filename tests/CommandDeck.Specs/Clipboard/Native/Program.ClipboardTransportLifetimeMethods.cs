using System.Diagnostics;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardTransportDiesWithParentAsync( )
    {
        var start = ClipboardTransportFixtureStart( acknowledge: true );
        // Keep a possible dotnet assembly prefix, replacing only the fixture command and its mode.
        start.ArgumentList.RemoveAt( start.ArgumentList.Count - 1 );
        start.ArgumentList[start.ArgumentList.Count - 1] = "--clipboard-transport-parent-fixture";
        start.UseShellExecute = false;
        start.RedirectStandardOutput = start.RedirectStandardError = true;
        using Process parent = new( )
        {
            StartInfo = start
        };
        True( parent.Start( ) );
        using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 15 ) );
        try
        {
            var line = await parent.StandardOutput.ReadLineAsync( deadline.Token );
            var pid = int.Parse( line!, System.Globalization.CultureInfo.InvariantCulture );
            using var child = Process.GetProcessById( pid );
            parent.Kill( entireProcessTree: false );
            await parent.WaitForExitAsync( deadline.Token );
            await child.WaitForExitAsync( deadline.Token );
            True( child.HasExited, "kernel parent-death handling terminates the helper without managed cleanup" );
        }
        finally
        {
            if ( !parent.HasExited )
            {
                parent.Kill( entireProcessTree: true );
            }

            await parent.WaitForExitAsync( );
        }
    }

    private static async Task<int> RunClipboardTransportParentFixtureAsync( )
    {
        await using var helper = new ClipboardHostProcess( ClipboardTransportFixtureStart( acknowledge: true ), 9 );
        _ = await helper.Events.ReadAsync( );
        Console.WriteLine( helper.ProcessId.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
        Console.Out.Flush( );
        await Task.Delay( Timeout.Infinite );
        return 3;
    }
}
