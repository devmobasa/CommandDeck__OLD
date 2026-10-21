using System.Globalization;
using System.Text.RegularExpressions;

namespace CommandDeck;

public sealed record DateCount( string Title, string Subtitle, string CopyText );

public static partial class DateCounting
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    public static bool IsCandidate( string text ) => CandidateRegex( ).IsMatch( text );

    public static bool TryEvaluate( string text, TimeProvider clock, out DateCount? result )
    {
        result = null;
        if ( text.Length > 512 || !IsCandidate( text ) )
        {
            return false;
        }

        var match = PhraseRegex( ).Match( text );
        if ( !match.Success )
        {
            return false;
        }

        var today = DateOnly.FromDateTime( clock.GetLocalNow( ).DateTime );
        var mode = match.Groups["mode"].Value.ToLowerInvariant( );
        var first = match.Groups["first"].Value;
        var second = match.Groups["second"].Value;
        DateOnly start;
        DateOnly end;
        if ( mode is "until" or "till" )
        {
            start = today;
            if ( !TryResolve( first, today, today, forward: true, out end ) )
            {
                return false;
            }
        }
        else
        {
            if ( !TryResolve( first, today, today, forward: false, out start ) )
            {
                return false;
            }

            end = today;
            if ( second.Length > 0 && !TryResolve( second, today, start, forward: true, out end ) )
            {
                return false;
            }
        }

        var days = end.DayNumber - start.DayNumber;
        var title = $"{days} {(Math.Abs( days ) == 1 ? "day" : "days")}";
        if ( Math.Abs( days ) >= 7 && match.Groups["unit"].Value.Equals( "weeks", StringComparison.OrdinalIgnoreCase ) )
        {
            var weeks = Math.Abs( days ) / 7;
            var remainder = Math.Abs( days % 7 );
            title = $"{(days < 0 ? "-" : "")}{weeks} {(weeks == 1 ? "week" : "weeks")} {remainder} {(remainder == 1 ? "day" : "days")}";
        }
        if ( days == 0 )
        {
            title = "0 days (today)";
        }

        string Label( DateOnly date ) => date == today ? "Today" : date.ToString( "ddd d MMM yyyy", English );
        result = new DateCount( title, $"{Label( start )} → {Label( end )}", days.ToString( CultureInfo.InvariantCulture ) );
        return true;
    }

    private static bool TryResolve( string text, DateOnly today, DateOnly anchor, bool forward, out DateOnly date )
    {
        date = default;
        switch ( text.ToLowerInvariant( ) )
        {
            case "today":
                date = today;
                return true;
            case "tomorrow" when today < DateOnly.MaxValue:
                date = today.AddDays( 1 );
                return true;
            case "yesterday" when today > DateOnly.MinValue:
                date = today.AddDays( -1 );
                return true;
        }
        if ( DateOnly.TryParseExact( text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date ) )
        {
            return true;
        }

        var parts = text.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
        if ( parts.Length is < 2 or > 3 )
        {
            return false;
        }

        var dayFirst = int.TryParse( parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day );
        if ( !dayFirst && !int.TryParse( parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out day ) )
        {
            return false;
        }

        var month = FindMonth( parts[dayFirst ? 1 : 0] );
        if ( month == 0 || day is < 1 or > 31 )
        {
            return false;
        }

        if ( parts.Length == 3 )
        {
            return int.TryParse( parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var year ) &&
                TryDate( year, month, day, out date );
        }

        for ( var year = anchor.Year; year is >= 1 and <= 9999; year += forward ? 1 : -1 )
        {
            if ( TryDate( year, month, day, out date ) && (forward ? date >= anchor : date <= anchor) )
            {
                return true;
            }
            if ( day > DateTime.DaysInMonth( 2000, month ) )
            {
                return false;
            }
        }
        return false;
    }

    private static int FindMonth( string text )
    {
        for ( var month = 1; month <= 12; month++ )
        {
            if ( text.Equals( English.DateTimeFormat.GetMonthName( month ), StringComparison.OrdinalIgnoreCase ) ||
                text.Equals( English.DateTimeFormat.GetAbbreviatedMonthName( month ), StringComparison.OrdinalIgnoreCase ) ||
                (month == 9 && text.Equals( "sept", StringComparison.OrdinalIgnoreCase )) )
            {
                return month;
            }
        }
        return 0;
    }

    private static bool TryDate( int year, int month, int day, out DateOnly date )
    {
        date = default;
        if ( year is < 1 or > 9999 || day > DateTime.DaysInMonth( year, month ) )
        {
            return false;
        }

        date = new DateOnly( year, month, day );
        return true;
    }

    [GeneratedRegex( @"\A(?:days|weeks)\s+(?:until|till|since|from|between)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant )]
    private static partial Regex CandidateRegex( );

    [GeneratedRegex( @"\A(?<unit>days|weeks)\s+(?:(?<mode>until|till|since)\s+(?<first>.+)|(?<mode>from)\s+(?<first>.+?)(?:\s+to\s+(?<second>.+))?|(?<mode>between)\s+(?<first>.+?)\s+and\s+(?<second>.+))\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant )]
    private static partial Regex PhraseRegex( );
}
