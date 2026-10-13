using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task PersonalizationCopyAndPreviewLeaveUsageUnchangedAsync( )
    {
        var root = Path.Combine(
            Path.GetTempPath( ),
            $"commanddeck-usage-spec-{Guid.NewGuid( ):N}" );
        var previousPath = Environment.GetEnvironmentVariable( "PATH" ) ?? string.Empty;
        Directory.CreateDirectory( root );
        try
        {
            var documents = Path.Combine( root, "documents" );
            var tools = Path.Combine( root, "tools" );
            Directory.CreateDirectory( documents );
            Directory.CreateDirectory( tools );
            var note = Path.Combine( documents, "usage-note.txt" );
            await File.WriteAllTextAsync( note, "preview content" );
            var clipboard = Path.Combine( tools, "wl-copy" );
            await File.WriteAllTextAsync( clipboard, "#!/bin/sh\ncat >/dev/null\n" );
            if ( !OperatingSystem.IsWindows( ) )
            {
                File.SetUnixFileMode(
                    clipboard,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
            }
            Environment.SetEnvironmentVariable( "PATH", $"{tools}:{previousPath}" );

            var config = Path.Combine( root, "config.json" );
            CommandDeckOptions options = new( )
            {
                Storage = new StorageOptions
                {
                    DatabasePath = Path.Combine( root, "catalog.db" )
                },
                Search = new SearchOptions { LearnFromInvocations = true },
                Indexing = new IndexingOptions
                {
                    Roots = [new IndexRootOptions { Path = documents, IncludeContentInSearch = true }],
                    IndexDesktopApplications = false,
                    IndexOmarchyCommands = false
                },
                Privacy = new PrivacyOptions
                {
                    StoreUsageHistory = true,
                    StoreQueryHistory = true,
                    NeverIndex = []
                },
                Ai = new AiOptions { Enabled = false, Providers = [] }
            };
            await File.WriteAllTextAsync(
                config,
                JsonSerializer.Serialize( options, CommandDeckJsonContext.Default.CommandDeckOptions ) );

            await using var runtime = await CommandDeckRuntime.CreateAsync(
                config,
                CancellationToken.None );
            await runtime.Indexer.ReindexAsync( progress: null, CancellationToken.None );
            var result = (await runtime.SearchAsync(
                new SearchRequest { Query = "usage-note", Limit = 10 },
                CancellationToken.None )).Single( value => value.Kind == ResultKinds.File );

            await runtime.SetFavoriteAsync( result.Id, true, CancellationToken.None );
            await runtime.SetAliasAsync( result.Id, "usage-note", CancellationToken.None );
            var preferences = LauncherPreferences.CreateDefault( );
            await runtime.UpdatePreferencesAsync( new LauncherPreferences
            {
                Sources = preferences.Sources,
                ReduceMotion = true
            }, CancellationToken.None );
            var preview = await runtime.PreviewAsync(
                result.Id,
                CancellationToken.None );
            True( preview.Succeeded );
            var copy = result.Actions.Single( action => action.Id == "copy-path" );
            var copied = await runtime.InvokeAsync(
                result.Id,
                copy.Id,
                confirmed: false,
                CancellationToken.None );
            True( copied.Succeeded );

            await runtime.ResetCustomizationsAsync( CancellationToken.None );
            var home = await runtime.SearchAsync(
                new SearchRequest { Query = string.Empty, Limit = 20 },
                CancellationToken.None );
            True( home.All( candidate => candidate.Id != result.Id ) );
        }
        finally
        {
            Environment.SetEnvironmentVariable( "PATH", previousPath );
            Directory.Delete( root, recursive: true );
        }
    }
}
