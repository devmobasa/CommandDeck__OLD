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

    private static async Task CalculatorProviderDelegatesUnitsToQalcAndKeepsArithmeticLocalAsync( )
    {
        List<string[]> calls = [];
        ExternalProcessRunner runner = ( _, arguments, _, _ ) =>
        {
            calls.Add( [.. arguments] );
            return ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "62.137 mi\n",
                StandardError = ""
            } );
        };
        CalculatorProvider provider = new( runner, ( ) => true );
        var units = QueryParser.Parse( "= 100 km to miles" );
        var results = await provider.SearchAsync(
            units, new SearchRequest { Query = "= 100 km to miles", Limit = 5 }, default );
        Equal( 1, results.Count );
        Equal( "62.137 mi", results[0].Title );
        Equal( "100 km to miles", results[0].Subtitle );
        Equal( SourceIds.Calculator, results[0].SourceId );
        Equal( "62.137 mi", results[0].Actions[0].Arguments["text"] );
        Equal( 1, calls.Count );
        True( calls[0].Contains( "-t" ), "qalc runs terse" );
        True( calls[0].Contains( "-m" ), "qalc gets a millisecond budget" );
        True( calls[0].Contains( "--" ), "the expression follows --" );
        Equal( "100 km to miles", calls[0][^1] );
        True( !calls[0].Contains( "-e" ), "unit conversion does not refresh exchange rates" );

        calls.Clear( );
        var arithmetic = await provider.SearchAsync(
            QueryParser.Parse( "= 2 + 3 * 4" ),
            new SearchRequest { Query = "= 2 + 3 * 4", Limit = 5 },
            default );
        Equal( 1, arithmetic.Count );
        Equal( "14", arithmetic[0].Title );
        Equal( 0, calls.Count );

        await AssertProviderDiscoveryContractAsync(
            ( r, a ) => new CalculatorProvider( r, a ),
            units,
            QueryParser.Parse( "firefox" ),
            "62.137 mi\n" );
    }

    private static async Task CalculatorProviderNormalizesTemperatureShorthandAsync( )
    {
        // Pure normalizer: bare temperature shorthand becomes qalc's canonical form
        // (degree units and `to`, since qalc reads `in` as inches), while anything
        // that is not a two-temperature-letter conversion is returned untouched.
        Equal( "105 °F to °C", CalculatorProvider.NormalizeTemperatureConversion( "105F in C" ) );
        Equal( "105 °F to °C", CalculatorProvider.NormalizeTemperatureConversion( "105f to c" ) );
        Equal( "0 °C to °F", CalculatorProvider.NormalizeTemperatureConversion( "0C in F" ) );
        Equal( "300 K to °C", CalculatorProvider.NormalizeTemperatureConversion( "300K in C" ) );
        Equal( "-40 °F to °C", CalculatorProvider.NormalizeTemperatureConversion( "-40°F to °C" ) );
        Equal( "98.6 °F to °C", CalculatorProvider.NormalizeTemperatureConversion( "98.6 F to C" ) );
        Equal( "105 °F to °C", CalculatorProvider.NormalizeTemperatureConversion( "105 °F to °C" ) );
        Equal( "5 farad in coulomb", CalculatorProvider.NormalizeTemperatureConversion( "5 farad in coulomb" ) );
        Equal( "5m in cm", CalculatorProvider.NormalizeTemperatureConversion( "5m in cm" ) );
        Equal( "100C", CalculatorProvider.NormalizeTemperatureConversion( "100C" ) );
        Equal( "2 + 3 * 4", CalculatorProvider.NormalizeTemperatureConversion( "2 + 3 * 4" ) );

        // End to end: the provider sends the normalized expression to qalc and shows
        // it, so the wrong farad/coulomb reading of "105F in C" can never surface.
        List<string[]> calls = [];
        ExternalProcessRunner runner = ( _, arguments, _, _ ) =>
        {
            calls.Add( [.. arguments] );
            return ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "40.56 °C\n",
                StandardError = ""
            } );
        };
        CalculatorProvider provider = new( runner, ( ) => true );
        var results = await provider.SearchAsync(
            QueryParser.Parse( "= 105F in C" ),
            new SearchRequest { Query = "= 105F in C", Limit = 5 },
            default );
        Equal( 1, results.Count );
        Equal( "40.56 °C", results[0].Title );
        Equal( "105 °F to °C", results[0].Subtitle );
        Equal( "40.56 °C", results[0].Actions[0].Arguments["text"] );
        Equal( 1, calls.Count );
        Equal( "105 °F to °C", calls[0][^1] );
    }

    private static async Task CalculatorProviderFallsBackWhenQalcErrorsAndHonorsNeverAsync( )
    {
        var calls = 0;
        ExternalProcessRunner failing = ( _, _, _, _ ) =>
        {
            calls++;
            return ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 1,
                StandardOutput = "0",
                StandardError = "error: unknown"
            } );
        };
        CalculatorProvider missing = new( failing, ( ) => false );
        Equal( 0, (await missing.SearchAsync(
            QueryParser.Parse( "= 100 km to miles" ),
            new SearchRequest { Query = "= 100 km to miles", Limit = 5 },
            default )).Count );
        Equal( 0, calls );

        CalculatorProvider broken = new( failing, ( ) => true );
        Equal( 0, (await broken.SearchAsync(
            QueryParser.Parse( "= 100 km to miles" ),
            new SearchRequest { Query = "= 100 km to miles", Limit = 5 },
            default )).Count );
        True( calls > 0, "qalc was invoked when present" );

        calls = 0;
        CalculatorProvider always = new(
            failing,
            ( ) => false,
            new CalculatorOptions { UseQalc = CalculatorOptions.UseQalcAlways } );
        Equal( 0, (await always.SearchAsync(
            QueryParser.Parse( "= 100 km to miles" ),
            new SearchRequest { Query = "= 100 km to miles", Limit = 5 },
            default )).Count );
        True( calls > 0, "always still invokes qalc when PATH detection is negative" );

        calls = 0;
        CalculatorProvider disabled = new(
            failing,
            ( ) => true,
            new CalculatorOptions { UseQalc = CalculatorOptions.UseQalcNever } );
        Equal( 0, (await disabled.SearchAsync(
            QueryParser.Parse( "= 100 km to miles" ),
            new SearchRequest { Query = "= 100 km to miles", Limit = 5 },
            default )).Count );
        Equal( 0, calls );

        var local = await disabled.SearchAsync(
            QueryParser.Parse( "= 8 / 2" ),
            new SearchRequest { Query = "= 8 / 2", Limit = 5 },
            default );
        Equal( 1, local.Count );
        Equal( "4", local[0].Title );
    }

    private static async Task CalculatorProviderRefreshesCurrencyRatesAtMostOncePerCooldownAsync( )
    {
        AdjustableTimeProvider clock = new( new DateTimeOffset( 2026, 9, 3, 12, 0, 0, TimeSpan.Zero ) );
        List<(string[] Arguments, TimeSpan Timeout)> calls = [];
        var refreshAttempts = 0;
        ExternalProcessRunner runner = ( _, arguments, timeout, _ ) =>
        {
            string[] copy = [.. arguments];
            calls.Add( (copy, timeout) );
            if ( copy.Contains( "-e" ) )
            {
                refreshAttempts++;
                if ( refreshAttempts == 1 )
                {
                    return ValueTask.FromResult( new ProcessResult
                    {
                        ExitCode = -1,
                        StandardOutput = "",
                        StandardError = "",
                        TimedOut = true
                    } );
                }
            }

            return ValueTask.FromResult( new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "€8.74\n",
                StandardError = ""
            } );
        };
        CalculatorProvider provider = new( runner, ( ) => true, clock: clock );

        async Task<string> EvaluateAsync( )
        {
            var results = await provider.SearchAsync(
                QueryParser.Parse( "= 10 USD to EUR" ),
                new SearchRequest { Query = "= 10 USD to EUR", Limit = 5 },
                default );
            Equal( 1, results.Count );
            return results[0].Title;
        }

        Equal( "€8.74", await EvaluateAsync( ) );
        True( calls.Count >= 2, "a currency query evaluates and kicks a background refresh" );
        True( calls.Any( call => call.Arguments.Contains( "-e" ) &&
                call.Timeout == TimeSpan.FromMilliseconds( 3000 ) ),
            "rate refresh is a separate 3s -e run" );
        True( calls.Any( call =>
                call.Arguments[^1] == "10 USD to EUR" &&
                !call.Arguments.Contains( "-e" ) &&
                call.Timeout == TimeSpan.FromMilliseconds( 250 ) ),
            "the query itself is a single fast cached-rate run" );

        calls.Clear( );
        Equal( "€8.74", await EvaluateAsync( ) );
        True( calls.Any( call => call.Arguments.Contains( "-e" ) ),
            "a failed refresh does not consume the 15-minute cooldown" );
        True( calls.Any( call => call.Arguments[^1] == "10 USD to EUR" && !call.Arguments.Contains( "-e" ) ),
            "the retry still answers from cached rates" );

        calls.Clear( );
        Equal( "€8.74", await EvaluateAsync( ) );
        True( !calls.Any( call => call.Arguments.Contains( "-e" ) ),
            "a successful refresh blocks another fetch for 15 minutes" );
        Equal( 1, calls.Count );

        clock.Advance( TimeSpan.FromMinutes( 15 ) );
        calls.Clear( );
        Equal( "€8.74", await EvaluateAsync( ) );
        True( calls.Any( call => call.Arguments.Contains( "-e" ) ),
            "rates refresh again after the cooldown" );
    }

    private static async Task CalculatorLiveQalcSmokeAsync( )
    {
        if ( !ProcessRunner.ExistsOnPath( "qalc" ) )
        {
            SkipSpec( "qalc is not installed on PATH." );
        }

        CalculatorProvider provider = new( );
        foreach ( var (expression, expected) in new[]
        {
            ("21899.23922 BAM to BAM", "BAM 21899.24"),
            ("12.301 BAM to BAM", "BAM 12.30"),
            ("12 BAM to BAM", "BAM 12.00"),
            ("-12.346 BAM to BAM", "−BAM 12.35")
        } )
        {
            var money = await provider.SearchAsync(
                QueryParser.Parse( "= " + expression ),
                new SearchRequest { Query = "= " + expression, Limit = 5 }, default );
            Equal( 1, money.Count );
            Equal( expected, money[0].Title );
            Equal( expected, money[0].Actions[0].Arguments["text"] );
        }
        var units = await provider.SearchAsync(
            QueryParser.Parse( "= 100 km to miles" ),
            new SearchRequest { Query = "= 100 km to miles", Limit = 5 },
            default );
        Equal( 1, units.Count );
        True( units[0].Title.Length > 0, "live qalc returned a unit conversion" );
        True( !string.Equals( units[0].Title, "100 km to miles", StringComparison.OrdinalIgnoreCase ),
            "live qalc did not echo the input" );
        True(
            units[0].Title.Contains( "mi", StringComparison.OrdinalIgnoreCase ),
            "live qalc unit result includes miles" );

        var arithmetic = await provider.SearchAsync(
            QueryParser.Parse( "= 2 + 2" ),
            new SearchRequest { Query = "= 2 + 2", Limit = 5 },
            default );
        Equal( "4", arithmetic[0].Title );

        var currency = await provider.SearchAsync(
            QueryParser.Parse( "= 1 USD to EUR" ),
            new SearchRequest { Query = "= 1 USD to EUR", Limit = 5 },
            default );
        True( currency.Count <= 1, "currency stays a single calculator result" );
        if ( currency.Count == 1 )
        {
            True( currency[0].Title.Length > 0, "cached-rate currency conversion returned a value" );
        }
    }

    private static void CalculatorConfigurationRejectsUnknownQalcModes( )
    {
        ConfigurationValidator.Validate( new CommandDeckOptions( ) );
        True( new CalculatorOptions { UseQalc = CalculatorOptions.UseQalcAlways }.RequiresQalc( ) );
        True( !new CalculatorOptions( ).RequiresQalc( ) );
        Throws<InvalidOperationException>( ( ) => ConfigurationValidator.Validate( new CommandDeckOptions
        {
            Search = new SearchOptions
            {
                Calculator = new CalculatorOptions { UseQalc = "sometimes" }
            }
        } ) );
    }
}
