using System.Globalization;

namespace CommandDeck;

/// <summary>
/// The user's own processes, listed only through <c>kill:</c>, so an ordinary
/// keystroke never scans <c>/proc</c>. Plain search for "kill" or "force quit"
/// offers one row that opens that list. Each process row carries its PID, start
/// time, and owner; Terminate and Force kill require confirmation and are checked
/// against the process again before any signal.
/// </summary>
public sealed class ProcessProvider : ICommandProvider
{
    private const int MaximumScanned = 8192;
    private const int MaximumResults = 100;
    private const int MinimumQueryLength = 2;
    private const string KillPrefix = "kill ";
    private const string BrowseTitle = "Kill Process";
    private const string BrowseKeywords = "kill terminate force quit end task stop process pkill killall";
    // Linux reports CPU time in USER_HZ ticks, 100 per second on every supported architecture.
    private const double TicksPerSecond = 100;
    private const double BrowseScore = 0.9;
    private const double RankStep = 0.0001;

    private readonly LinuxProcessTable _table;

    public ProcessProvider( LinuxProcessTable? table = null )
    {
        _table = table ?? new LinuxProcessTable( );
    }

    public string Id => ProviderIds.Processes;

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( query );
        ArgumentNullException.ThrowIfNull( request );
        cancellationToken.ThrowIfCancellationRequested( );

        if ( query.IsCalculator || query.IsAiMode ||
            (request.EnabledSearchSources is not null && !request.EnabledSearchSources.Contains( SourceIds.Processes )) ||
            query.ExcludedFilters.Any( static filter => filter.SourceId == SourceIds.Processes ) )
        {
            return ValueTask.FromResult<IReadOnlyList<SearchResult>>( [] );
        }

        var text = query.Text.Trim( );
        var listing = query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.Processes );
        IReadOnlyList<SearchResult> results = listing
            ? List( text, Math.Clamp( request.Limit, 1, MaximumResults ), cancellationToken )
            : Browse( text );
        return ValueTask.FromResult( results );
    }

    // "kill firefox" opens the list already filtered; "kill" or "force quit"
    // opens it empty.
    private static IReadOnlyList<SearchResult> Browse( string text )
    {
        if ( text.Length < MinimumQueryLength )
        {
            return [];
        }

        if ( text.StartsWith( KillPrefix, StringComparison.OrdinalIgnoreCase ) && text.Length > KillPrefix.Length )
        {
            var name = text[KillPrefix.Length..].Trim( );
            return [BrowseRow( $"{BrowseTitle}: {name}", name, 1 )];
        }

        var score = DesktopControlsProvider.Match( text, BrowseTitle, BrowseKeywords );
        return score > 0 ? [BrowseRow( BrowseTitle, string.Empty, score )] : [];
    }

    private List<SearchResult> List( string text, int limit, CancellationToken cancellationToken )
    {
        var entries = _table.List( MaximumScanned, cancellationToken )
            .OrderByDescending( static entry => entry.ResidentBytes )
            .ThenBy( static entry => entry.Pid )
            .ToArray( );
        List<SearchResult> results = [];
        for ( var rank = 0; rank < entries.Length && results.Count < limit; rank++ )
        {
            var entry = entries[rank];
            var score = text.Length == 0 ? BrowseScore : Match( text, entry );
            if ( score > 0 )
            {
                results.Add( BuildRow( entry, score - (rank * RankStep) ) );
            }
        }

        return results;
    }

    private static double Match( string text, ProcessEntry entry )
    {
        if ( string.Equals( text, entry.Pid.ToString( CultureInfo.InvariantCulture ), StringComparison.Ordinal ) )
        {
            return 1;
        }

        var score = FuzzyMatcher.Score( text, entry.Name );
        return score >= 0.53 ? score : 0;
    }

    private SearchResult BuildRow( ProcessEntry entry, double score )
    {
        var pid = entry.Pid.ToString( CultureInfo.InvariantCulture );
        var executable = _table.Executable( entry.Pid );
        var name = DisplayName( entry, executable );
        Dictionary<string, string> identity = new( StringComparer.Ordinal )
        {
            [ProcessActionFields.Pid] = pid,
            [ProcessActionFields.StartTime] = entry.StartTicks.ToString( CultureInfo.InvariantCulture ),
            [ProcessActionFields.Uid] = _table.UserId.ToString( CultureInfo.InvariantCulture )
        };
        List<DetailField> details =
        [
            new( "PID", pid, Monospace: true ),
            new( "State", DescribeState( entry.State ) ),
            new( "Memory", FormatBytes( entry.ResidentBytes ) ),
            new( "CPU time", FormatCpu( entry.CpuTicks ) )
        ];
        if ( executable is not null )
        {
            details.Add( new DetailField( "Executable", executable, Monospace: true ) );
        }

        return new SearchResult
        {
            Id = ProcessResultIds.For( entry.Pid, entry.StartTicks ),
            Kind = ResultKinds.Process,
            Title = name,
            Subtitle = $"PID {pid} · {FormatBytes( entry.ResidentBytes )} · CPU {FormatCpu( entry.CpuTicks )}",
            Icon = "utilities-system-monitor",
            ProviderId = ProviderIds.Processes,
            SourceId = SourceIds.Processes,
            Score = score,
            Details = details,
            Actions =
            [
                SignalAction( ProcessActionIds.Terminate, $"Terminate PID {pid}", identity ),
                SignalAction( ProcessActionIds.ForceKill, $"Force kill PID {pid}", identity ),
                new CommandAction
                {
                    Id = ProcessActionIds.CopyPid,
                    Title = "Copy PID",
                    Kind = ActionKinds.CopyText,
                    Arguments = new TextActionPayload( pid ).ToArguments( )
                }
            ],
            Metadata = new Dictionary<string, string>( StringComparer.Ordinal ) { [ProcessActionFields.Pid] = pid }
        };
    }

    // The confirmation reads "Terminate PID 4242 “firefox”?", naming the exact process.
    private static CommandAction SignalAction( string id, string title, Dictionary<string, string> identity ) => new( )
    {
        Id = id,
        Title = title,
        Kind = ActionKinds.ProcessSignal,
        IsDestructive = true,
        RequiresConfirmation = true,
        Arguments = new Dictionary<string, string>( identity, StringComparer.Ordinal )
    };

    private static SearchResult BrowseRow( string title, string query, double score ) => new( )
    {
        Id = ProcessResultIds.Browse,
        Kind = ResultKinds.CustomCommand,
        Title = title,
        Subtitle = "List your processes to terminate or force kill one",
        Icon = "utilities-system-monitor",
        ProviderId = ProviderIds.Processes,
        SourceId = SourceIds.Processes,
        Score = score,
        Actions =
        [
            new CommandAction
            {
                Id = "browse",
                Title = "List Processes",
                Kind = ActionKinds.ApplyQuery,
                Arguments = new Dictionary<string, string>( StringComparer.Ordinal )
                {
                    ["query"] = query,
                    ["filterId"] = ProcessResultIds.FilterId
                }
            }
        ]
    };

    // The kernel keeps 15 characters of a command name; the executable's file
    // name completes a name it cut short.
    private static string DisplayName( ProcessEntry entry, string? executable )
    {
        var file = executable is null ? null : Path.GetFileName( executable.Replace( " (deleted)", "", StringComparison.Ordinal ) );
        return entry.Name.Length >= 15 && file is not null && file.StartsWith( entry.Name, StringComparison.Ordinal )
            ? file
            : entry.Name;
    }

    private static string DescribeState( char state ) => state switch
    {
        'R' => "Running",
        'S' => "Sleeping",
        'D' => "Waiting for I/O",
        'T' => "Stopped",
        't' => "Traced",
        'I' => "Idle",
        _ => state.ToString( )
    };

    private static string FormatBytes( long bytes ) => bytes switch
    {
        < 1024 * 1024 => $"{(bytes / 1024).ToString( CultureInfo.InvariantCulture )} KB",
        < 1024L * 1024 * 1024 => $"{(bytes / (1024.0 * 1024)).ToString( "0", CultureInfo.InvariantCulture )} MB",
        _ => $"{(bytes / (1024.0 * 1024 * 1024)).ToString( "0.0", CultureInfo.InvariantCulture )} GB"
    };

    private static string FormatCpu( ulong ticks )
    {
        var time = TimeSpan.FromSeconds( Math.Floor( ticks / TicksPerSecond ) );
        return time.TotalHours >= 1
            ? $"{(( int ) time.TotalHours).ToString( CultureInfo.InvariantCulture )}h {time.Minutes:00}m"
            : time.TotalMinutes >= 1
                ? $"{time.Minutes.ToString( CultureInfo.InvariantCulture )}m {time.Seconds:00}s"
                : $"{time.Seconds.ToString( CultureInfo.InvariantCulture )}s";
    }
}
