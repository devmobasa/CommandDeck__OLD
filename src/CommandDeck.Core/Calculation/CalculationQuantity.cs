namespace CommandDeck;

internal sealed record CalculationQuantity( CalculationNumber Value, CalculationUnitPower[] Units )
{
    internal bool BarePercent { get; init; }
    internal bool UnitOnly { get; init; }
    internal CalculationDimension Dimension => Units.Aggregate( new CalculationDimension( ),
        ( dimension, term ) => dimension + term.Unit.Dimension.Scale( term.Power ) );
    internal bool Offset => Units.Any( term => term.Unit.Offset != 0 );
    internal CalculationNumber Factor => Units.Aggregate( new CalculationNumber( 1m ),
        ( factor, term ) => CalculationNumber.Binary( factor, UnitFactor( term.Unit ).Power( new( ( decimal ) term.Power ) ), '*' ) );
    internal CalculationNumber BaseValue => CalculationNumber.Binary(
        Offset ? CalculationNumber.Binary( Value, new( Units[0].Unit.Offset ), '+' ) : Value, Factor, '*' );

    internal static CalculationQuantity Scalar( decimal number ) => new( new( number ), [] );
    internal static CalculationQuantity OfUnit( CalculationUnit unit ) => new( new( 1m ), [new( unit, 1 )] ) { UnitOnly = true };

    private static CalculationNumber UnitFactor( CalculationUnit unit ) => unit.PerEuro is { } rate
        ? CalculationNumber.Binary( new( 1m ), new( rate ), '/' ) : new( unit.Factor );

    internal CalculationQuantity ConvertTo( CalculationQuantity target )
    {
        if ( Dimension != target.Dimension )
        {
            throw new ArithmeticException( );
        }

        if ( Units.SequenceEqual( target.Units ) )
        {
            return target with { Value = Value, UnitOnly = false };
        }

        if ( Units is [{ Unit.PerEuro: { } sourceRate, Power: 1 }] &&
            target.Units is [{ Unit.PerEuro: { } targetRate, Power: 1 }] )
        {
            var amount = CalculationNumber.Binary( CalculationNumber.Binary( Value, new( targetRate ), '*' ), new( sourceRate ), '/' );

            return target with { Value = amount, UnitOnly = false };
        }

        var value = CalculationNumber.Binary( BaseValue, target.Factor, '/' );
        if ( target.Offset )
        {
            value = CalculationNumber.Binary( value, new( target.Units[0].Unit.Offset ), '-' );
        }

        return target with { Value = value, UnitOnly = false };
    }

    internal CalculationQuantity Add( CalculationQuantity right, bool subtract )
    {
        RejectOffset( );
        right.RejectOffset( );

        if ( right.BarePercent && right.Dimension == default )
        {
            right = this with { Value = CalculationNumber.Binary( Value, right.Value, '*' ) };
        }

        var converted = right.ConvertTo( this );

        return this with
        {
            Value = CalculationNumber.Binary( Value, converted.Value, subtract ? '-' : '+' ),
            BarePercent = false,
            UnitOnly = false
        };
    }

    internal CalculationQuantity Multiply( CalculationQuantity right, bool divide, bool implicitProduct = false )
    {
        // Offset scales can only attach to a scalar, never participate in arithmetic.
        if ( implicitProduct && !divide && Dimension == default && right.Offset && right.UnitOnly )
        {
            return right with { Value = CalculationNumber.Binary( Value, right.Value, '*' ), UnitOnly = false };
        }

        RejectOffset( );
        right.RejectOffset( );

        var terms = Units.ToList( );
        var rescale = false;

        foreach ( var term in right.Units )
        {
            var power = divide ? -term.Power : term.Power;
            var existing = terms.FindIndex( item => item.Unit.Dimension == term.Unit.Dimension );
            if ( existing >= 0 )
            {
                rescale |= terms[existing].Unit != term.Unit;
                terms[existing] = terms[existing] with { Power = terms[existing].Power + power };
            }
            else
            {
                terms.Add( term with { Power = power } );
            }
        }

        terms.RemoveAll( term => term.Power == 0 );

        var dimension = (Dimension + right.Dimension.Scale( divide ? -1 : 1 )).Scale( 1 );
        var result = new CalculationQuantity( new( 1m ), dimension == default ? [] : terms.ToArray( ) );
        if ( result.Units.Any( term => Math.Abs( term.Power ) > 64 ) )
        {
            throw new ArithmeticException( );
        }

        var combined = CalculationNumber.Binary( Value, right.Value, divide ? '/' : '*' );
        if ( rescale || dimension == default && terms.Count > 0 )
        {
            combined = CalculationNumber.Binary( combined, Factor, '*' );
            combined = CalculationNumber.Binary( combined, right.Factor, divide ? '/' : '*' );
            combined = CalculationNumber.Binary( combined, result.Factor, '/' );
        }

        return result with { Value = combined, UnitOnly = UnitOnly && right.UnitOnly };
    }

    internal CalculationQuantity Power( CalculationQuantity exponent )
    {
        RejectOffset( );
        exponent.RequireScalar( );
        if ( Dimension != default && (!exponent.Value.IsInteger || exponent.Value.CompareTo( new( -64m ) ) < 0 ||
            exponent.Value.CompareTo( new( 64m ) ) > 0) )
        {
            throw new ArithmeticException( );
        }

        if ( Dimension == default )
        {
            return new( Value.Power( exponent.Value ), [] );
        }

        var power = ( int ) exponent.Value.Double;

        _ = Dimension.Scale( power );
        if ( Units.Any( term => Math.Abs( term.Power * power ) > 64 ) )
        {
            throw new ArithmeticException( );
        }

        return new( Value.Power( exponent.Value ), Units.Select( term => term with { Power = term.Power * power } )
            .Where( term => term.Power != 0 ).ToArray( ) )
        { UnitOnly = UnitOnly };
    }

    internal CalculationQuantity Root( int degree )
    {
        RejectOffset( );

        var dimension = Dimension.Root( degree );
        var terms = Units.All( term => term.Power % degree == 0 )
            ? Units.Select( term => term with { Power = term.Power / degree } ).Where( term => term.Power != 0 ).ToArray( )
            : BaseUnits( dimension );
        var result = new CalculationQuantity( new( 1m ), terms );

        var value = BaseValue;
        if ( degree == 2 && value.CompareTo( new( 0m ) ) < 0 )
        {
            throw new ArithmeticException( );
        }

        var root = degree == 2 ? Math.Sqrt( value.Double ) : Math.Cbrt( value.Double );

        return result with { Value = CalculationNumber.Binary( new( root ), result.Factor, '/' ) };
    }

    private CalculationUnitPower[] BaseUnits( CalculationDimension dimension )
    {
        (string Symbol, int Power)[] terms = [("m", dimension.Length), ("kg", dimension.Mass),
            ("s", dimension.Time), ("K", dimension.Temperature), ("b", dimension.Data), ("rad", dimension.Angle)];
        var result = terms.Where( term => term.Power != 0 )
            .Select( term => new CalculationUnitPower( CalculationUnits.Find( term.Symbol )!, term.Power ) ).ToList( );
        if ( dimension.Currency != 0 )
        {
            result.Add( new( Units.First( term => term.Unit.Dimension.Currency != 0 ).Unit, dimension.Currency ) );
        }

        return result.ToArray( );
    }

    internal void RequireScalar( )
    {
        if ( Dimension != default )
        {
            throw new ArithmeticException( );
        }
    }

    internal void RejectOffset( )
    {
        if ( Offset )
        {
            throw new ArithmeticException( );
        }
    }
}
