namespace CommandDeck;

internal readonly record struct CalculationDimension( int Length = 0, int Mass = 0, int Time = 0,
    int Temperature = 0, int Data = 0, int Angle = 0, int Currency = 0 )
{
    public static CalculationDimension operator +( CalculationDimension a, CalculationDimension b ) => new(
        a.Length + b.Length, a.Mass + b.Mass, a.Time + b.Time, a.Temperature + b.Temperature,
        a.Data + b.Data, a.Angle + b.Angle, a.Currency + b.Currency );

    internal CalculationDimension Scale( int power )
    {
        var result = new CalculationDimension( Length * power, Mass * power, Time * power,
            Temperature * power, Data * power, Angle * power, Currency * power );
        if ( new[] { result.Length, result.Mass, result.Time, result.Temperature, result.Data, result.Angle, result.Currency }
            .Any( value => Math.Abs( value ) > 64 ) )
        {
            throw new ArithmeticException( );
        }

        return result;
    }

    internal CalculationDimension Root( int degree )
    {
        if ( new[] { Length, Mass, Time, Temperature, Data, Angle, Currency }.Any( value => value % degree != 0 ) )
        {
            throw new ArithmeticException( );
        }

        return new( Length / degree, Mass / degree, Time / degree, Temperature / degree,
            Data / degree, Angle / degree, Currency / degree );
    }
}

internal sealed record CalculationUnit( string Symbol, CalculationDimension Dimension, decimal Factor,
    string Symbols, string Names, bool Si = false, bool Binary = false, decimal Offset = 0, decimal? PerEuro = null );

internal readonly record struct CalculationUnitPower( CalculationUnit Unit, int Power );
