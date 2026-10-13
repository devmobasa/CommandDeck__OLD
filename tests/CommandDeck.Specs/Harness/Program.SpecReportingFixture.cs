namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class SpecSkippedException( string reason ) : Exception( reason );

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void SkipSpec( string reason ) => throw new SpecSkippedException( reason );

    private static async Task RunSpecReportingFixtureAsync( )
    {
        Run( "unavailable synchronous prerequisite", ( ) => SkipSpec( "fixture tool is unavailable" ) );
        await RunAsync( "unavailable asynchronous prerequisite", async ( ) =>
        {
            await Task.Yield( );
            SkipSpec( "fixture platform is unavailable" );
        } );
        Run( "available prerequisite", ( ) => Equal( 2, 1 + 1 ) );
    }
}
