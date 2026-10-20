namespace CommandDeck;

/// <summary>Result metadata the launcher reads, beyond presentation fields.</summary>
public static class ResultMetadataKeys
{
    public const string CatalogResultId = "catalogResultId";
    public const string ExactAlias = "exactAlias";

    // The text Tab puts in the search field for this row, instead of its title:
    // a completion row names the whole query with its value filled in.
    public const string CompletionText = "completionText";

    // Omarchy menu rows: "true" when the row's `when` guard failed (the row is
    // dimmed and scores lower) or its `checked` condition holds (it shows ✓).
    public const string OmarchyUnavailable = "omarchyUnavailable";
    public const string OmarchyChecked = "omarchyChecked";

    // Omarchy menu rows: the menu an entry is listed in ("root" at the top);
    // on a submenu row, the menu it opens, its label path ("Style › Theme") and
    // how many entries it lists.
    public const string OmarchyParent = "omarchyParent";
    public const string OmarchyMenu = "omarchyMenu";
    public const string OmarchyMenuPath = "omarchyMenuPath";
    public const string OmarchyMenuItems = "omarchyMenuItems";
}
