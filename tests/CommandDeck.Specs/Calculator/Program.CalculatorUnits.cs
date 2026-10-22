namespace CommandDeck.Specs;

public static partial class Program
{
    private static (string Expression, string Text)[] CalculatorUnitCases( ) =>
    [
        ("5 in", "5 in"), ("12 in to cm", "30.48 cm"), ("5 ft in in", "60 in"),
        ("100 km in miles", "62.1371192237334 mi"), ("100km→mi", "62.1371192237334 mi"),
        ("100 km -> mi", "62.1371192237334 mi"), ("100 km as mi", "62.1371192237334 mi"),
        ("5 km in (m)", "5000 m"), ("2 in²", "2 in²"),
        ("1 ft to (in)", "12 in"), ("1 m² to in²", "1550.0031000062 in²"),
        ("1 m/s to in/s", "39.3700787401575 in/s"),
        ("1 m/(s*kg) to m/(s*kg)", "1 m/(s·kg)"), ("1 kWh to (kW*h)", "1 kW·h"),
        ("1 Hz to s^-1", "1/s"), ("1 m² to (cm^2)", "10000 cm²"),
        ("3 km", "3 km"), ("km/h", "1 km/h"), ("60 mph to kilometers/hour", "96.56064 km/h"),
        ("100 km / 50 km/h", "2 h"), ("1 GB / 10 MB/s", "100 s"), ("100 km / 50 km/h to min", "120 min"),
        ("1 GB / 10 MB/s to s", "100 s"), ("50km/h", "50 km/h"), ("10km/2h", "5 km/h"), ("3 m/s/s", "3 m/s²"),
        ("9.81 m/s² to ft/s^2", "32.1850393700787 ft/s²"), ("10 in/s to cm/s", "25.4 cm/s"),
        ("1 kWh/day to W", "41.6666666666667 W"), ("2 km/kph", "2 km/kph"),
        ("1/2 h", "0.5 h"), ("3/4 cup", "0.75 cup"), ("1/2 cup to ml", "118.29411825 mL"),
        ("3/4 cup to tbsp", "12 tbsp"), ("1/4 tsp to ml", "1.2322303984375 mL"), ("1/2 g", "0.5 g"),
        ("2 h + 30 min", "2.5 h"), ("90 min to h", "1.5 h"), ("1.5 h to min", "90 min"),
        ("3 days in hours", "72 h"), ("1 year to days", "365.25 d"), ("12 months to year", "1 yr"),
        ("10 mins to hours", "0.166666666666667 h"), ("90 secs to min", "1.5 min"),
        ("2 hrs to min", "120 min"), ("2 yrs to d", "730.5 d"), ("3 wks to d", "21 d"),
        ("10 km / 5 km", "2"), ("2 km * 3 m", "0.006 km²"), ("3 km + 500 m", "3.5 km"),
        ("10000000000000000000000000000 km to km", "10000000000000000000000000000 km"),
        ("10000000000000000000000000000 km * 1", "10000000000000000000000000000 km"),
        ("100000000000000000000 eV", "100000000000000000000 eV"),
        ("1/s", "1/s"), ("5/s", "5/s"), ("1/(m*s)", "1/(m·s)"),
        ("1 kph²", "1 kph²"), ("1 / 1 kph", "1/kph"),
        ("500 m - 1 km", "-500 m"), ("2 m * 3 s", "6 m·s"), ("2 m / (3 s * 4 kg)", "0.166666666666667 m/(s·kg)"),
        ("1 m^2 to cm²", "10000 cm²"), ("1 km2 to ha", "100 ha"), ("1 cm3 to cc", "1 cc"),
        ("1 ft² to sqft", "1 sqft"), ("1 ac to sqft", "43560 sqft"), ("1 ha to m2", "10000 m²"),
        ("1 L to m³", "0.001 m³"), ("1 gal to L", "3.785411784 L"), ("1 qt to pt", "2 pt"),
        ("1 cup to mL", "236.5882365 mL"), ("1 floz to mL", "29.5735295625 mL"),
        ("1 tbsp to tsp", "3 tsp"), ("1 cL + 1 dL", "11 cL"),
        ("g", "9.80665 m/s²"), ("pi g", "30.8084995963263 m/s²"),
        ("2 * g", "19.6133 m/s²"), ("5 g to oz", "0.176369809747902 oz"),
        ("2g", "2 g"), ("1 g/cm^3", "1 g/cm³"), ("1000 mg to g", "1 g"), ("1 g + 500 mg", "1.5 g"),
        ("(2)g", "2 g"), ("2 m g", "2 m·gram"), ("c", "299792458 m/s"),
        ("2 m² g", "2 m²·gram"), ("2 m2 g", "2 m²·gram"),
        ("105F in C", "40.5555555555556 °C"), ("105f to c", "40.5555555555556 °C"),
        ("0C in F", "32 °F"), ("300K in C", "26.85 °C"), ("-40°F to °C", "-40 °C"),
        ("98.6 F to C", "37 °C"), ("-40 °C to °F", "-40 °F"), ("273.15 K to celsius", "0 °C"),
        ("10 K + 5 K", "15 K"), ("0 °C to K", "273.15 K"),
        ("1 lb to g", "453.59237 g"), ("1 pounds to oz", "16 oz"), ("1 st to lbs", "14 lb"),
        ("1 ton to kg", "907.18474 kg"), ("1 ct to mg", "200 mg"), ("1 gr to mg", "64.79891 mg"),
        ("1 t to kg", "1000 kg"), ("1 tonne to grams", "1000000 g"),
        ("1 nmi to m", "1852 m"), ("1 nm to m", "1e-9 m"), ("1 yd to ft", "3 ft"),
        ("1 au to m", "149597870700 m"), ("1 ly to m", "9460730472580800 m"),
        ("1 pc to m", "3.08567758149137e16 m"), ("1 Å to nm", "0.1 nm"),
        ("1 pc / m - 30856775814900000", "13672.7891393796"),
        ("1 psi / Pa - 6894.75729316", "8.36133672267345e-9"),
        ("1 m to ly", "1.05700083402462e-16 ly"),
        // Exact arithmetic exceeds the 3.240779289444365e-17 midpoint; the double fallback lies just below it.
        ("1 m to pc", "3.24077928944436e-17 pc"),
        ("1 m/s to ly/s", "1.05700083402462e-16 ly/s"),
        ("1 sec to ms", "1000 ms"), ("1 µs to ns", "1000 ns"), ("1 μs to ns", "1000 ns"),
        ("1 us to ns", "1000 ns"), ("1 wk to d", "7 d"), ("1 hr to min", "60 min"),
        ("1 kn to km/h", "1.852 km/h"), ("1 kt to kn", "1 kn"), ("1 kph to kmh", "1 km/h"),
        ("1 B to b", "8 b"), ("1 MB to Mb", "8 Mb"), ("1 KB to kB", "1 kB"),
        ("1 GiB to MiB", "1024 MiB"), ("1 kilobyte to bit", "8000 b"),
        ("1 megabyte to byte", "1000000 B"), ("1 gigabyte to byte", "1000000000 B"),
        ("1 terabyte to byte", "1000000000000 B"), ("1 kibibyte to bit", "8192 b"),
        ("1 mebibyte to byte", "1048576 B"), ("1 gibibyte to byte", "1073741824 B"),
        ("1 tebibyte to byte", "1099511627776 B"), ("1 kbit to b", "1000 b"),
        ("1 Mbit to b", "1000000 b"), ("1 Gibit to Mibit", "1024 Mib"),
        ("1 kbps to b/s", "1000 b/s"), ("1 Mbps to kbps", "1000 kb/s"), ("1 Gbps to MB/s", "125 MB/s"),
        ("1 cal to J", "4.184 J"), ("1 Cal to cal", "1000 cal"), ("1 kcal to J", "4184 J"),
        ("1 kWh to J", "3600000 J"), ("1 MWh to kWh", "1000 kWh"), ("1 eV to J", "1.602176634e-19 J"),
        ("1 J to eV", "6.24150907446076e18 eV"), ("1 J/s to eV/s", "6.24150907446076e18 eV/s"),
        ("1 btu to J", "1055.05585262 J"), ("1 hp to W", "745.69987158227 W"),
        ("1 kW to W", "1000 W"), ("1 kJ to J", "1000 J"), ("1 mbar to hPa", "1 hPa"),
        ("1 atm to Pa", "101325 Pa"), ("1 MPa to kPa", "1000 kPa"), ("1 torr to Pa", "133.322368421053 Pa"),
        ("1 mmHg to Pa", "133.322387415 Pa"), ("1 inHg to Pa", "3386.388640341 Pa"),
        ("1 psi to Pa", "6894.75729316836 Pa"), ("1 turn to degree", "360°"), ("1 rev to rad", "6.28318530717959 rad"),
        ("1 MHz to kHz", "1000 kHz"), ("1 GHz to Hz", "1000000000 Hz"), ("60 rpm to Hz", "1 Hz"),
        ("30 mpg to L/100km", "7.84048611111111 L/100km"), ("235.214583333333 L/100km to mpg", "1 mpg"),
        ("1 METER to CENTIMETERS", "100 cm"), ("1 gramme to milligrammes", "1000 mg"),
        ("1 LITER to millilitres", "1000 mL"),
        ("1 millibar to Pa", "100 Pa"), ("1 BAR to Pa", "100000 Pa"), ("1 TON to kg", "907.18474 kg"),
        ("1 BARS to Pa", "100000 Pa"), ("1 TONS to kg", "907.18474 kg"),
        ("1 NAUTICALMILE to m", "1852 m"), ("1 NAUTICALMILES to m", "1852 m"),
        ("1 ASTRONOMICALUNIT to m", "149597870700 m"), ("1 ASTRONOMICALUNITS to m", "149597870700 m"),
        ("1 SQUAREFOOT to m²", "0.09290304 m²"), ("1 SQUAREFEET to m²", "0.09290304 m²"),
        ("1 CUBICCENTIMETER to m³", "0.000001 m³"), ("1 CUBICCENTIMETERS to m³", "0.000001 m³"),
        ("1 CUBICCENTIMETRE to m³", "0.000001 m³"), ("1 CUBICCENTIMETRES to m³", "0.000001 m³"),
        ("1 FLUIDOUNCE to mL", "29.5735295625 mL"), ("1 FLUIDOUNCES to mL", "29.5735295625 mL"),
        ("1 KILOMETERPERHOUR to km/h", "1 km/h"), ("1 KILOMETERSPERHOUR to km/h", "1 km/h"),
        ("1 KILOMETREPERHOUR to km/h", "1 km/h"), ("1 KILOMETRESPERHOUR to km/h", "1 km/h"),
        ("1 MILEPERHOUR to mph", "1 mph"), ("1 MILESPERHOUR to mph", "1 mph"),
        ("1 DEGREECELSIUS to K", "274.15 K"), ("1 DEGREESCELSIUS to K", "274.15 K"),
        ("1 DEGREEFAHRENHEIT to °C", "-17.2222222222222 °C"), ("1 DEGREESFAHRENHEIT to °C", "-17.2222222222222 °C"),
        ("1 KILOBYTE to b", "8000 b"), ("1 KILOBYTES to b", "8000 b"),
        ("1 KILOBITPERSECOND to b/s", "1000 b/s"), ("1 KILOBITSPERSECOND to b/s", "1000 b/s"),
        ("1 MEGABITPERSECOND to b/s", "1000000 b/s"), ("1 MEGABITSPERSECOND to b/s", "1000000 b/s"),
        ("1 GIGABITPERSECOND to b/s", "1000000000 b/s"), ("1 GIGABITSPERSECOND to b/s", "1000000000 b/s"),
        ("1 KILOCALORIE to J", "4184 J"), ("1 KILOCALORIES to J", "4184 J"),
        ("1 WATTHOUR to J", "3600 J"), ("1 WATTHOURS to J", "3600 J"),
        ("1 ELECTRONVOLT to J", "1.602176634e-19 J"), ("1 ELECTRONVOLTS to J", "1.602176634e-19 J"),
        ("1 BRITISHTHERMALUNIT to J", "1055.05585262 J"), ("1 BRITISHTHERMALUNITS to J", "1055.05585262 J"),
        ("1 HORSEPOWERS to W", "745.69987158227 W"),
        ("1 ATMOSPHERE to Pa", "101325 Pa"), ("1 ATMOSPHERES to Pa", "101325 Pa"),
        ("1 POUNDPERSQUAREINCH to Pa", "6894.75729316836 Pa"), ("1 POUNDSPERSQUAREINCH to Pa", "6894.75729316836 Pa"),
        ("1 MILLIMETEROFMERCURY to Pa", "133.322387415 Pa"), ("1 MILLIMETERSOFMERCURY to Pa", "133.322387415 Pa"),
        ("1 MILLIMETREOFMERCURY to Pa", "133.322387415 Pa"), ("1 MILLIMETRESOFMERCURY to Pa", "133.322387415 Pa"),
        ("1 TORR to Pa", "133.322368421053 Pa"), ("1 TORRS to Pa", "133.322368421053 Pa"),
        ("1 INCHOFMERCURY to Pa", "3386.388640341 Pa"), ("1 INCHESOFMERCURY to Pa", "3386.388640341 Pa"),
        ("1 REVOLUTIONPERMINUTE to Hz", "0.0166666666666667 Hz"), ("1 REVOLUTIONSPERMINUTE to Hz", "0.0166666666666667 Hz"),
        ("1 MILEPERGALLON to mpg", "1 mpg"), ("1 MILESPERGALLON to mpg", "1 mpg"),
        ("1 LITERPER100KILOMETERS to L/100km", "1 L/100km"), ("1 LITERSPER100KILOMETERS to L/100km", "1 L/100km"),
        ("1 LITREPER100KILOMETRES to L/100km", "1 L/100km"), ("1 LITRESPER100KILOMETRES to L/100km", "1 L/100km")
    ];

    private static void CalculatorUnitTextRoundTrips( )
    {
        (string Expression, string Text)[] cases =
        [
            ("2 m g", "2 m·gram"), ("2 m / (1 g)", "2 m/gram"), ("1 / (2 in)", "0.5/inch"),
            ("5 g", "5 g"), ("12 in", "12 in"), ("1 g/cm^3", "1 g/cm³"),
            ("2 s * 3 inch", "6 s·inch"), ("1 / (2 g)", "0.5/gram"),
            ("100 km / 2 h", "50 km/h"), ("3 m * 4 m", "12 m²"),
            ("1 m^32 * m^32", "1 m^64"), ("1 m^32 / m^-32", "1 m^64"),
            ("1 m^-32 / m^32", "1/m^64"),
            ("1 m^63 * (m/kph)", "1 m^64/kph"), ("1 m^63 / (kph/m)", "1 m^64/kph"),
            ("(1 m^32/kph^16)^2", "1 m^64/kph^32"),
            ("(1 m^-32*kph^16)^2", "1 kph^32/m^64"),
            ("1 kph²", "1 kph²"), ("1 / 1 kph", "1/kph"),
            ("1 kbps²", "1 kbps²"), ("1 / (2 Mbps)", "0.5/Mbps"),
            ("2 m / (3 s * 4 kg)", "0.166666666666667 m/(s·kg)")
        ];

        foreach ( var (expression, text) in cases )
        {
            var result = CalculatorEngine.Evaluate( expression );

            Equal( CalculationStatus.Success, result.Status );
            Equal( text, result.Text );

            var copied = CalculatorEngine.Evaluate( result.Text! );

            Equal( CalculationStatus.Success, copied.Status );
            Equal( text, copied.Text );
        }
    }
}
