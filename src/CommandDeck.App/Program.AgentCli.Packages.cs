using System.Text.Json;

namespace CommandDeck;

public static partial class Program
{
    private static async Task<int> RunAgentPackagesAsync(
        CommandDeckRuntime runtime,
        CliArguments arguments,
        IReadOnlyList<string> positionals,
        CancellationToken cancellationToken )
    {
        var action = positionals.Count > 1 ? positionals[1].ToLowerInvariant( ) : string.Empty;
        var json = arguments.HasFlag( CliOptionNames.Json );
        switch ( action )
        {
            case AgentCliActions.Search when positionals.Count >= 3:
                return await WritePackageSearchAsync( runtime, string.Join( ' ', positionals.Skip( 2 ) ), json, cancellationToken )
                    .ConfigureAwait( false );
            case AgentCliActions.Preview when positionals.Count >= 4:
                return await WritePackagePreviewAsync(
                    runtime, positionals[2].ToLowerInvariant( ), positionals.Skip( 3 ).ToArray( ), json, cancellationToken )
                    .ConfigureAwait( false );
            case AgentCliActions.Commit when positionals.Count >= 3 && !arguments.HasFlag( CliOptionNames.Yes ):
                Console.Error.WriteLine( "agent packages commit requires --yes." );
                return 2;
            case AgentCliActions.Commit when positionals.Count >= 3:
                return await WritePackageCommitAsync( runtime, positionals[2], json, cancellationToken ).ConfigureAwait( false );
            default:
                Console.Error.WriteLine(
                    "Use 'agent packages search QUERY', 'preview install|remove NAME...', or 'commit PREVIEW_ID --yes'." );
                return 2;
        }
    }

    private static async Task<int> WritePackageSearchAsync(
        CommandDeckRuntime runtime,
        string query,
        bool json,
        CancellationToken cancellationToken )
    {
        var search = await runtime.SearchPackagesAsync( query, 16, cancellationToken ).ConfigureAwait( false );
        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize( search, CommandDeckJsonContext.Default.PackageSearchResult ) );
            return 0;
        }
        if ( search.Error is not null )
        {
            Console.Error.WriteLine( Safe( search.Error ) );
            return 1;
        }
        foreach ( var record in search.Packages )
        {
            var installed = record.Installed ? " [installed]" : string.Empty;
            Console.WriteLine( $"{Safe( record.Repository ?? "?" )}/{Safe( record.Name )} {Safe( record.Version ?? string.Empty )}{installed}" );
        }
        Console.WriteLine( search.HelperAvailable
            ? "Package helper: installed"
            : "Package helper: not installed (previews only)" );
        return 0;
    }

    private static async Task<int> WritePackagePreviewAsync(
        CommandDeckRuntime runtime,
        string operation,
        IReadOnlyList<string> names,
        bool json,
        CancellationToken cancellationToken )
    {
        var preview = await runtime.PreviewPackagesAsync( operation, names, cancellationToken )
            .ConfigureAwait( false );
        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize( preview, CommandDeckJsonContext.Default.PackageTransactionPreview ) );
            return 0;
        }
        Console.WriteLine( $"Preview {Safe( preview.PreviewId )} ({Safe( preview.Operation )})" );
        foreach ( var entry in preview.Entries )
        {
            var dependency = entry.Requested ? string.Empty : " (dependency)";
            Console.WriteLine( $"  {Safe( entry.Name )} {Safe( entry.TargetVersion ?? entry.CurrentVersion ?? string.Empty )}{dependency}" );
        }
        foreach ( var warning in preview.Warnings )
        {
            Console.WriteLine( $"  warning: {Safe( warning )}" );
        }
        Console.WriteLine( $"  route: {Safe( string.Join( ' ', preview.Route ) )}" );
        Console.WriteLine( preview.Commitable
            ? "  commit: available with 'commanddeck agent packages commit PREVIEW_ID --yes'"
            : $"  commit: unavailable ({Safe( preview.Reason ?? "see warnings" )})" );
        return preview.Commitable ? 0 : 1;
    }

    private static async Task<int> WritePackageCommitAsync(
        CommandDeckRuntime runtime,
        string previewId,
        bool json,
        CancellationToken cancellationToken )
    {
        var commit = await runtime.CommitPackagesAsync( previewId, cancellationToken ).ConfigureAwait( false );
        if ( json )
        {
            Console.WriteLine( JsonSerializer.Serialize( commit, CommandDeckJsonContext.Default.PackageCommitResult ) );
        }
        else
        {
            Console.WriteLine( $"{Safe( commit.State )}: {Safe( commit.Message ?? string.Empty )}" );
        }
        return commit.State == PackageOperationStates.Completed ? 0 : 1;
    }
}
