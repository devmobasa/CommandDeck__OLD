namespace CommandDeck;

/// <summary>
/// Built-in desktop commands found from plain search: typing "volume" or "left
/// half" offers the matching controls, and <c>ctl:</c> lists them all. A command
/// whose tool is not on PATH is left out rather than shown as an error. Bare Home
/// and one-letter queries do no work.
/// </summary>
public sealed partial class DesktopControlsProvider : ICommandProvider
{
    private const int MinimumQueryLength = 2;
    private const double MinimumTermScore = 0.53;
    private const double KeywordWeight = 0.85;
    private const double BrowseScore = 0.5;

    private readonly Func<string, bool> _toolAvailable;
    private readonly ExternalProcessRunner _hyprland;

    public DesktopControlsProvider(
        Func<string, bool>? toolAvailable = null,
        ExternalProcessRunner? hyprlandRunner = null )
    {
        _toolAvailable = toolAvailable ?? ProcessRunner.ExistsOnPath;
        _hyprland = hyprlandRunner ?? DefaultHyprlandRunAsync;
    }

    public string Id => ProviderIds.DesktopControls;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( query );
        ArgumentNullException.ThrowIfNull( request );
        cancellationToken.ThrowIfCancellationRequested( );

        var text = query.Text.Trim( );
        var browsing = query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.DesktopControls );
        if ( !Participates( query, request ) || (!browsing && text.Length < MinimumQueryLength) )
        {
            return [];
        }

        Dictionary<string, bool> tools = new( StringComparer.Ordinal );
        List<SearchResult> results = [];
        foreach ( var control in DesktopControlCatalog.System )
        {
            var score = Score( text, control.Title, control.Keywords );
            if ( score > 0 && control.RequiredTools.All( tool => IsAvailable( tool, tools ) ) )
            {
                results.Add( BuildRow( control, score ) );
            }
        }

        AddColorPickerRow( text, tools, results );
        await AddWindowRowsAsync( text, tools, results, cancellationToken ).ConfigureAwait( false );
        return results;
    }

    private static double Score( string text, string title, string keywords ) =>
        text.Length == 0 ? BrowseScore : Match( text, title, keywords );

    // A disabled or excluded source, calculator, and assistant input never list
    // desktop commands.
    internal static bool Participates( ParsedQuery query, SearchRequest request ) =>
        !query.IsCalculator && !query.IsAiMode &&
        (request.EnabledSearchSources is null || request.EnabledSearchSources.Contains( SourceIds.DesktopControls )) &&
        !query.ExcludedFilters.Any( static filter => filter.SourceId == SourceIds.DesktopControls && filter.Kind is null );

    // Every word must match the title or, more weakly, the keywords; a whole-title
    // match keeps its own score. Subsequence noise below a typo match is ignored.
    internal static double Match( string text, string title, string keywords )
    {
        var whole = FuzzyMatcher.Score( text, title );
        var terms = text.Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
        var total = 0.0;
        foreach ( var term in terms )
        {
            var best = Math.Max(
                FuzzyMatcher.Score( term, title ),
                FuzzyMatcher.Score( term, keywords ) * KeywordWeight );
            if ( best < MinimumTermScore )
            {
                return whole >= MinimumTermScore ? whole : 0;
            }

            total += best;
        }

        var average = terms.Length == 0 ? 0 : total / terms.Length * 0.9;
        return Math.Max( whole >= MinimumTermScore ? whole : 0, average );
    }

    private bool IsAvailable( string tool, Dictionary<string, bool> tools )
    {
        if ( !tools.TryGetValue( tool, out var available ) )
        {
            available = _toolAvailable( tool );
            tools[tool] = available;
        }

        return available;
    }

    // The color picker is asked for by name, so a missing tool is reported on
    // its row instead of hiding it.
    private void AddColorPickerRow( string text, Dictionary<string, bool> tools, List<SearchResult> results )
    {
        var picker = DesktopControlCatalog.ColorPicker;
        var score = Score( text, picker.Title, picker.Keywords );
        if ( score <= 0 )
        {
            return;
        }

        var missing = picker.RequiredTools.FirstOrDefault( tool => !IsAvailable( tool, tools ) );
        results.Add( missing is null ? BuildRow( picker, score ) : new SearchResult
        {
            Id = DesktopControlResultIds.For( picker.Id + "-unavailable" ),
            Kind = ResultKinds.Status,
            Title = picker.Title,
            Subtitle = $"Unavailable: install {missing} to pick a color from the screen",
            Icon = picker.Icon,
            ProviderId = ProviderIds.DesktopControls,
            SourceId = SourceIds.DesktopControls,
            Score = score,
            Details = [new DetailField( "Missing tool", missing, Monospace: true )]
        } );
    }

    private static SearchResult BuildRow( DesktopControl control, double score )
    {
        var command = string.Join( ' ', control.Argv );
        return new SearchResult
        {
            Id = DesktopControlResultIds.For( control.Id ),
            Kind = ResultKinds.CustomCommand,
            Title = control.Title,
            Subtitle = $"{control.Category} · {control.Tool}",
            Icon = control.Icon,
            ProviderId = ProviderIds.DesktopControls,
            SourceId = SourceIds.DesktopControls,
            Score = score,
            Details =
            [
                new DetailField( "Category", control.Category ),
                new DetailField( "Command", command, Monospace: true )
            ],
            Actions =
            [
                new CommandAction
                {
                    Id = "run",
                    Title = control.Title,
                    Kind = ActionKinds.RunArgv,
                    Arguments = new Dictionary<string, string>( StringComparer.Ordinal )
                    {
                        ["argv"] = string.Join( '\u001f', control.Argv )
                    }
                }
            ],
            Metadata = new Dictionary<string, string>( StringComparer.Ordinal )
            {
                ["control"] = control.Id
            }
        };
    }
}

public static class DesktopControlResultIds
{
    public static string For( string controlId ) => "desktop:" + controlId;
}
