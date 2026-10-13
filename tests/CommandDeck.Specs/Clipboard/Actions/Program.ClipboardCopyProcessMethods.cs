using System.Diagnostics;
using System.Text;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ClipboardCopyProcessCancelsAndReapsAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-copy-process-" ).FullName;
        try
        {
            using CancellationTokenSource cancelled = new( );
            cancelled.Cancel( );
            await ThrowsAsync<OperationCanceledException>( async ( ) => await ClipboardCopyProcess.CopyAsync(
                "never written", new ProcessStartInfo( "/no-such-copy-test-program" ), cancelled.Token ) );

            var executable = Environment.ProcessPath;
            if ( executable is null )
            {
                throw new InvalidOperationException( "Spec executable is unavailable." );
            }

            ProcessStartInfo start = new( executable );
            if ( Path.GetFileNameWithoutExtension( executable ) == "dotnet" )
            {
                start.ArgumentList.Add( typeof( Program ).Assembly.Location );
            }

            start.ArgumentList.Add( "--clipboard-copy-block-fixture" );
            start.ArgumentList.Add( root );
            using CancellationTokenSource cancellation = new( );
            const string text = "exact synthetic café\r\n";
            var copying = ClipboardCopyProcess.CopyAsync( text, start, cancellation.Token ).AsTask( );
            try
            {
                var ready = Path.Combine( root, "ready" );
                var started = Stopwatch.GetTimestamp( );
                while ( !File.Exists( ready ) && !copying.IsCompleted && Stopwatch.GetElapsedTime( started ) < TimeSpan.FromSeconds( 5 ) )
                {
                    await Task.Delay( 10 );
                }

                True( File.Exists( ready ), "synthetic child received stdin before cancellation" );
                var bytes = await File.ReadAllBytesAsync( Path.Combine( root, "input" ) );
                True( Encoding.UTF8.GetBytes( text ).SequenceEqual( bytes ) );
                var pid = int.Parse( await File.ReadAllTextAsync( ready ), System.Globalization.CultureInfo.InvariantCulture );
                cancellation.Cancel( );
                await ThrowsAsync<OperationCanceledException>( async ( ) => await copying );
                True( !Directory.Exists( $"/proc/{pid}" ), "copy completion waits for child reaping" );
            }
            finally
            {
                cancellation.Cancel( );
                try
                {
                    await copying;
                }
                catch ( OperationCanceledException ) { }
            }
        }
        finally
        {
            Directory.Delete( root, recursive: true );
        }
    }

    private static int RunClipboardCopyBlockFixture( string root )
    {
        if ( !Path.GetFileName( root ).StartsWith( "commanddeck-copy-process-", StringComparison.Ordinal ) ||
            !Directory.Exists( root ) || Directory.EnumerateFileSystemEntries( root ).Any( ) )
        {
            return 2;
        }

        using ( var output = File.Create( Path.Combine( root, "input" ) ) )
        {
            Console.OpenStandardInput( ).CopyTo( output );
        }

        File.WriteAllText( Path.Combine( root, "ready" ), Environment.ProcessId.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
        using ManualResetEvent hold = new( false );
        hold.WaitOne( );
        return 3;
    }
}
