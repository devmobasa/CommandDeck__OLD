using System.Text.Json;

namespace CommandDeck;

public sealed partial class SqliteCatalog
{
    private static SearchResult CreateCatalogSearchResult(
        string stableId,
        string kind,
        string title,
        string? subtitle,
        string? path,
        string? icon,
        string? payload,
        string providerId,
        string sourceId,
        double score,
        bool isFavorite,
        List<string> aliases,
        string? exactAlias = null,
        RankingExplanation? ranking = null )
    {
        var metadata = BuildMetadata( path, payload );
        if ( exactAlias is not null )
        {
            metadata[ResultMetadataKeys.ExactAlias] = exactAlias;
        }

        return new SearchResult
        {
            Id = stableId,
            Kind = kind,
            Title = title,
            Subtitle = subtitle,
            Icon = icon,
            ProviderId = providerId,
            SourceId = sourceId,
            IsFavorite = isFavorite,
            Aliases = aliases,
            CanCustomize = CanCustomize( kind, sourceId ) && !ClipboardSequenceCommands.IsCommand( stableId ),
            Score = score,
            MatchedRanges = ranking?.Matches.FirstOrDefault( static match =>
                match.Field == RankingFieldNames.Title )?.Ranges ?? [],
            Ranking = ranking ?? new RankingExplanation { FinalScore = score },
            Details = BuildDetails( kind, path, metadata ),
            Actions = BuildActions( stableId, kind, title, path, payload ),
            Metadata = metadata
        };
    }

    private static List<DetailField> BuildDetails(
        string kind,
        string? path,
        IReadOnlyDictionary<string, string> metadata )
    {
        List<DetailField> details = [];
        switch ( kind )
        {
            case ResultKinds.Application:
                AddDetail(
                    details,
                    "Desktop file",
                    GetValueOrFallback( metadata, "desktopFile", path ),
                    monospace: true );
                AddDetail( details, "Desktop ID", metadata.GetValueOrDefault( "desktopId" ), true );
                AddDetail( details, "Command", metadata.GetValueOrDefault( "exec" ), true );
                AddDetail( details, "Terminal", FormatBoolean( metadata.GetValueOrDefault( "terminal" ) ) );
                break;

            case ResultKinds.Configuration:
                AddDetail( details, "File", path, monospace: true );
                AddDetail(
                    details,
                    "Key location",
                    FormatLocation( metadata, "keyLine", "keyColumn" ),
                    true );
                AddDetail(
                    details,
                    "Value location",
                    FormatLocation( metadata, "valueLine", "valueColumn" ),
                    true );
                AddDetail( details, "Key", metadata.GetValueOrDefault( "key" ), true );
                AddDetail( details, "Section", metadata.GetValueOrDefault( "section" ), true );
                AddDetail( details, "Value", metadata.GetValueOrDefault( "value" ) );
                break;

            case ResultKinds.Omarchy:
                AddDetail( details, "Command", metadata.GetValueOrDefault( "command" ), true );
                AddDetail( details, "Source", GetValueOrFallback( metadata, "source", path ), true );
                break;

            case ResultKinds.Quicklink:
                AddDetail( details, "URL", metadata.GetValueOrDefault( "urlTemplate" ), true );
                AddDetail( details, "Alias", metadata.GetValueOrDefault( "alias" ), true );
                break;

            case ResultKinds.CustomCommand:
                var argv = metadata.GetValueOrDefault( "argv" )?.Replace(
                    '\u001f',
                    ' ' );
                AddDetail( details, "Command", argv, true );
                AddDetail(
                    details,
                    "Working directory",
                    metadata.GetValueOrDefault( "workingDirectory" ),
                    true );
                AddDetail( details, "Confirmation", FormatBoolean( metadata.GetValueOrDefault( "confirm" ) ) );
                break;

            default:
                AddDetail( details, "Path", path, monospace: true );
                break;
        }

        return details;
    }

    private static void AddDetail(
        List<DetailField> details,
        string label,
        string? value,
        bool monospace = false )
    {
        if ( !string.IsNullOrWhiteSpace( value ) )
        {
            details.Add( new DetailField( label, value, monospace ) );
        }
    }

    private static string? FormatBoolean( string? value )
    {
        if ( string.IsNullOrWhiteSpace( value ) )
        {
            return null;
        }

        return string.Equals( value, "true", StringComparison.OrdinalIgnoreCase )
            ? "Yes"
            : "No";
    }

    private static string? GetValueOrFallback(
        IReadOnlyDictionary<string, string> values,
        string key,
        string? fallback ) =>
        values.TryGetValue( key, out var value ) ? value : fallback;

    private static List<CommandAction> BuildActions(
        string stableId,
        string kind,
        string title,
        string? path,
        string? payload )
    {
        List<CommandAction> actions = [];
        var values = ReadPayload( payload );

        if ( stableId == ClipboardHistoryNavigation.Id && kind == ResultKinds.CustomCommand )
        {
            return [ClipboardHistoryNavigation.CreateAction( )];
        }

        if ( ClipboardSequenceCommands.IsCommand( stableId ) && kind == ResultKinds.CustomCommand )
        {
            return [ClipboardSequenceCommands.CreateAction( stableId )];
        }

        switch ( kind )
        {
            case ResultKinds.Application:
                values["applicationName"] = title;
                actions.Add( new CommandAction
                {
                    Id = "launch",
                    Title = "Launch",
                    Kind = ActionKinds.LaunchDesktop,
                    Arguments = values
                } );
                break;

            case ResultKinds.File:
                if ( !string.IsNullOrWhiteSpace( path ) )
                {
                    actions.Add( Action( "open", "Open", ActionKinds.OpenPath, new PathActionPayload( path ).ToArguments( ) ) );
                    actions.Add( Action( "collect", "Toggle file selection", ActionKinds.CollectFile, new PathActionPayload( path ).ToArguments( ) ) );
                    actions.Add( Action( "browse-parent", "Browse containing folder", ActionKinds.BrowseFolder, new PathActionPayload( Path.GetDirectoryName( path ) ?? path ).ToArguments( ) ) );
                    actions.Add( Action( "reveal", "Reveal", ActionKinds.RevealPath, new PathActionPayload( path ).ToArguments( ) ) );
                    actions.Add( Action( "copy-path", "Copy Path", ActionKinds.CopyText, new TextActionPayload( path ).ToArguments( ) ) );
                    actions.Add( Action(
                        "copy-file-url",
                        "Copy File URL",
                        ActionKinds.CopyText,
                        new TextActionPayload( new Uri( Path.GetFullPath( path ) ).AbsoluteUri ).ToArguments( ) ) );
                }
                break;

            case ResultKinds.Configuration:
                if ( !string.IsNullOrWhiteSpace( path ) )
                {
                    Dictionary<string, string> editArguments = new( StringComparer.Ordinal )
                    {
                        ["path"] = path,
                        ["line"] = values.GetValueOrDefault( "keyLine",
                            values.GetValueOrDefault( "line", "1" ) ),
                        ["column"] = values.GetValueOrDefault( "keyColumn",
                            values.GetValueOrDefault( "column", "1" ) ),
                        ["key"] = values.GetValueOrDefault( "key", title )
                    };
                    actions.Add( new CommandAction
                    {
                        Id = "edit",
                        Title = "Edit at Key",
                        Kind = ActionKinds.EditAtLine,
                        Arguments = editArguments
                    } );
                    actions.Add( Action( "copy-value", "Copy Value", ActionKinds.CopyText,
                        new TextActionPayload( values.GetValueOrDefault( "value", title ) ).ToArguments( ) ) );
                    actions.Add( Action( "copy-key", "Copy Key", ActionKinds.CopyText,
                        new TextActionPayload( values.GetValueOrDefault( "key", title ) ).ToArguments( ) ) );
                    if ( values.TryGetValue( "assignment", out var assignment ) &&
                        !string.IsNullOrWhiteSpace( assignment ) )
                    {
                        actions.Add( Action(
                            "copy-assignment",
                            "Copy Assignment",
                            ActionKinds.CopyText,
                            new TextActionPayload( assignment ).ToArguments( ) ) );
                    }
                    actions.Add( Action( "reveal", "Reveal", ActionKinds.RevealPath, new PathActionPayload( path ).ToArguments( ) ) );
                }
                break;

            case ResultKinds.Omarchy:
                if ( values.GetValueOrDefault( "nativeRoute" ) == "true" )
                {
                    actions.Add( new CommandAction
                    {
                        Id = "open-native-menu",
                        Title = "Open Native Menu",
                        Kind = ActionKinds.RunArgv,
                        Arguments = values
                    } );
                }
                else
                {
                    actions.Add( new CommandAction
                    {
                        Id = "run",
                        Title = "Run",
                        Kind = ActionKinds.RunTrustedShell,
                        Arguments = values
                    } );
                }
                break;

            case ResultKinds.Quicklink:
                actions.Add( new CommandAction
                {
                    Id = "open",
                    Title = "Open",
                    Kind = ActionKinds.OpenUrl,
                    Arguments = values
                } );
                break;

            case ResultKinds.CustomCommand:
                actions.Add( new CommandAction
                {
                    Id = "run",
                    Title = "Run",
                    Kind = ActionKinds.RunArgv,
                    Arguments = values,
                    RequiresConfirmation = values.GetValueOrDefault( "confirm" ) == "true"
                } );
                break;
        }

        if ( actions.Count == 0 && !string.IsNullOrWhiteSpace( path ) )
        {
            actions.Add( Action( "open", "Open", ActionKinds.OpenPath, new PathActionPayload( path ).ToArguments( ) ) );
        }

        return actions;

        static CommandAction Action(
            string id,
            string actionTitle,
            string actionKind,
            Dictionary<string, string> arguments ) =>
            new( )
            {
                Id = id,
                Title = actionTitle,
                Kind = actionKind,
                Arguments = arguments
            };
    }

    private static string? FormatLocation(
        IReadOnlyDictionary<string, string> metadata,
        string lineKey,
        string columnKey )
    {
        var line = metadata.GetValueOrDefault( lineKey ) ??
                   metadata.GetValueOrDefault( "line" );
        if ( string.IsNullOrWhiteSpace( line ) )
        {
            return null;
        }

        var column = metadata.GetValueOrDefault( columnKey ) ??
                     metadata.GetValueOrDefault( "column" );
        return string.IsNullOrWhiteSpace( column ) ? line : $"{line}:{column}";
    }

    private static Dictionary<string, string> BuildMetadata( string? path, string? payload )
    {
        var values = ReadPayload( payload );
        if ( !string.IsNullOrWhiteSpace( path ) )
        {
            values["path"] = path;
        }

        return values;
    }

    private static Dictionary<string, string> ReadPayload( string? payload )
    {
        Dictionary<string, string> result = new( StringComparer.Ordinal );
        if ( string.IsNullOrWhiteSpace( payload ) )
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse( payload );
            foreach ( var property in document.RootElement.EnumerateObject( ) )
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString( ) ?? string.Empty,
                    JsonValueKind.Number => property.Value.GetRawText( ),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => string.Empty,
                    JsonValueKind.Array => string.Join(
                        "\u001f",
                        property.Value.EnumerateArray( ).Select( static value => value.GetString( ) ?? string.Empty ) ),
                    _ => property.Value.GetRawText( )
                };
            }
        }
        catch ( JsonException )
        {
            // An old/corrupt payload must not break search.
        }

        return result;
    }

    internal static List<string> SplitAliases( string value ) =>
        string.IsNullOrEmpty( value )
            ? []
            : value.Split( '\u001f', StringSplitOptions.RemoveEmptyEntries ).ToList( );

    internal static bool CanCustomize( string kind, string sourceId ) =>
        sourceId != SourceIds.OmarchySystem && kind is
            ResultKinds.Application or
            ResultKinds.File or
            ResultKinds.Configuration or
            ResultKinds.Omarchy or
            ResultKinds.Quicklink or
            ResultKinds.CustomCommand or ResultKinds.ExtensionCommand;
}
