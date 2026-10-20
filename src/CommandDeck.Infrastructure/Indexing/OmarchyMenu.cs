namespace CommandDeck;

// Omarchy's menu as its own menu reads it. Documents merge in order: an entry
// that reuses an ID replaces the earlier one and keeps its place. Each entry
// sits under the menu its `parent` field names, or else the one its dotted ID
// implies ("root" at the top), and a menu lists its entries in that order.
// Entries with an action become action rows; entries without one become
// submenu rows when they list at least one row, directly or through a
// `target` link. The Apps and System menus are left to their own surfaces,
// and menus Omarchy fills at runtime through a provider list nothing here.
public sealed partial class OmarchyMenu
{
    public const string RootId = "root";
    private const int MaximumDepth = 32;

    private readonly Dictionary<string, MenuEntry> _entries;
    private readonly Dictionary<string, List<string>> _children;
    private readonly Dictionary<string, IndexedItem?> _rows = new( StringComparer.Ordinal );
    private readonly HashSet<string> _resolving = new( StringComparer.Ordinal );

    private OmarchyMenu( Dictionary<string, MenuEntry> entries, List<string> order )
    {
        _entries = entries;
        _children = new( StringComparer.Ordinal );
        foreach ( var id in order )
        {
            var parent = ParentOf( entries[id] );
            if ( parent is null )
            {
                continue;
            }

            if ( !_children.TryGetValue( parent, out var siblings ) )
            {
                siblings = [];
                _children[parent] = siblings;
            }

            siblings.Add( id );
        }

        // Every entry's row is resolved here, so concurrent searches only read.
        List<IndexedItem> rows = [];
        foreach ( var id in entries.Keys.Order( StringComparer.Ordinal ) )
        {
            if ( Row( id, 0 ) is { } row )
            {
                rows.Add( row );
            }
        }

        rows.Add( CreateRoute( RootId, "Omarchy Control", SourceIds.OmarchyControl ) );
        rows.Add( CreateRoute( "system", "System Menu", SourceIds.OmarchySystem ) );
        Rows = rows;
    }

    // Every catalog row: action and submenu rows by ID, then the two rows that
    // open Omarchy's native menu.
    public IReadOnlyList<IndexedItem> Rows { get; }

    public static OmarchyMenu Parse( IEnumerable<OmarchyMenuDocument> documents )
    {
        Dictionary<string, MenuEntry> entries = new( StringComparer.Ordinal );
        List<string> order = [];
        foreach ( var source in documents )
        {
            foreach ( var entry in ParseDocument( source ) )
            {
                if ( !entries.ContainsKey( entry.Id ) )
                {
                    order.Add( entry.Id );
                }

                entries[entry.Id] = entry;
            }
        }

        return new OmarchyMenu( entries, order );
    }

    // The rows a submenu lists, in Omarchy's order, each with the extra words
    // Omarchy's own filter matches. Null when the ID names no submenu this
    // menu can list: an unknown ID, an action, a link, the root, or the Apps
    // and System menus.
    public IReadOnlyList<OmarchyMenuRow>? Children( string menuId )
    {
        if ( !_entries.TryGetValue( menuId, out var entry ) || entry.IsAction || entry.IsLink || IsExcluded( menuId ) )
        {
            return null;
        }

        List<OmarchyMenuRow> rows = [];
        foreach ( var id in _children.GetValueOrDefault( menuId ) ?? [] )
        {
            if ( Row( id, 0 ) is { } row )
            {
                rows.Add( new OmarchyMenuRow( row, SearchTerms( _entries[id] ) ) );
            }
        }

        return rows;
    }

    // The label Omarchy shows for a menu.
    public string? Label( string menuId ) =>
        _entries.TryGetValue( menuId, out var entry ) ? LabelOf( entry ) : null;

    private IndexedItem? Row( string id, int depth )
    {
        if ( _rows.TryGetValue( id, out var known ) )
        {
            return known;
        }

        // A link or explicit parent can loop back; a menu still being resolved
        // lists no rows through that path.
        if ( depth > MaximumDepth || !_resolving.Add( id ) )
        {
            return null;
        }

        var entry = _entries[id];
        IndexedItem? row = null;
        if ( !IsExcluded( id ) )
        {
            row = entry.IsAction ? CreateAction( entry ) : CreateSubmenu( entry, depth );
        }

        _resolving.Remove( id );
        _rows[id] = row;
        return row;
    }

    private IndexedItem? CreateSubmenu( MenuEntry entry, int depth )
    {
        var target = entry.IsLink ? entry.Target! : entry.Id;
        if ( target == RootId || IsExcluded( target ) ||
            !_entries.TryGetValue( target, out var menu ) || menu.IsAction || (menu.IsLink && target != entry.Id) )
        {
            return null;
        }

        var items = (_children.GetValueOrDefault( target ) ?? [])
            .Count( child => child != entry.Id && Row( child, depth + 1 ) is not null );
        if ( items == 0 )
        {
            return null;
        }

        var title = LabelOf( entry );
        var parentPath = BuildPath( target );
        var menuPath = parentPath is null ? LabelOf( menu ) : $"{parentPath} › {LabelOf( menu )}";
        return new IndexedItem
        {
            StableId = $"omarchy:{entry.Id}",
            Kind = ResultKinds.Omarchy,
            Title = title,
            Subtitle = BuildPath( entry.Id ) ?? "Omarchy",
            Icon = entry.Icon ?? "omarchy",
            Body = Join( entry.Id, title, entry.Target, string.Join( ' ', entry.Aliases ) ),
            PayloadJson = PayloadWriter.Create(
                ("source", entry.SourcePath),
                ("menuId", entry.Id),
                ("menuRoot", RootOf( entry.Id )),
                ("sourceGroup", SourceIds.OmarchyControl),
                (OmarchyMenuState.WhenKey, entry.When),
                (OmarchyMenuState.CheckedKey, entry.Checked),
                (ResultMetadataKeys.OmarchyParent, ParentOf( entry )),
                (ResultMetadataKeys.OmarchyMenu, target),
                (ResultMetadataKeys.OmarchyMenuPath, menuPath),
                (ResultMetadataKeys.OmarchyMenuItems, items.ToString( System.Globalization.CultureInfo.InvariantCulture )) ),
            ProviderId = ProviderIds.Omarchy,
            SourceId = SourceIds.OmarchyControl
        };
    }

    private IndexedItem CreateAction( MenuEntry entry )
    {
        var title = LabelOf( entry );
        return new IndexedItem
        {
            StableId = $"omarchy:{entry.Id}",
            Kind = ResultKinds.Omarchy,
            Title = title,
            Subtitle = BuildPath( entry.Id ),
            Icon = entry.Icon ?? "omarchy",
            Body = Join( entry.Id, title, entry.Command, entry.Target, entry.Provider, string.Join( ' ', entry.Aliases ) ),
            PayloadJson = PayloadWriter.Create(
                ("command", entry.Command),
                ("source", entry.SourcePath),
                ("menuId", entry.Id),
                ("menuRoot", RootOf( entry.Id )),
                ("sourceGroup", SourceIds.OmarchyControl),
                (OmarchyMenuState.WhenKey, entry.When),
                (OmarchyMenuState.CheckedKey, entry.Checked),
                (ResultMetadataKeys.OmarchyParent, ParentOf( entry )) ),
            ProviderId = ProviderIds.Omarchy,
            SourceId = SourceIds.OmarchyControl
        };
    }

    private static IndexedItem CreateRoute( string route, string title, string sourceId ) => new( )
    {
        StableId = $"omarchy-route:{route}",
        Kind = ResultKinds.Omarchy,
        Title = title,
        Subtitle = "Open Omarchy's native menu",
        Icon = route == "system" ? "system-shutdown" : "omarchy",
        Body = $"{title} {route}",
        PayloadJson = PayloadWriter.Create(
            ("argv", $"omarchy-menu\u001ftoggle\u001f{route}"),
            ("menuId", route),
            ("menuRoot", route),
            ("sourceGroup", sourceId),
            ("nativeRoute", "true") ),
        ProviderId = ProviderIds.Omarchy,
        SourceId = sourceId
    };

    // Omarchy's filter matches a row's label, its last ID segment and its
    // aliases, with dots, dashes and underscores read as spaces.
    private static List<string> SearchTerms( MenuEntry entry ) =>
    [
        .. entry.Aliases.Prepend( entry.Id.Split( '.' ).Last( ) )
            .Select( static term => string.Join( ' ', term.Split( ['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries ) ) )
            .Where( static term => term.Length > 0 )
            .Distinct( StringComparer.OrdinalIgnoreCase )
    ];

    private string? BuildPath( string id )
    {
        var segments = id.Split( '.' );
        if ( segments.Length <= 1 )
        {
            return null;
        }

        List<string> labels = [];
        for ( var index = 1; index < segments.Length; index++ )
        {
            var parent = string.Join( '.', segments.Take( index ) );
            labels.Add( _entries.TryGetValue( parent, out var value )
                ? value.Label ?? value.Title ?? segments[index - 1]
                : segments[index - 1] );
        }

        return string.Join( " › ", labels );
    }

    private static string LabelOf( MenuEntry entry ) => entry.Label ?? entry.Title ?? entry.Id.Split( '.' ).Last( );

    // An empty `parent` lists the entry in no menu, as in Omarchy.
    private static string? ParentOf( MenuEntry entry )
    {
        if ( entry.Id == RootId )
        {
            return null;
        }

        if ( entry.Parent is not null )
        {
            return entry.Parent.Length == 0 ? null : entry.Parent;
        }

        var dot = entry.Id.LastIndexOf( '.' );
        return dot > 0 ? entry.Id[..dot] : RootId;
    }

    private static string RootOf( string id ) => id.Split( '.', 2 )[0];

    private static bool IsExcluded( string id ) => RootOf( id ) is "apps" or "system";

    private static string Join( params string?[] values ) =>
        string.Join( ' ', values.Where( static value => !string.IsNullOrWhiteSpace( value ) ) );
}

// One row a submenu lists, with the extra words Omarchy's own filter matches.
public sealed record OmarchyMenuRow( IndexedItem Item, IReadOnlyList<string> Terms );
