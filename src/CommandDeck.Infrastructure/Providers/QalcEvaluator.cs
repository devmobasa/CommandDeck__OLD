using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CommandDeck;

/// <summary>
/// Optional <c>qalc</c> backend for unit, currency, constant, and function
/// expressions. The expression is a single argv token after <c>--</c>; there is
/// no shell. Currency rate fetches run in the background with a 15-minute
/// success cooldown so search stays on cached rates.
/// </summary>
public sealed partial class QalcEvaluator
{
    public static readonly TimeSpan ExchangeRateCooldown = TimeSpan.FromMinutes( 15 );

    private const int EvaluateTimeoutMilliseconds = 250;
    private const int RefreshTimeoutMilliseconds = 3000;
    private const int MaximumExpressionLength = 512;
    private const int MaximumResultLength = 256;
    private const int MaximumStandardOutputBytes = 8 * 1024;
    private const int MaximumStandardErrorBytes = 4 * 1024;
    private const long MaximumMemoryBytes = 128L * 1024 * 1024;
    private const string RateRefreshExpression = "1 USD";

    private static readonly TimeSpan EvaluateTimeout =
        TimeSpan.FromMilliseconds( EvaluateTimeoutMilliseconds );
    private static readonly TimeSpan RefreshTimeout =
        TimeSpan.FromMilliseconds( RefreshTimeoutMilliseconds );

    private static readonly HashSet<string> CurrencyTokens = new( StringComparer.OrdinalIgnoreCase )
    {
        "aed", "aud", "bam", "brl", "btc", "cad", "chf", "cny", "czk", "dkk", "eth",
        "eur", "gbp", "hkd", "huf", "ils", "inr", "jpy", "krw", "mxn", "nok",
        "nzd", "pln", "rub", "sek", "sgd", "thb", "try", "twd", "usd", "zar",
        "bitcoin", "bitcoins", "dollar", "dollars", "ethereum", "euro", "euros",
        "pound", "pounds", "rupee", "rupees", "yen", "yuan"
    };

    private static readonly SearchValues<char> CurrencySymbols =
        SearchValues.Create( "$€£¥₹₽₩₪₿" );

    private readonly ExternalProcessRunner _run;
    private readonly Func<bool> _available;
    private readonly TimeProvider _clock;
    private readonly object _cooldownGate = new( );
    private DateTimeOffset _lastSuccessfulRefresh = DateTimeOffset.MinValue;
    private bool _refreshInFlight;

    public QalcEvaluator(
        ExternalProcessRunner? runner = null,
        Func<bool>? available = null,
        TimeProvider? clock = null )
    {
        _run = runner ?? DefaultRunAsync;
        _available = available ?? (static ( ) => ProcessRunner.ExistsOnPath( "qalc" ));
        _clock = clock ?? TimeProvider.System;
    }

    public async ValueTask<string?> TryEvaluateAsync(
        string expression,
        CancellationToken cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        if ( !_available( ) || !IsSafeExpression( expression ) )
        {
            return null;
        }

        if ( LooksLikeCurrency( expression ) && TryBeginRefresh( _clock.GetUtcNow( ) ) )
        {
            _ = RefreshRatesAsync( );
        }

        return await RunOnceAsync( expression, cancellationToken ).ConfigureAwait( false );
    }

    private async ValueTask<string?> RunOnceAsync(
        string expression,
        CancellationToken cancellationToken )
    {
        ProcessResult result;
        try
        {
            result = await _run(
                "qalc",
                BuildEvaluateArguments( expression ),
                EvaluateTimeout,
                cancellationToken ).ConfigureAwait( false );
        }
        catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
        {
            return null;
        }

        return ParseOutput( result, expression );
    }

    private async Task RefreshRatesAsync( )
    {
        var succeeded = false;
        try
        {
            var result = await _run(
                "qalc",
                BuildRefreshArguments( ),
                RefreshTimeout,
                CancellationToken.None ).ConfigureAwait( false );
            succeeded = result.ExitCode == 0 &&
                !result.TimedOut &&
                !result.AnyOutputLimitExceeded &&
                !result.MemoryLimitExceeded;
        }
        catch ( Exception exception ) when (
            exception is IOException or InvalidOperationException or OperationCanceledException )
        {
        }
        finally
        {
            CompleteRefresh( succeeded );
        }
    }

    private bool TryBeginRefresh( DateTimeOffset now )
    {
        lock ( _cooldownGate )
        {
            if ( _refreshInFlight || now - _lastSuccessfulRefresh < ExchangeRateCooldown )
            {
                return false;
            }

            _refreshInFlight = true;
            return true;
        }
    }

    private void CompleteRefresh( bool succeeded )
    {
        lock ( _cooldownGate )
        {
            if ( succeeded )
            {
                _lastSuccessfulRefresh = _clock.GetUtcNow( );
            }

            _refreshInFlight = false;
        }
    }

    private static bool LooksLikeCurrency( string expression )
    {
        if ( expression.AsSpan( ).IndexOfAny( CurrencySymbols ) >= 0 )
        {
            return true;
        }

        var index = 0;
        while ( index < expression.Length )
        {
            if ( !char.IsLetter( expression[index] ) )
            {
                index++;
                continue;
            }

            var start = index;
            while ( index < expression.Length && char.IsLetter( expression[index] ) )
            {
                index++;
            }

            if ( CurrencyTokens.Contains( expression[start..index] ) )
            {
                return true;
            }
        }

        return false;
    }

    internal static string FormatDisplay( string expression, string exact )
    {
        if ( !LooksLikeCurrency( expression ) )
        {
            return exact;
        }

        var match = CurrencyAmountRegex( ).Match( exact );
        if ( !match.Success )
        {
            return exact;
        }

        var number = match.Groups["amount"];
        if ( !decimal.TryParse( number.ValueSpan, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount ) )
        {
            return exact;
        }

        return exact[..number.Index] + amount.ToString( "F2", CultureInfo.InvariantCulture );
    }

    [GeneratedRegex(
        @"\A(?:[+−-]?(?:[$€£¥₹₽₩₪₿]|[A-Z]{3})\s*|(?:[$€£¥₹₽₩₪₿]|[A-Z]{3})\s*[+−-])(?<amount>[0-9]+(?:\.[0-9]*)?|\.[0-9]+)\z",
        RegexOptions.CultureInvariant )]
    private static partial Regex CurrencyAmountRegex( );

    private static IReadOnlyList<string> BuildEvaluateArguments( string expression ) =>
    [
        "-t",
        "-m",
        EvaluateTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture),
        "-s",
        "update_exchange_rates 0",
        "-s", "decimal comma off",
        "-s", "digit grouping 0",
        // qalc's currency defaults discard decimals on large amounts. Keep
        // extra digits for details and round only the displayed title.
        .. LooksLikeCurrency( expression ) ? new[]
        {
            "-s", "precision 30", "-s", "max_decimals 20", "-s", "min_decimals 0"
        } : Array.Empty<string>( ),
        "--",
        expression
    ];

    private static IReadOnlyList<string> BuildRefreshArguments( ) =>
    [
        "-t",
        "-e",
        "-m",
        RefreshTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture),
        "--",
        RateRefreshExpression
    ];

    private static string? ParseOutput( ProcessResult result, string expression )
    {
        if ( result.ExitCode != 0 ||
            result.TimedOut ||
            result.AnyOutputLimitExceeded ||
            result.MemoryLimitExceeded )
        {
            return null;
        }

        var line = FirstLine( result.StandardOutput );
        if ( line.Length is 0 or > MaximumResultLength || HasUnsafeControls( line ) )
        {
            return null;
        }

        if ( IsErrorToken( line ) || IsEchoOf( expression, line ) )
        {
            return null;
        }

        return line;
    }

    private static bool IsSafeExpression( string expression )
    {
        if ( expression.Length is 0 or > MaximumExpressionLength )
        {
            return false;
        }

        foreach ( var character in expression )
        {
            if ( char.IsControl( character ) )
            {
                return false;
            }
        }

        return true;
    }

    private static string FirstLine( string text )
    {
        if ( string.IsNullOrWhiteSpace( text ) )
        {
            return string.Empty;
        }

        var newline = text.IndexOf( '\n' );
        var span = newline < 0 ? text.AsSpan( ) : text.AsSpan( 0, newline );
        return span.Trim( ).ToString( );
    }

    private static bool HasUnsafeControls( string value )
    {
        foreach ( var character in value )
        {
            if ( char.IsControl( character ) )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsErrorToken( string line ) =>
        line.StartsWith( "error", StringComparison.OrdinalIgnoreCase ) ||
        string.Equals( line, "undefined", StringComparison.OrdinalIgnoreCase ) ||
        string.Equals( line, "inf", StringComparison.OrdinalIgnoreCase ) ||
        string.Equals( line, "-inf", StringComparison.OrdinalIgnoreCase ) ||
        string.Equals( line, "nan", StringComparison.OrdinalIgnoreCase );

    private static bool IsEchoOf( string expression, string result ) =>
        string.Equals(
            CollapseWhitespace( expression ),
            CollapseWhitespace( result ),
            StringComparison.OrdinalIgnoreCase );

    private static string CollapseWhitespace( string value )
    {
        StringBuilder builder = new( value.Length );
        foreach ( var character in value )
        {
            if ( !char.IsWhiteSpace( character ) )
            {
                builder.Append( character );
            }
        }

        return builder.ToString( );
    }

    private static async ValueTask<ProcessResult> DefaultRunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken ) =>
        await ProcessRunner.RunBoundedAsync(
            fileName,
            arguments,
            workingDirectory: null,
            timeout,
            new ProcessExecutionLimits
            {
                MaximumStandardOutputBytes = MaximumStandardOutputBytes,
                MaximumStandardErrorBytes = MaximumStandardErrorBytes,
                MaximumMemoryBytes = MaximumMemoryBytes,
                ClearEnvironment = false
            },
            cancellationToken ).ConfigureAwait( false );
}
