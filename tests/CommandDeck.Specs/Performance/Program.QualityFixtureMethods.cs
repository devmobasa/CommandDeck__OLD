using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    // About one word in ten starts with s and two start with sy, roughly as in
    // English file names; folders repeat the way /usr and a home folder do.
    private static readonly string[] QualityWords =
    [
        "readme", "license", "changelog", "index", "main", "config", "preferences", "setup", "install", "system",
        "account", "profile", "service", "client", "handler", "query", "sample", "layout", "record", "import",
        "export", "overview", "report", "invoice", "notes", "todo", "draft", "budget", "plan", "meeting",
        "letter", "photo", "picture", "wallpaper", "icon", "theme", "font", "audio", "video", "music",
        "backup", "archive", "data", "test", "helper", "util", "common", "core", "types", "model",
        "view", "controller", "widget", "window", "panel", "dialog", "button", "menu", "keyboard", "mouse",
        "display", "network", "bluetooth", "power", "battery", "kernel", "module", "driver", "firmware", "library",
        "package", "manifest", "makefile", "cmake", "python", "rust", "dotnet", "node", "bash", "vim",
        "editor", "terminal", "browser", "mail", "calendar", "contacts", "chat", "game", "level", "texture",
        "mesh", "animation", "vector", "matrix", "buffer", "cursor", "gradient", "palette", "locale", "timezone",
        "server", "session", "sync", "style", "source", "summary", "storage", "string", "sort", "snapshot"
    ];

    private static readonly string[] QualityFolders =
    [
        "src", "lib", "include", "share", "doc", "docs", "icons", "hicolor", "apps", "locale",
        "man", "man1", "python3", "site-packages", "node_modules", "tests", "assets", "images", "config", "user",
        "fonts", "themes", "sounds", "pkgconfig", "gtk-4.0", "qt6", "modules", "firmware", "zoneinfo", "Documents",
        "Projects", "commanddeck", "notes", "reports", "invoices", "photos", "2025", "archive", "build", "dist",
        "vendor", "Downloads", "Pictures", "templates", "scripts", "cache", "backup", "applications", "mime", "plugins"
    ];

    private static readonly string[] QualityExtensions =
    [
        "txt", "md", "png", "svg", "json", "toml", "yaml", "conf", "sh", "py",
        "rs", "cs", "js", "ts", "h", "c", "so", "gz", "xml", "html", "css", "pdf", "desktop", "service"
    ];

    private static readonly string[] QualityRoots = ["/usr/share", "/usr/lib", "/usr/include", "/home/user", "/etc", "/opt"];

    // Deterministic stand-in for a real catalog: 100k files named and nested the
    // way /usr and a home folder are, so short prefixes match most rows and bm25
    // ties are as common as they are on disk; plus applications, configuration
    // keys and a tenth of the files with extracted text.
    private static async IAsyncEnumerable<IndexedItem> RealisticQualityItems( int fileCount )
    {
        Random random = new( 20_260_929 );
        for ( var index = 0; index < 120; index++ )
        {
            var name = $"{Pick( random, QualityWords )} {Pick( random, QualityWords )}";
            yield return new IndexedItem
            {
                StableId = $"app:quality-{index}.desktop",
                Kind = ResultKinds.Application,
                Title = name,
                Subtitle = "Application",
                Path = $"/usr/share/applications/quality-{index}.desktop",
                ProviderId = ProviderIds.Applications,
                SourceId = SourceIds.Applications
            };
        }

        for ( var index = 0; index < 500; index++ )
        {
            var key = $"{Pick( random, QualityWords )}_{Pick( random, QualityWords )}";
            yield return new IndexedItem
            {
                StableId = $"config:quality-{index}",
                Kind = ResultKinds.Configuration,
                Title = key,
                Subtitle = $"{Pick( random, QualityWords )}.conf",
                Path = $"/home/user/.config/{Pick( random, QualityFolders )}/{Pick( random, QualityWords )}.conf",
                ConfigKeys = key,
                ProviderId = ProviderIds.Config,
                SourceId = SourceIds.Configuration
            };
        }

        for ( var index = 0; index < fileCount; index++ )
        {
            var folder = string.Join( '/', Enumerable.Range( 0, random.Next( 2, 6 ) ).Select( _ => Pick( random, QualityFolders ) ) );
            var stem = random.Next( 3 ) switch
            {
                0 => Pick( random, QualityWords ),
                1 => $"{Pick( random, QualityWords )}-{Pick( random, QualityWords )}",
                _ => $"{Pick( random, QualityWords )}_{random.Next( 1_000 )}"
            };
            var title = $"{stem}.{Pick( random, QualityExtensions )}";
            var path = $"{Pick( random, QualityRoots )}/{folder}/{title}";
            yield return new IndexedItem
            {
                StableId = $"file:{path}#{index}",
                Kind = ResultKinds.File,
                Title = title,
                Subtitle = folder,
                Path = path,
                Body = index % 10 == 0
                    ? string.Join( ' ', Enumerable.Range( 0, 24 ).Select( _ => Pick( random, QualityWords ) ) )
                    : null,
                ProviderId = ProviderIds.Files,
                SourceId = SourceIds.Files
            };
            if ( index % 5_000 == 0 )
            {
                await Task.Yield( );
            }
        }
    }

    private static string Pick( Random random, string[] values ) => values[random.Next( values.Length )];

    // Three thousand used items, as on a desktop in daily use, spread over two
    // months so recency both boosts and breaks ties.
    private static async Task SeedQualityUsageAsync( string database )
    {
        await using SqliteConnection connection = new( $"Data Source={database};Pooling=False" );
        await connection.OpenAsync( );
        await using var seed = connection.CreateCommand( );
        seed.CommandText = """
            INSERT INTO usage(stable_id, use_count, last_used_unix_ms)
            SELECT stable_id, ((ordinal * 7) % 25) + 1,
                   unixepoch('now') * 1000 - ((ordinal * 17) % 61) * 86400000
            FROM (SELECT stable_id, ROW_NUMBER() OVER (ORDER BY stable_id) AS ordinal
                  FROM catalog_items WHERE (length(stable_id) + unicode(substr(stable_id, -1))) % 33 = 0
                  LIMIT 3000);
            """;
        await seed.ExecuteNonQueryAsync( );
    }
}
