using System.Text.RegularExpressions;

namespace CommandDeck;


public sealed class SavedSearch
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public QueryExpression Expression { get; init; } = new( );
    public string? Shortcut { get; init; }
    public bool ShowOnHome { get; init; }
    public int Position { get; init; }
    public bool Unavailable { get; init; }
}

public sealed class SavedSearchResult
{
    public List<SavedSearch> Searches { get; init; } = [];
    public string? Message { get; init; }
}

public static partial class SavedSearchValidator
{
    public const int MaximumSearches = 64;

    [GeneratedRegex( "^[^\\p{Cc}\\p{Cf}]{1,64}$", RegexOptions.CultureInvariant )]
    private static partial Regex ValidNameRegex( );

    [GeneratedRegex( "^(?:(?:SUPER|CTRL|ALT|SHIFT)\\+){1,4}[A-Z0-9][A-Z0-9._-]{0,15}$",
        RegexOptions.CultureInvariant )]
    private static partial Regex ValidShortcutRegex( );

    public static string ValidateName( string name )
    {
        var value = name.Trim( );
        if ( !ValidNameRegex( ).IsMatch( value ) )
        {
            throw new InvalidOperationException(
                "Saved-search names must be 1-64 visible characters without controls." );
        }
        return value;
    }

    public static QueryExpression ValidateExpression( QueryExpression expression, ContributionRegistrySnapshot? registry = null )
    {
        var validated = QueryParser.ValidateShape( expression );
        var query = QueryParser.Parse( validated.LiteralQuery, validated, registry: registry );
        if ( ClipboardPrivacy.ReferencesHistory( query ) )
        {
            throw new InvalidOperationException( "Clipboard queries cannot be saved." );
        }

        if ( PrivateSources.ReferencesMail( query ) )
        {
            throw new InvalidOperationException( "HEY searches cannot be saved." );
        }

        return query.Expression;
    }

    public static string? NormalizeShortcut( string? shortcut )
    {
        if ( string.IsNullOrWhiteSpace( shortcut ) )
        {
            return null;
        }
        var raw = shortcut.Split( '+', StringSplitOptions.TrimEntries |
                                       StringSplitOptions.RemoveEmptyEntries );
        var key = raw[^1].ToUpperInvariant( );
        var modifiers = raw[..^1]
            .Select( static value => value.ToUpperInvariant( ) switch
            {
                "SUPER" or "META" => "SUPER",
                "CONTROL" or "CTRL" => "CTRL",
                "ALT" => "ALT",
                "SHIFT" => "SHIFT",
                _ => throw new InvalidOperationException( "Unknown shortcut modifier." )
            } )
            .ToHashSet( StringComparer.Ordinal );
        string[] order = ["SUPER", "CTRL", "ALT", "SHIFT"];
        var normalized = string.Join( '+', order.Where( modifiers.Contains ).Append( key ) );
        if ( modifiers.Count != raw.Length - 1 || !ValidShortcutRegex( ).IsMatch( normalized ) )
        {
            throw new InvalidOperationException(
                "Shortcuts require unique modifiers and one ASCII key, for example Ctrl+Alt+S." );
        }
        return normalized;
    }

    public static void EnsureShortcutAvailable(
        string? shortcut,
        string savedSearchId,
        IReadOnlyList<SavedSearch> existing,
        ShortcutInventory inventory )
    {
        var normalized = NormalizeShortcut( shortcut );
        if ( normalized is null )
        {
            return;
        }
        HashSet<string> builtIns =
        [
            "CTRL+/", "CTRL+,", "CTRL+N", "CTRL+P", "CTRL+HOME", "CTRL+END",
            "CTRL+ENTER", "CTRL+SHIFT+ENTER", "CTRL+U", "CTRL+UP", "CTRL+DOWN",
            "CTRL+K", "CTRL+D", "CTRL+SHIFT+C", "ALT+SHIFT+A", "ALT+SHIFT+D", "CTRL+0", "CTRL+I",
            .. Enumerable.Range(1, 9).Select(static value => $"CTRL+{value}")
        ];
        if ( builtIns.Contains( normalized ) )
        {
            throw new InvalidOperationException(
                $"Shortcut '{normalized}' is reserved by CommandDeck." );
        }
        var duplicate = existing.FirstOrDefault( value =>
            !string.Equals( value.Id, savedSearchId, StringComparison.Ordinal ) &&
            string.Equals( value.Shortcut, normalized, StringComparison.OrdinalIgnoreCase ) );
        if ( duplicate is not null )
        {
            throw new InvalidOperationException(
                $"Shortcut '{normalized}' is already assigned to '{duplicate.Name}'." );
        }
        if ( !inventory.Available )
        {
            throw new InvalidOperationException(
                $"Shortcut conflicts cannot be checked: {inventory.Detail ?? "Hyprland bindings are unavailable."}" );
        }
        if ( inventory.Chords.Contains( normalized, StringComparer.OrdinalIgnoreCase ) )
        {
            throw new InvalidOperationException(
                $"Shortcut '{normalized}' conflicts with an effective Hyprland binding." );
        }
    }
}
