using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace CommandDeck;

// Live state for Omarchy menu rows. A row's `when` guard that fails marks it
// unavailable; its `checked` condition that holds marks it checked. Both are
// bash conditions from Omarchy's own menu files, the same trust as the row's
// action. Like Omarchy's menu, the conditions a search needs run as one login
// bash batch, bounded in time, memory and output, and answers are cached by
// condition text. A search waits briefly for a condition it has never run and
// otherwise uses the previous answers while a refresh runs; the query is never
// passed in. A batch that answers nothing (it timed out or failed) is cached
// too, so a broken condition runs again only once its answer is stale.
// Disposal cancels a batch still running.
public sealed class OmarchyMenuState : IDisposable
{
    public const string WhenKey = "when";
    public const string CheckedKey = "checked";
    public const int MaximumRows = 12;
    private const int MaximumConditionLength = 2048;
    private static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds( 5 );
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds( 20 );

    private readonly BoundedProcessRunner _run;
    private readonly TimeProvider _time;
    private readonly TimeSpan _wait;
    private readonly Func<bool> _enabled;
    private readonly ConcurrentDictionary<string, Answer> _answers = new( StringComparer.Ordinal );
    private readonly ConcurrentDictionary<string, Task> _pending = new( StringComparer.Ordinal );
    private readonly CancellationTokenSource _shutdown = new( );

    // Holds is null when the condition has never answered.
    private readonly record struct Answer( bool? Holds, DateTimeOffset At );

    // `enabled` is asked on every search, so turning the state off in Settings
    // stops running conditions from the next search on.
    public OmarchyMenuState( BoundedProcessRunner? run = null, TimeProvider? time = null, TimeSpan? wait = null,
        Func<bool>? enabled = null )
    {
        _run = run ?? (( file, arguments, timeout, limits, token ) =>
            ProcessRunner.RunBoundedAsync( file, arguments, null, timeout, limits, token ));
        _time = time ?? TimeProvider.System;
        _wait = wait ?? TimeSpan.FromMilliseconds( 120 );
        _enabled = enabled ?? (static ( ) => true);
    }

    public void Dispose( )
    {
        _shutdown.Cancel( );
        _shutdown.Dispose( );
    }

    // Marks the first rows with the answers known after a brief wait. An
    // unavailable row keeps its actions and scores half as much.
    public async ValueTask<IReadOnlyList<SearchResult>> ApplyAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken cancellationToken )
    {
        if ( !_enabled( ) )
        {
            return results;
        }

        var rows = results.Where( static result => result.Kind == ResultKinds.Omarchy ).Take( MaximumRows ).ToArray( );
        var conditions = rows.SelectMany( static row => new[] { Condition( row, WhenKey ), Condition( row, CheckedKey ) } )
            .Where( static condition => condition is not null ).Select( static condition => condition! )
            .Distinct( StringComparer.Ordinal ).ToArray( );
        if ( conditions.Length == 0 )
        {
            return results;
        }

        var now = _time.GetUtcNow( );
        var stale = conditions.Where( condition =>
            !_answers.TryGetValue( condition, out var answer ) || now - answer.At > FreshFor ).ToArray( );
        if ( stale.Length > 0 )
        {
            var batch = Refresh( stale );
            if ( stale.Any( condition => !_answers.ContainsKey( condition ) ) )
            {
                await Task.WhenAny( batch, Task.Delay( _wait, cancellationToken ) ).ConfigureAwait( false );
                cancellationToken.ThrowIfCancellationRequested( );
            }
        }

        return [.. results.Select( result => rows.Contains( result ) ? Mark( result ) : result )];
    }

    private SearchResult Mark( SearchResult result )
    {
        var when = Condition( result, WhenKey );
        var checkedCondition = Condition( result, CheckedKey );
        var unavailable = when is not null && _answers.TryGetValue( when, out var guard ) && guard.Holds == false;
        var isChecked = checkedCondition is not null && _answers.TryGetValue( checkedCondition, out var mark ) && mark.Holds == true;
        if ( !unavailable && !isChecked )
        {
            return result;
        }

        Dictionary<string, string> metadata = new( result.Metadata, StringComparer.Ordinal );
        if ( unavailable )
        {
            metadata[ResultMetadataKeys.OmarchyUnavailable] = "true";
        }

        if ( isChecked )
        {
            metadata[ResultMetadataKeys.OmarchyChecked] = "true";
        }

        return new SearchResult( result ) { Metadata = metadata, Score = unavailable ? result.Score * 0.5 : result.Score };
    }

    private static string? Condition( SearchResult result, string key )
    {
        var value = result.Metadata.GetValueOrDefault( key );
        return string.IsNullOrWhiteSpace( value ) || value.Length > MaximumConditionLength ? null : value;
    }

    // One batch for the conditions this call claims; a condition another
    // search already has in flight is never started twice. The returned task
    // completes when every condition's batch has.
    private Task Refresh( string[] conditions )
    {
        // Claimed before the batch starts, so a fast batch cannot finish first
        // and leave its conditions marked in flight for good.
        TaskCompletionSource done = new( TaskCreationOptions.RunContinuationsAsynchronously );
        var claimed = conditions.Where( condition => _pending.TryAdd( condition, done.Task ) ).ToArray( );
        if ( claimed.Length > 0 )
        {
            _ = Task.Run( async ( ) =>
            {
                try
                {
                    await EvaluateAsync( claimed ).ConfigureAwait( false );
                }
                finally
                {
                    foreach ( var condition in claimed )
                    {
                        _pending.TryRemove( KeyValuePair.Create( condition, done.Task ) );
                    }

                    done.TrySetResult( );
                }
            } );
        }

        return Task.WhenAll( conditions.Select( condition => _pending.GetValueOrDefault( condition ) ?? Task.CompletedTask ) );
    }

    private async Task EvaluateAsync( string[] conditions )
    {
        var answered = new bool[conditions.Length];
        try
        {
            var result = await _run( "/bin/bash", ["-lc", Script( conditions )], BatchTimeout, new ProcessExecutionLimits
            {
                MaximumStandardOutputBytes = 64 * 1024,
                MaximumStandardErrorBytes = 16 * 1024,
                MaximumMemoryBytes = 256L * 1024 * 1024,
                ClearEnvironment = false
            }, _shutdown.Token ).ConfigureAwait( false );
            var now = _time.GetUtcNow( );
            foreach ( var (index, holds) in Parse( result.StandardOutput, conditions.Length ) )
            {
                _answers[conditions[index]] = new Answer( holds, now );
                answered[index] = true;
            }
        }
        catch ( Exception exception ) when ( exception is not OutOfMemoryException )
        {
            // An unanswered condition leaves its rows as they are.
        }

        // An unanswered condition keeps its last answer, freshly dated, so it
        // is not run again until that is stale.
        var at = _time.GetUtcNow( );
        for ( var index = 0; index < conditions.Length; index++ )
        {
            if ( !answered[index] )
            {
                _answers[conditions[index]] = _answers.TryGetValue( conditions[index], out var previous )
                    ? previous with { At = at }
                    : new Answer( null, at );
            }
        }
    }

    // Each condition runs in its own subshell, so an `exit` ends only that one,
    // and reports `<index>:<0|1>`; its own output is discarded.
    public static string Script( IReadOnlyList<string> conditions )
    {
        StringBuilder script = new( );
        for ( var index = 0; index < conditions.Count; index++ )
        {
            var number = index.ToString( CultureInfo.InvariantCulture );
            script.Append( "if ( " ).Append( conditions[index] ).Append( "\n) >/dev/null 2>&1 </dev/null; then echo " )
                .Append( number ).Append( ":1; else echo " ).Append( number ).Append( ":0; fi\n" );
        }

        return script.ToString( );
    }

    public static IEnumerable<(int Index, bool Holds)> Parse( string output, int count )
    {
        foreach ( var line in output.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
        {
            var parts = line.Split( ':' );
            if ( parts.Length == 2 && int.TryParse( parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var index ) &&
                index >= 0 && index < count && parts[1] is "0" or "1" )
            {
                yield return (index, parts[1] == "1");
            }
        }
    }
}
