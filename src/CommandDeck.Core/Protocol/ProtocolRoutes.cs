using System.Collections.Frozen;

namespace CommandDeck;

/// <summary>The handler family that serves an operation.</summary>
public enum ProtocolFamily
{
    Search,
    Suggest,
    Invoke,
    Preview,
    Status,
    Ping,
    Reindex,
    IndexControl,
    Catalog,
    Personalization,
    Files,
    Clipboard,
    ExtensionSession,
    Ai,
    AiManagement,
    Agent,
    Package
}

/// <summary>How a connection admits an operation before its handler runs.</summary>
public enum ProtocolAdmission
{
    /// <summary>Takes one of the connection's request slots and runs beside the others.</summary>
    Ordinary,

    /// <summary>Takes a slot and cancels the connection's previous search.</summary>
    ReplacesSearch,

    /// <summary>
    /// Creates, consumes, or purges the connection's AI state. It runs behind the
    /// connection's AI barrier and fails when ai.clear advanced the epoch first.
    /// </summary>
    AiOrdered,

    /// <summary>
    /// ai.clear: admitted even with every slot taken, it advances the AI epoch and
    /// waits for the AI-ordered requests admitted before it.
    /// </summary>
    AiClearBarrier
}

public readonly record struct ProtocolRoute( ProtocolFamily Family, ProtocolAdmission Admission );

/// <summary>
/// The one table from operation name to handler family and admission. Adding an
/// operation adds its constant and one row here. cancel has no route: the
/// connection serves it before admission.
/// </summary>
public static class ProtocolRoutes
{
    public static FrozenDictionary<string, ProtocolRoute> All { get; } = Build( );

    public static bool TryResolve( string operation, out ProtocolRoute route ) =>
        All.TryGetValue( operation, out route );

    public static bool IsIn( string operation, ProtocolFamily family ) =>
        All.TryGetValue( operation, out var route ) && route.Family == family;

    private static FrozenDictionary<string, ProtocolRoute> Build( )
    {
        Dictionary<string, ProtocolRoute> routes = new( StringComparer.Ordinal );

        Add( routes, ProtocolFamily.Search, ProtocolAdmission.ReplacesSearch, ProtocolOperations.Search );
        Add( routes, ProtocolFamily.Suggest, ProtocolOperations.Suggest );
        Add( routes, ProtocolFamily.Invoke, ProtocolOperations.Invoke );
        Add( routes, ProtocolFamily.Preview, ProtocolOperations.Preview );
        Add( routes, ProtocolFamily.Status, ProtocolOperations.Status );
        Add( routes, ProtocolFamily.Ping, ProtocolOperations.Ping );
        Add( routes, ProtocolFamily.Reindex, ProtocolOperations.Reindex );
        Add( routes, ProtocolFamily.IndexControl, ProtocolOperations.IndexPause, ProtocolOperations.IndexResume );
        Add( routes, ProtocolFamily.Files,
            ProtocolOperations.FilesSelect, ProtocolOperations.FilesClear, ProtocolOperations.FilesBatch );

        Add( routes, ProtocolFamily.Catalog,
            ProtocolOperations.CatalogInspect, ProtocolOperations.PrivacyAudit, ProtocolOperations.DatabaseIntegrity,
            ProtocolOperations.DatabaseOptimize, ProtocolOperations.DatabaseRepair, ProtocolOperations.DatabasePurge );

        Add( routes, ProtocolFamily.Personalization,
            ProtocolOperations.ExtensionsManage, ProtocolOperations.Bootstrap,
            ProtocolOperations.PreferencesGet, ProtocolOperations.PreferencesUpdate,
            ProtocolOperations.FavoriteSet, ProtocolOperations.FavoriteRemoveOrphan, ProtocolOperations.FavoriteReorder,
            ProtocolOperations.FavoriteMove,
            ProtocolOperations.AliasSet, ProtocolOperations.FilterAliasSet,
            ProtocolOperations.CustomizationsExport, ProtocolOperations.CustomizationsReset,
            ProtocolOperations.HiddenSet, ProtocolOperations.HiddenRestore, ProtocolOperations.HiddenList,
            ProtocolOperations.QueryHistoryGet, ProtocolOperations.QueryHistoryDelete, ProtocolOperations.QueryHistoryReset,
            ProtocolOperations.SavedSearchGet, ProtocolOperations.SavedSearchSave, ProtocolOperations.SavedSearchDelete );

        Add( routes, ProtocolFamily.Clipboard,
            ProtocolOperations.ClipboardOpen, ProtocolOperations.ClipboardClose, ProtocolOperations.ClipboardQuery,
            ProtocolOperations.ClipboardPreview, ProtocolOperations.ClipboardAction,
            ProtocolOperations.ClipboardSettingsGet, ProtocolOperations.ClipboardSettingsSet,
            ProtocolOperations.ClipboardMigrationAcknowledge, ProtocolOperations.ClipboardActionsPrepare,
            ProtocolOperations.ClipboardReleaseAbort, ProtocolOperations.LauncherOpenCancel );

        Add( routes, ProtocolFamily.ExtensionSession,
            ProtocolOperations.ExtensionOpen, ProtocolOperations.ExtensionPresent,
            ProtocolOperations.ExtensionAction, ProtocolOperations.ExtensionClose );

        // Inventory reads and settings, selection, and template writes are ordinary
        // requests: a write changes authority only under the preflight service's
        // global gate, which rejects every older preflight whichever connection made
        // the change. Behind the AI barrier they would wait for a streaming answer.
        Add( routes, ProtocolFamily.Ai,
            ProtocolOperations.AiProvidersGet, ProtocolOperations.AiModelsGet,
            ProtocolOperations.AiSelectionGet, ProtocolOperations.AiSelectionUpdate,
            ProtocolOperations.AiTemplatesGet, ProtocolOperations.AiTemplatesSave, ProtocolOperations.AiTemplatesDelete );
        Add( routes, ProtocolFamily.Ai, ProtocolAdmission.AiOrdered,
            ProtocolOperations.AiPreflight, ProtocolOperations.AiStart, ProtocolOperations.AiOutput );
        Add( routes, ProtocolFamily.Ai, ProtocolAdmission.AiClearBarrier, ProtocolOperations.AiClear );
        Add( routes, ProtocolFamily.AiManagement,
            ProtocolOperations.AiSettingsGet, ProtocolOperations.AiSettingsUpdate,
            ProtocolOperations.AiProvidersSave, ProtocolOperations.AiProvidersDelete, ProtocolOperations.AiProvidersTest );

        Add( routes, ProtocolFamily.Agent,
            ProtocolOperations.AgentBackendsGet, ProtocolOperations.AgentStatusGet, ProtocolOperations.AgentModelsGet,
            ProtocolOperations.AgentPreferencesGet, ProtocolOperations.AgentPreferencesUpdate,
            ProtocolOperations.AgentLoginStart, ProtocolOperations.AgentLoginCancel,
            ProtocolOperations.AgentLoginComplete, ProtocolOperations.AgentLogout,
            ProtocolOperations.AgentConversationGet, ProtocolOperations.AgentTurnStart, ProtocolOperations.AgentTurnInput,
            ProtocolOperations.AgentTurnCancel, ProtocolOperations.AgentClear, ProtocolOperations.AgentPrewarm,
            ProtocolOperations.AgentTrustGet, ProtocolOperations.AgentTrustChallenge, ProtocolOperations.AgentTrustUpdate,
            ProtocolOperations.AgentGrantsGet, ProtocolOperations.AgentGrantsAdd,
            ProtocolOperations.AgentGrantsRemove, ProtocolOperations.AgentGrantsClear,
            ProtocolOperations.AgentContextResolve, ProtocolOperations.AgentOperationsGet, ProtocolOperations.AgentOutput );
        Add( routes, ProtocolFamily.Package,
            ProtocolOperations.PackageSearch, ProtocolOperations.PackagePreview,
            ProtocolOperations.PackageCommit, ProtocolOperations.PackageCancel );

        return routes.ToFrozenDictionary( StringComparer.Ordinal );
    }

    private static void Add(
        Dictionary<string, ProtocolRoute> routes,
        ProtocolFamily family,
        params ReadOnlySpan<string> operations ) =>
        Add( routes, family, ProtocolAdmission.Ordinary, operations );

    // Dictionary.Add rejects a second row for one operation when the table loads.
    private static void Add(
        Dictionary<string, ProtocolRoute> routes,
        ProtocolFamily family,
        ProtocolAdmission admission,
        params ReadOnlySpan<string> operations )
    {
        foreach ( var operation in operations )
        {
            routes.Add( operation, new ProtocolRoute( family, admission ) );
        }
    }
}
