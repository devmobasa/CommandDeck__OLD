using System.Diagnostics;

namespace CommandDeck;

internal static class ClipboardCopyProcess
{
    public static ValueTask<ActionExecutionResult> CopyAsync( string text, CancellationToken cancellationToken )
        => CopyAsync( text, new ProcessStartInfo( "wl-copy" )
        {
            ArgumentList = { "--type", "text/plain;charset=utf-8" }
        }, cancellationToken );

    internal static async ValueTask<ActionExecutionResult> CopyAsync( string text, ProcessStartInfo startInfo, CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        // wl-copy exits after handing ownership to a background child. That child
        // retains stderr, so capturing and draining it would wait for ownership loss.
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardInput = true;
        startInfo.CreateNoWindow = true;
        using var process = new Process { StartInfo = startInfo };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        deadline.CancelAfter( TimeSpan.FromSeconds( 2 ) );
        if ( !process.Start( ) )
        {
            return ActionExecutionResult.Failure( "Could not start the clipboard service." );
        }
        try
        {
            await process.StandardInput.WriteAsync( text.AsMemory( ), deadline.Token ).ConfigureAwait( false );
            process.StandardInput.Close( );
            await process.WaitForExitAsync( deadline.Token ).ConfigureAwait( false );
            return process.ExitCode == 0 ? ActionExecutionResult.Success( "Copied." ) :
                ActionExecutionResult.Failure( $"The clipboard service failed (exit {process.ExitCode})." );
        }
        catch ( OperationCanceledException )
        {
            if ( !process.HasExited )
            {
                process.Kill( entireProcessTree: true );
            }
            await process.WaitForExitAsync( CancellationToken.None ).ConfigureAwait( false );

            cancellationToken.ThrowIfCancellationRequested( );
            return ActionExecutionResult.Failure( "The clipboard service timed out; the copy may already have completed." );
        }
        catch
        {
            if ( !process.HasExited )
            {
                process.Kill( entireProcessTree: true );
            }
            await process.WaitForExitAsync( CancellationToken.None ).ConfigureAwait( false );

            throw;
        }
    }
}
