using System.Text.Json;

namespace CommandDeck.Specs;

public static partial class Program
{
    // Discovery stays gated, bounded, cancellable, and query-filtered even when
    // a provider adds explicitly confirmed controls.
    private static async Task AssertProviderDiscoveryContractAsync(
        Func<ExternalProcessRunner, Func<bool>, ICommandProvider> factory,
        ParsedQuery activatingQuery,
        ParsedQuery inertQuery,
        string listingJson,
        bool confirmedControls = false )
    {
        ExternalProcessRunner ok = ( _, _, _, _ ) =>
            ValueTask.FromResult( new ProcessResult { ExitCode = 0, StandardOutput = listingJson, StandardError = "" } );

        // 1. Empty and no process when the backing tool is absent.
        var calls = 0;
        ExternalProcessRunner counting = ( f, a, t, c ) =>
        {
            calls++;
            return ok( f, a, t, c );
        };
        var missing = factory( counting, ( ) => false );
        Equal( 0, (await missing.SearchAsync( activatingQuery, new SearchRequest { Query = "", Limit = 20 }, default )).Count );
        Equal( 0, calls );

        // 2. Empty and no process for a query that does not target the provider.
        var present = factory( counting, ( ) => true );
        Equal( 0, (await present.SearchAsync( inertQuery, new SearchRequest { Query = "", Limit = 20 }, default )).Count );
        Equal( 0, calls );

        // 3. Bounded by the request limit.
        var limited = await present.SearchAsync(
            activatingQuery, new SearchRequest { Query = "", Limit = 1 }, default );
        True( limited.Count <= 1, "results honor the request limit" );

        // 4. Mutations require confirmation; discovery detail is bounded and control-free.
        var all = await present.SearchAsync(
            activatingQuery, new SearchRequest { Query = "", Limit = 20 }, default );
        True( all.Count > 0, "the activating query returns results" );
        foreach ( var result in all )
        {
            foreach ( var action in result.Actions )
            {
                True( action.Kind is ActionKinds.CopyText || (confirmedControls &&
                    action.Kind == ActionKinds.SystemdUnit &&
                    (action.Arguments["operation"] is "logs" or "status" or "edit" || action.RequiresConfirmation)),
                    "provider mutations require confirmation" );
            }
            var detail = result.DetailMarkdown ?? string.Empty;
            True( detail.Length <= 16 * 1024, "detail is bounded" );
            True( !detail.Any( static c => char.IsControl( c ) && c is not '\n' and not '\t' ),
                "detail carries no raw control characters" );
            foreach ( var field in result.Details )
            {
                True( field.Value.Length <= 16 * 1024, "detail field is bounded" );
                True( !field.Value.Any( static c => char.IsControl( c ) && c is not '\n' and not '\t' ),
                    "detail field carries no raw control characters" );
            }
        }

        // 5. Cancellation is honored.
        using CancellationTokenSource cancelled = new( );
        cancelled.Cancel( );
        await ThrowsAsync<OperationCanceledException>( async ( ) =>
            await present.SearchAsync( activatingQuery, new SearchRequest { Query = "", Limit = 20 }, cancelled.Token ) );
    }

    private static async Task SystemdProviderDiscoversUnitsAsync( )
    {
        var json = JsonSerializer.Serialize( new[]
        {
            new { unit = "codex.service", load = "loaded", active = "active", sub = "running", description = new string( 'x', 20000 ) },
            new { unit = "backup.service", load = "loaded", active = "inactive", sub = "dead", description = "Nightly backup" },
            new { unit = "sshd.socket", load = "loaded", active = "active", sub = "listening", description = "not a service" },
            new { unit = "backup.timer", load = "loaded", active = "active", sub = "waiting", description = "Nightly schedule" }
        } );
        ExternalProcessRunner runner = ( file, _, _, _ ) =>
            ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = file == "systemctl" ? json : "line one\nline two",
                StandardError = ""
            } );

        var activating = QueryParser.Parse( "svc: backup.service" );
        var inert = QueryParser.Parse( "backup" );
        True( activating.IncludedFilters.Any( filter => filter.SourceId == SourceIds.SystemdUnits ),
            "the svc: token targets the systemd source" );

        SystemdUserServiceProvider provider = new( runner, ( ) => true );
        var filtered = await provider.SearchAsync(
            activating, new SearchRequest { Query = "backup", Limit = 20 }, default );
        Equal( 1, filtered.Count );
        Equal( "systemd:backup.service", filtered[0].Id );
        Equal( SourceIds.SystemdUnits, filtered[0].SourceId );
        True( filtered[0].DetailMarkdown!.Contains( "inactive", StringComparison.Ordinal ), "detail reports the unit state" );
        True( !filtered[0].DetailMarkdown!.Contains( "line one", StringComparison.Ordinal ), "search does not load logs" );
        Equal( "logs", filtered[0].Actions[0].Id );

        // A .socket unit is not a service and is dropped.
        var all = await provider.SearchAsync(
            QueryParser.Parse( "svc:" ), new SearchRequest { Query = "", Limit = 20 }, default );
        Equal( 3, all.Count );
        True( all.Any( result => result.Title == "backup.timer" ), "timers are listed" );
        True( all.All( result => result.DetailMarkdown?.Length < 16 * 1024 ), "long descriptions remain bounded" );

        await AssertProviderDiscoveryContractAsync(
            ( r, a ) => new SystemdUserServiceProvider( r, a ), activating, inert, json, confirmedControls: true );
    }

    private static async Task SystemdProviderStaysSilentOnFailureAsync( )
    {
        var activating = QueryParser.Parse( "svc: anything" );
        SystemdUserServiceProvider failing = new(
            ( _, _, _, _ ) => ValueTask.FromResult( new ProcessResult { ExitCode = 1, StandardOutput = "", StandardError = "boom" } ),
            ( ) => true );
        Equal( 0, (await failing.SearchAsync( activating, new SearchRequest { Query = "", Limit = 20 }, default )).Count );

        SystemdUserServiceProvider malformed = new(
            ( _, _, _, _ ) => ValueTask.FromResult( new ProcessResult { ExitCode = 0, StandardOutput = "not json", StandardError = "" } ),
            ( ) => true );
        Equal( 0, (await malformed.SearchAsync( activating, new SearchRequest { Query = "", Limit = 20 }, default )).Count );

        SystemdUserServiceProvider overflowing = new(
            ( _, _, _, _ ) => ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = SystemdListing,
                StandardError = "",
                OutputLimitExceeded = true
            } ), static ( ) => true );
        Equal( 0, (await overflowing.SearchAsync( QueryParser.Parse( "svc:" ), new SearchRequest( ), default )).Count );
    }

}
