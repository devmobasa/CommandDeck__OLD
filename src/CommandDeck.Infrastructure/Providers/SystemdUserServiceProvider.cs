using System.Text;
using System.Text.Json;

namespace CommandDeck;

/// <summary>
/// Explicit discovery of systemd user services and timers. The provider only
/// answers when the query explicitly targets it through the <c>svc:</c> filter,
/// so it never spawns <c>systemctl</c> on an ordinary keystroke. Search lists
/// state only; logs, editing, and confirmed controls are explicit actions.
/// </summary>
public sealed class SystemdUserServiceProvider : ICommandProvider
{
    private const int MaximumUnits = 40;
    private static readonly TimeSpan ListTimeout = TimeSpan.FromMilliseconds( 700 );

    private readonly ExternalProcessRunner _run;
    private readonly Func<bool> _available;

    public SystemdUserServiceProvider(
        ExternalProcessRunner? runner = null,
        Func<bool>? available = null )
    {
        _run = runner ?? DefaultRunAsync;
        _available = available ?? (static ( ) => ProcessRunner.ExistsOnPath( "systemctl" ));
    }

    public string Id => ProviderIds.SystemdUnits;

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        ParsedQuery query,
        SearchRequest request,
        CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( query );
        ArgumentNullException.ThrowIfNull( request );
        cancellationToken.ThrowIfCancellationRequested( );

        // Filter-activated only: no results, and no process, unless the user
        // typed svc:/unit:/service:.
        if ( !query.IncludedFilters.Any( static filter => filter.SourceId == SourceIds.SystemdUnits ) )
        {
            return [];
        }
        if ( !_available( ) )
        {
            return [];
        }

        ProcessResult listing;
        try
        {
            listing = await _run(
                "systemctl",
                ["--user", "list-units", "--type=service,timer", "--all", "--no-legend", "--no-pager", "--output=json"],
                ListTimeout,
                cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            return [];
        }
        if ( listing.ExitCode != 0 || listing.TimedOut || listing.AnyOutputLimitExceeded || listing.MemoryLimitExceeded )
        {
            return [];
        }

        var units = ParseUnits( listing.StandardOutput, query.Text );
        var limit = Math.Clamp( request.Limit, 1, MaximumUnits );
        List<SearchResult> results = [];
        foreach ( var unit in units.Take( limit ) )
        {
            cancellationToken.ThrowIfCancellationRequested( );
            var detail = BuildDetail( unit );
            results.Add( BuildResult( unit, detail ) );
        }
        return results;
    }

    private static List<SystemdUnit> ParseUnits( string json, string query )
    {
        List<SystemdUnit> units = [];
        if ( string.IsNullOrWhiteSpace( json ) )
        {
            return units;
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse( json );
        }
        catch ( JsonException )
        {
            return units;
        }
        using ( document )
        {
            if ( document.RootElement.ValueKind != JsonValueKind.Array )
            {
                return units;
            }
            foreach ( var element in document.RootElement.EnumerateArray( ) )
            {
                if ( element.ValueKind != JsonValueKind.Object )
                {
                    continue;
                }
                var name = ReadString( element, "unit" );
                if ( !IsValidUnitName( name ) )
                {
                    continue;
                }
                SystemdUnit unit = new(
                    name,
                    ReadString( element, "load" ),
                    ReadString( element, "active" ),
                    ReadString( element, "sub" ),
                    ReadString( element, "description" ) );
                if ( Matches( unit, query ) )
                {
                    units.Add( unit );
                }
            }
        }
        units.Sort( static ( left, right ) => string.Compare( left.Name, right.Name, StringComparison.Ordinal ) );
        return units;
    }

    private static bool Matches( SystemdUnit unit, string query )
    {
        if ( string.IsNullOrWhiteSpace( query ) )
        {
            return true;
        }
        return unit.Name.Contains( query, StringComparison.OrdinalIgnoreCase ) ||
            unit.Description.Contains( query, StringComparison.OrdinalIgnoreCase );
    }

    private static string BuildDetail( SystemdUnit unit )
    {
        StringBuilder detail = new( );
        detail.Append( "**" ).Append( Escape( unit.Name ) ).Append( "**\n\n" );
        if ( unit.Description.Length > 0 )
        {
            detail.Append( Escape( unit.Description ) ).Append( "\n\n" );
        }
        detail.Append( "- load: " ).Append( Escape( unit.Load ) ).Append( '\n' );
        detail.Append( "- active: " ).Append( Escape( unit.Active ) ).Append( '\n' );
        detail.Append( "- sub: " ).Append( Escape( unit.Sub ) ).Append( '\n' );

        detail.Append( "\nLogs and current status load only when requested. Controls affect this user unit only." );
        return detail.ToString( );
    }

    internal static bool IsValidUnitName( string name )
    {
        if ( name.Length is < 7 or > 255 ||
            !(name.EndsWith( ".service", StringComparison.Ordinal ) || name.EndsWith( ".timer", StringComparison.Ordinal )) )
        {
            return false;
        }

        if ( name is ".service" or ".timer" )
        {
            return false;
        }

        return name.All( character => char.IsAsciiLetterOrDigit( character ) || character is ':' or '_' or '.' or '@' or '-' or '\\' );
    }

    private static CommandAction UnitAction( string unit, string operation, string title, bool confirm = false ) => new( )
    {
        Id = operation,
        Title = confirm ? $"{title}: {unit}" : title,
        Kind = ActionKinds.SystemdUnit,
        RequiresConfirmation = confirm,
        IsDestructive = SystemdUnitActionIds.IsDestructive( operation ),
        Arguments = new Dictionary<string, string>( StringComparer.Ordinal )
        {
            [SystemdUnitActionFields.Unit] = unit,
            [SystemdUnitActionFields.Operation] = operation
        }
    };

    private static SearchResult BuildResult( SystemdUnit unit, string detail ) => new( )
    {
        Id = SystemdUnitResultIds.ForUnit( unit.Name ),
        Kind = ResultKinds.CustomCommand,
        Title = unit.Name,
        Subtitle = DescribeState( unit ),
        Icon = "applications-system",
        ProviderId = SourceIds.SystemdUnits,
        SourceId = SourceIds.SystemdUnits,
        Score = unit.Active == "active" ? 1.2 : 1.0,
        DetailMarkdown = detail,
        Section = new ResultSection { Id = SourceIds.SystemdUnits, Title = "Services & timers", Order = 6 },
        Metadata = new Dictionary<string, string>( StringComparer.Ordinal ) { [SystemdUnitActionFields.Unit] = unit.Name },
        Actions =
        [
            UnitAction( unit.Name, SystemdUnitActionIds.Logs, "Show recent logs" ),
            UnitAction( unit.Name, SystemdUnitActionIds.Status, "Show current status / timer schedule" ),
            UnitAction( unit.Name, SystemdUnitActionIds.Edit, "Edit unit with preferred editor" ),
            UnitAction( unit.Name, SystemdUnitActionIds.Start, "Start user unit", confirm: true ),
            UnitAction( unit.Name, SystemdUnitActionIds.Stop, "Stop user unit", confirm: true ),
            UnitAction( unit.Name, SystemdUnitActionIds.Restart, "Restart user unit", confirm: true ),
            new CommandAction
            {
                Id = SystemdUnitActionIds.CopyUnit,
                Title = "Copy unit name",
                Kind = ActionKinds.CopyText,
                Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = unit.Name }
            }
        ]
    };

    private static string DescribeState( SystemdUnit unit )
    {
        var state = unit.Active.Length > 0 ? unit.Active : unit.Load;
        if ( unit.Sub.Length > 0 && unit.Sub != unit.Active )
        {
            state = state.Length > 0 ? state + " (" + unit.Sub + ")" : unit.Sub;
        }
        return unit.Description.Length > 0 ? state + " · " + unit.Description : state;
    }

    private static string ReadString( JsonElement element, string name )
    {
        if ( !element.TryGetProperty( name, out var value ) || value.ValueKind != JsonValueKind.String )
        {
            return string.Empty;
        }

        var text = value.GetString( ) ?? string.Empty;
        if ( name == "unit" )
        {
            // Identity is validated, never truncated into a different unit name.
            return text;
        }

        var limit = name == "description" ? 2048 : 64;
        return text.Length <= limit ? text : text[..limit];
    }

    private static string Escape( string value ) => SanitizeBlock( value ).Replace( "\n", " ", StringComparison.Ordinal );

    private static string SanitizeBlock( string value )
    {
        StringBuilder builder = new( value.Length );
        foreach ( var character in value )
        {
            if ( character is '\n' or '\t' )
            {
                builder.Append( character );
            }
            else if ( char.IsControl( character ) )
            {
                builder.Append( '\uFFFD' );
            }
            else
            {
                builder.Append( character );
            }
        }
        return builder.ToString( ).TrimEnd( '\n' );
    }

    private static async ValueTask<ProcessResult> DefaultRunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken ) =>
        await ProcessRunner.RunBoundedAsync( fileName, arguments, workingDirectory: null, timeout,
            new ProcessExecutionLimits
            {
                MaximumStandardOutputBytes = 1024 * 1024,
                MaximumStandardErrorBytes = 4096,
                MaximumMemoryBytes = 128 * 1024 * 1024,
                ClearEnvironment = false
            }, cancellationToken )
            .ConfigureAwait( false );

    private sealed record SystemdUnit( string Name, string Load, string Active, string Sub, string Description );
}
