using System.Collections.Frozen;

namespace CommandDeck;

internal static class CalculationUnits
{
    private static readonly CalculationDimension Length = new( Length: 1 );
    private static readonly CalculationDimension Mass = new( Mass: 1 );
    private static readonly CalculationDimension Time = new( Time: 1 );
    private static readonly CalculationDimension Volume = new( Length: 3 );
    private static readonly CalculationDimension Speed = new( Length: 1, Time: -1 );
    private static readonly CalculationDimension Energy = new( Length: 2, Mass: 1, Time: -2 );
    private static readonly CalculationDimension Power = new( Length: 2, Mass: 1, Time: -3 );
    private static readonly CalculationDimension Pressure = new( Length: -1, Mass: 1, Time: -2 );
    private static readonly CalculationDimension Angle = new( Angle: 1 );
    private static readonly CalculationDimension Frequency = new( Time: -1 );
    private static readonly CalculationUnit[] Rows =
    [
        new( "m", Length, 1m, "m", "meter|meters|metre|metres", true ),
        new( "in", Length, 0.0254m, "in", "inch|inches" ),
        new( "ft", Length, 0.3048m, "ft", "foot|feet" ),
        new( "yd", Length, 0.9144m, "yd", "yard|yards" ),
        new( "mi", Length, 1609.344m, "mi", "mile|miles" ),
        new( "nmi", Length, 1852m, "nmi", "nauticalmile|nauticalmiles" ),
        new( "au", Length, 149597870700m, "au", "astronomicalunit|astronomicalunits" ),
        new( "ly", Length, 9460730472580800m, "ly", "lightyear|lightyears" ),
        new( "pc", Length, 30856775814913672.789139379578m, "pc", "parsec|parsecs" ),
        new( "Å", Length, 0.0000000001m, "Å", "angstrom|angstroms" ),
        new( "g", Mass, 0.001m, "g", "gram|grams|gramme|grammes", true ),
        new( "t", Mass, 1000m, "t", "tonne|tonnes" ),
        new( "lb", Mass, 0.45359237m, "lb|lbs", "pound|pounds" ),
        new( "oz", Mass, 0.028349523125m, "oz", "ounce|ounces" ),
        new( "st", Mass, 6.35029318m, "st", "stone|stones" ),
        new( "ton", Mass, 907.18474m, "ton", "ton|tons" ),
        new( "ct", Mass, 0.0002m, "ct", "carat|carats" ),
        new( "gr", Mass, 0.00006479891m, "gr", "grain|grains" ),
        new( "s", Time, 1m, "s", "sec|secs|second|seconds", true ),
        new( "min", Time, 60m, "min", "mins|minute|minutes" ),
        new( "h", Time, 3600m, "h|hr", "hrs|hour|hours" ),
        new( "d", Time, 86400m, "d", "day|days" ),
        new( "wk", Time, 604800m, "wk", "wks|week|weeks" ),
        // A Julian year is exactly 365.25 days; months here are one twelfth of it.
        new( "month", Time, 2629800m, "", "month|months" ),
        new( "yr", Time, 31557600m, "yr", "yrs|year|years" ),
        new( "ha", new( Length: 2 ), 10000m, "ha", "hectare|hectares" ),
        new( "ac", new( Length: 2 ), 4046.8564224m, "ac", "acre|acres" ),
        new( "sqft", new( Length: 2 ), 0.09290304m, "sqft", "squarefoot|squarefeet" ),
        new( "L", Volume, 0.001m, "L|l", "liter|liters|litre|litres", true ),
        new( "cc", Volume, 0.000001m, "cc", "cubiccentimeter|cubiccentimeters|cubiccentimetre|cubiccentimetres" ),
        new( "gal", Volume, 0.003785411784m, "gal", "gallon|gallons" ),
        new( "qt", Volume, 0.000946352946m, "qt", "quart|quarts" ),
        new( "pt", Volume, 0.000473176473m, "pt", "pint|pints" ),
        new( "cup", Volume, 0.0002365882365m, "", "cup|cups" ),
        new( "floz", Volume, 0.0000295735295625m, "floz", "fluidounce|fluidounces" ),
        new( "tbsp", Volume, 0.00001478676478125m, "tbsp", "tablespoon|tablespoons" ),
        new( "tsp", Volume, 0.00000492892159375m, "tsp", "teaspoon|teaspoons" ),
        new( "km/h", Speed, 1m / 3.6m, "kph|kmh", "kilometerperhour|kilometersperhour|kilometreperhour|kilometresperhour" ),
        new( "mph", Speed, 0.44704m, "mph", "mileperhour|milesperhour" ),
        new( "kn", Speed, 1852m / 3600m, "kn|kt", "knot|knots" ),
        new( "K", new( Temperature: 1 ), 1m, "K", "kelvin|kelvins" ),
        new( "°C", new( Temperature: 1 ), 1m, "°C|degC|C", "celsius|degreecelsius|degreescelsius", Offset: 273.15m ),
        new( "°F", new( Temperature: 1 ), 5m / 9m, "°F|degF|F", "fahrenheit|degreefahrenheit|degreesfahrenheit", Offset: 459.67m ),
        new( "b", new( Data: 1 ), 1m, "b|bit", "bit|bits", true, true ),
        new( "B", new( Data: 1 ), 8m, "B", "byte|bytes", true, true ),
        new( "kB", new( Data: 1 ), 8000m, "KB", "kilobyte|kilobytes" ),
        new( "kb/s", new( Data: 1, Time: -1 ), 1000m, "kbps", "kilobitpersecond|kilobitspersecond" ),
        new( "Mb/s", new( Data: 1, Time: -1 ), 1000000m, "Mbps", "megabitpersecond|megabitspersecond" ),
        new( "Gb/s", new( Data: 1, Time: -1 ), 1000000000m, "Gbps", "gigabitpersecond|gigabitspersecond" ),
        new( "J", Energy, 1m, "J", "joule|joules", true ),
        new( "cal", Energy, 4.184m, "cal", "calorie|calories" ),
        new( "kcal", Energy, 4184m, "kcal|Cal", "kilocalorie|kilocalories" ),
        new( "Wh", Energy, 3600m, "Wh", "watthour|watthours", true ),
        new( "eV", Energy, 0.0000000000000000001602176634m, "eV", "electronvolt|electronvolts" ),
        new( "BTU", Energy, 1055.05585262m, "BTU|btu", "britishthermalunit|britishthermalunits" ),
        new( "W", Power, 1m, "W", "watt|watts", true ),
        new( "hp", Power, 745.69987158227022m, "hp", "horsepower|horsepowers" ),
        new( "Pa", Pressure, 1m, "Pa", "pascal|pascals", true ),
        new( "bar", Pressure, 100000m, "bar", "bar|bars", true ),
        new( "atm", Pressure, 101325m, "atm", "atmosphere|atmospheres" ),
        new( "psi", Pressure, 0.45359237m * 9.80665m / (0.0254m * 0.0254m), "psi", "poundpersquareinch|poundspersquareinch" ),
        new( "mmHg", Pressure, 133.322387415m, "mmHg", "millimeterofmercury|millimetersofmercury|millimetreofmercury|millimetresofmercury" ),
        new( "torr", Pressure, 101325m / 760m, "torr", "torr|torrs" ),
        new( "inHg", Pressure, 3386.388640341m, "inHg", "inchofmercury|inchesofmercury" ),
        new( "rad", Angle, 1m, "rad", "radian|radians" ),
        new( "°", Angle, 0.0174532925199432957692369077m, "deg|°", "degree|degrees" ),
        new( "turn", Angle, 6.2831853071795864769252867666m, "rev", "turn|turns|revolution|revolutions" ),
        new( "Hz", Frequency, 1m, "Hz", "hertz", true ),
        new( "rpm", Frequency, 1m / 60m, "rpm", "revolutionperminute|revolutionsperminute" ),
        new( "mpg", new( Length: -2 ), 1609.344m / 0.003785411784m, "mpg", "milepergallon|milespergallon" ),
        new( "L/100km", new( Length: 2 ), 0.001m / 100000m, "L/100km", "literper100kilometers|litersper100kilometers|litreper100kilometres|litresper100kilometres" )
    ];

    private static readonly FrozenDictionary<string, CalculationUnit> Symbols = Rows
        .SelectMany( row => row.Symbols.Split( '|', StringSplitOptions.RemoveEmptyEntries ).Select( key => KeyValuePair.Create( key, row ) ) )
        .ToFrozenDictionary( StringComparer.Ordinal );
    private static readonly FrozenDictionary<string, CalculationUnit> Names = Rows
        .SelectMany( row => row.Names.Split( '|', StringSplitOptions.RemoveEmptyEntries ).Select( key => KeyValuePair.Create( key, row ) ) )
        .ToFrozenDictionary( StringComparer.OrdinalIgnoreCase );

    private static readonly (string Symbol, string Name, decimal Factor)[] Prefixes =
    [
        ("k", "kilo", 1000m), ("M", "mega", 1000000m), ("G", "giga", 1000000000m),
        ("T", "tera", 1000000000000m), ("P", "peta", 1000000000000000m), ("E", "exa", 1000000000000000000m),
        ("h", "hecto", 100m), ("da", "deca", 10m), ("d", "deci", 0.1m), ("c", "centi", 0.01m),
        ("m", "milli", 0.001m), ("µ", "micro", 0.000001m), ("μ", "micro", 0.000001m),
        ("u", "micro", 0.000001m), ("n", "nano", 0.000000001m), ("p", "pico", 0.000000000001m),
        ("Ki", "kibi", 1024m), ("Mi", "mebi", 1048576m), ("Gi", "gibi", 1073741824m),
        ("Ti", "tebi", 1099511627776m), ("Pi", "pebi", 1125899906842624m), ("Ei", "exbi", 1152921504606846976m)
    ];

    internal static CalculationUnit? Find( string spelling )
    {
        if ( Symbols.TryGetValue( spelling, out var unit ) || Names.TryGetValue( spelling, out unit ) )
        {
            return unit;
        }

        foreach ( var (symbol, name, factor) in Prefixes )
        {
            var binary = symbol.EndsWith( 'i' );
            if ( spelling.StartsWith( symbol, StringComparison.Ordinal ) &&
                Symbols.TryGetValue( spelling[symbol.Length..], out unit ) && AllowsPrefix( unit, binary, factor ) )
            {
                return unit with { Symbol = symbol.Replace( 'μ', 'µ' ).Replace( 'u', 'µ' ) + unit.Symbol, Factor = factor * unit.Factor };
            }

            if ( spelling.StartsWith( name, StringComparison.OrdinalIgnoreCase ) &&
                Names.TryGetValue( spelling[name.Length..], out unit ) && AllowsPrefix( unit, binary, factor ) )
            {
                return unit with { Symbol = symbol + unit.Symbol, Factor = factor * unit.Factor };
            }
        }

        return null;
    }

    private static bool AllowsPrefix( CalculationUnit unit, bool binary, decimal factor ) =>
        binary ? unit.Binary : unit.Si && (unit.Dimension.Data == 0 || factor >= 1000m);

    internal static CalculationUnit Canonical( string symbol ) => Rows.First( unit => unit.Symbol == symbol );
}
