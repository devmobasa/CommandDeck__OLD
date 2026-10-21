namespace CommandDeck;

internal sealed class CalculationParser( CalculationToken[] tokens, CalculationContext context, bool target = false )
{
    private int _position;
    private int _depth;
    private int _exponentDepth;
    private bool _unitIdentifier;
    internal CalculationFeatures Features { get; private set; }
    private string Current => _position < tokens.Length ? tokens[_position].Lower : "";
    private bool End => _position == tokens.Length;

    internal CalculationQuantity Parse( )
    {
        if ( tokens.Length == 0 )
        {
            throw new FormatException( );
        }

        var result = Add( );
        if ( !End || target && (!result.UnitOnly || result.Value.CompareTo( new( 1m ) ) != 0) )
        {
            throw new FormatException( );
        }

        return result;
    }

    private CalculationQuantity Add( )
    {
        var left = Product( );

        while ( Current is "+" or "-" )
        {
            var subtract = Current == "-";
            _position++;
            Features |= CalculationFeatures.Operator;

            left = left.Add( Product( ), subtract );
        }

        return left;
    }

    private CalculationQuantity Product( )
    {
        var left = Unary( );

        while ( Current is "*" or "/" or "mod" or "of" || Current == "%" && StartsOperand( _position + 1 ) )
        {
            var operation = Current;
            _position++;
            Features |= CalculationFeatures.Operator;

            var right = Unary( );
            if ( operation is "mod" or "%" )
            {
                left.RejectOffset( );
                right.RejectOffset( );
                right = right.ConvertTo( left );

                left = left with { Value = CalculationNumber.Binary( left.Value, right.Value, '%' ), BarePercent = false, UnitOnly = false };
            }
            else
            {
                left = left.Multiply( right, operation == "/" );
            }
        }

        return left;
    }

    private CalculationQuantity Unary( )
    {
        CalculationQuantity result;
        if ( Current is "+" or "-" )
        {
            Enter( );

            var negate = Current == "-";

            _position++;
            result = Unary( );
            _depth--;
            if ( negate )
            {
                result = result with { Value = result.Value.Negate( ), UnitOnly = false };
            }
        }
        else
        {
            result = Implicit( );
        }

        return result;
    }

    private CalculationQuantity Implicit( )
    {
        var left = Power( );

        while ( StartsOperand( _position ) )
        {
            if ( tokens[_position].Number && CalculationCurrencies.Resolve( tokens[_position - 1].Text ) is null )
            {
                throw new FormatException( );
            }

            left = left.Multiply( Power( juxtaposed: true ), false, implicitProduct: true );
        }

        return left;
    }

    private CalculationQuantity Power( bool juxtaposed = false )
    {
        var left = Postfix( juxtaposed );
        var unitIdentifier = _unitIdentifier;
        if ( Current == "^" )
        {
            _position++;
            Features |= CalculationFeatures.Operator;
            Enter( );

            var negative = Current == "-";
            if ( Current is "-" or "+" )
            {
                _position++;
            }

            _exponentDepth++;
            var exponent = Power( );
            _exponentDepth--;
            _depth--;
            if ( negative )
            {
                exponent = exponent with { Value = exponent.Value.Negate( ) };
            }

            left = left.Power( exponent );
        }

        _unitIdentifier = unitIdentifier;

        return left;
    }

    private CalculationQuantity Postfix( bool juxtaposed )
    {
        var result = Primary( juxtaposed );

        // % before + or - is percentage then a binary operator; signed modulo needs mod or parentheses.
        while ( Current is "!" or "²" or "³" || Current == "%" && !StartsOperand( _position + 1 ) )
        {
            var operation = Current;
            _position++;
            result.RejectOffset( );
            if ( operation is "²" or "³" )
            {
                result = result.Power( CalculationQuantity.Scalar( operation == "²" ? 2 : 3 ) );
            }
            else
            {
                if ( target )
                {
                    throw new FormatException( );
                }

                if ( operation == "!" )
                {
                    result.RequireScalar( );
                }

                result = result with
                {
                    Value = operation == "!" ? result.Value.Factorial( ) : CalculationNumber.Binary( result.Value, new( 100m ), '/' ),
                    BarePercent = operation == "%",
                    UnitOnly = false
                };
            }
        }

        return result;
    }

    private CalculationQuantity Primary( bool juxtaposed )
    {
        if ( End )
        {
            throw new FormatException( );
        }

        var token = tokens[_position++];
        if ( token.Number )
        {
            if ( target && _exponentDepth == 0 )
            {
                throw new FormatException( );
            }

            _unitIdentifier = false;

            return new( CalculationNumber.Parse( token.Text ), [] );
        }

        if ( token.Text == "(" )
        {
            Enter( );

            var inner = Add( );

            Expect( ")" );
            _depth--;
            _unitIdentifier = false;

            return inner with { BarePercent = false };
        }

        if ( CalculationFunctions.IsFunction( token.Lower ) &&
            (Current == "(" || token.Lower is not ("min" or "max" or "gcd" or "lcm")) )
        {
            return Function( token.Lower );
        }

        var afterNumber = _position >= 2 && tokens[_position - 2].Number;
        if ( token.Lower == "in" && !afterNumber && !target )
        {
            throw new FormatException( );
        }

        var gram = juxtaposed && (afterNumber || _unitIdentifier || tokens[_position - 2].Text == ")");
        var value = Identifier( token.Text == "k" && afterNumber ? "K" : token.Text, gram );
        _unitIdentifier = value.UnitOnly;

        return value;
    }

    private CalculationQuantity Function( string name )
    {
        if ( target )
        {
            throw new FormatException( );
        }

        Features |= CalculationFeatures.Function;
        Enter( );

        List<CalculationQuantity> arguments = [];

        if ( Current == "(" )
        {
            _position++;
            arguments.Add( Add( ) );

            while ( Current == "," )
            {
                _position++;
                arguments.Add( Add( ) );
            }

            Expect( ")" );
        }
        else
        {
            arguments.Add( Unary( ) );
        }

        _depth--;
        _unitIdentifier = false;

        return CalculationFunctions.Call( name, arguments );
    }

    private CalculationQuantity Identifier( string spelling, bool gram )
    {
        // Bare g is gravity; after a number, closing parenthesis or unit it is gram.
        if ( spelling == "g" && !target && !gram )
        {
            return CalculationFunctions.Constant( "g" )!;
        }

        var unit = CalculationUnits.Find( spelling );
        if ( unit is not null )
        {
            return CalculationQuantity.OfUnit( unit );
        }

        var currency = context.Currency( spelling );
        if ( currency is not null )
        {
            return CalculationQuantity.OfUnit( currency );
        }

        if ( !target )
        {
            var constant = CalculationFunctions.Constant( spelling == "c" ? spelling : spelling.ToLowerInvariant( ) );
            if ( constant is not null )
            {
                return constant;
            }
        }

        if ( spelling.Length > 1 && spelling[^1] is '2' or '3' )
        {
            unit = CalculationUnits.Find( spelling[..^1] );
            if ( unit is not null )
            {
                return CalculationQuantity.OfUnit( unit ).Power( CalculationQuantity.Scalar( spelling[^1] - '0' ) ) with { UnitOnly = true };
            }
        }

        throw new FormatException( );
    }

    private bool StartsOperand( int position ) => position < tokens.Length &&
        (tokens[position].Number || tokens[position].Text == "(" ||
         !new[] { "+", "-", "*", "/", "^", "%", "!", "²", "³", ")", ",", "mod", "of", "to", "as", "->", "→" }.Contains( tokens[position].Lower ));

    private void Expect( string spelling )
    {
        if ( Current != spelling )
        {
            throw new FormatException( );
        }

        _position++;
    }

    private void Enter( )
    {
        if ( ++_depth > 64 )
        {
            throw new FormatException( );
        }
    }
}

internal sealed class CalculationContext( Func<CurrencyRateSnapshot?>? rates )
{
    internal CurrencyRateSnapshot? Snapshot { get; private set; }

    internal CalculationUnit? Currency( string spelling )
    {
        var code = CalculationCurrencies.Resolve( spelling );
        if ( code is null )
        {
            return null;
        }

        if ( Snapshot is null )
        {
            Snapshot = rates?.Invoke( );
            if ( Snapshot is null )
            {
                throw new CalculationRatesUnavailableException( );
            }
        }

        var rate = 1m;
        if ( code != "EUR" && (!Snapshot.PerEuro.TryGetValue( code, out rate ) || rate <= 0) )
        {
            throw new FormatException( );
        }

        return new( code, new( Currency: 1 ), 1m, code, "", PerEuro: rate );
    }
}

internal sealed class CalculationRatesUnavailableException : Exception;
