using System.Text.Json;

namespace CommandDeck;

public sealed record HerdrTabOutcome( bool Succeeded, string Message );

public sealed partial class HerdrClient
{
    public ValueTask<HerdrTabOutcome> CreateTabAsync( string workspaceId, string name, CancellationToken cancellationToken ) =>
        MutateTabAsync( workspaceId, name, create: true, cancellationToken );

    public ValueTask<HerdrTabOutcome> RenameTabAsync( string tabId, string name, CancellationToken cancellationToken ) =>
        MutateTabAsync( tabId, name, create: false, cancellationToken );

    private async ValueTask<HerdrTabOutcome> MutateTabAsync(
        string targetId, string name, bool create, CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        if ( !IsValidIdentifier( targetId ) || string.IsNullOrWhiteSpace( name ) ||
            name.Length > MaximumLabelLength || name.Any( char.IsControl ) )
        {
            return new( false, "Choose a valid Herdr target and a name of 1–512 characters without control characters." );
        }

        if ( !_available( ) )
        {
            return new( false, "Herdr is not on the daemon PATH." );
        }

        string[] arguments = create
            ? ["tab", "create", "--workspace", targetId, "--label", name, "--no-focus"]
            : ["tab", "rename", targetId, name];
        ProcessResult result;
        try
        {
            result = await RunAsync( arguments, FocusTimeout, FocusStandardOutputBytes,
                FocusStandardErrorBytes, cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            return new( false, "The Herdr client could not start." );
        }

        // A lost acknowledgement must never cause an automatic second creation.
        if ( result.TimedOut || result.AnyOutputLimitExceeded || result.MemoryLimitExceeded )
        {
            return new( false, "Herdr did not confirm the change; it may already have happened. Refresh before trying again." );
        }

        if ( result.ExitCode != 0 )
        {
            return new( false, "Herdr could not change this tab. Refresh to check that the space or tab still exists." );
        }

        return VerifyTabMutation( result.StandardOutput, targetId, name, create );
    }

    private static HerdrTabOutcome VerifyTabMutation( string json, string targetId, string name, bool create )
    {
        const string unconfirmed = "Herdr did not confirm the requested change. Refresh before trying again.";
        try
        {
            using var document = JsonDocument.Parse( json );
            var envelope = document.RootElement;
            if ( envelope.ValueKind != JsonValueKind.Object || envelope.TryGetProperty( "error", out _ ) ||
                !envelope.TryGetProperty( "result", out var payload ) || payload.ValueKind != JsonValueKind.Object ||
                ReadString( payload, "type" ) != (create ? "tab_created" : "tab_info") ||
                !payload.TryGetProperty( "tab", out var tab ) || tab.ValueKind != JsonValueKind.Object )
            {
                return new( false, unconfirmed );
            }

            var tabId = ReadString( tab, "tab_id" );
            var confirmedTarget = ReadString( tab, create ? "workspace_id" : "tab_id" );
            if ( tabId is null || !IsValidIdentifier( tabId ) || confirmedTarget != targetId || ReadString( tab, "label" ) != name )
            {
                return new( false, unconfirmed );
            }

            return new( true, create ? "Created Herdr tab." : "Renamed Herdr tab." );
        }
        catch ( JsonException )
        {
            return new( false, unconfirmed );
        }
    }
}
