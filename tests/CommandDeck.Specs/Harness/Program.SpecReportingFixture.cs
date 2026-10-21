namespace CommandDeck.Specs;

public static partial class Program
{
    private sealed class SpecSkippedException( string reason ) : Exception( reason );

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void SkipSpec( string reason ) => throw new SpecSkippedException( reason );

    private static async Task RunSpecStorageFixtureAsync( )
    {
        var temporary = new DirectoryInfo( Path.GetTempPath( ) );
        Equal( temporary.FullName.TrimEnd( Path.DirectorySeparatorChar ), Environment.GetEnvironmentVariable( "SQLITE_TMPDIR" ) );
        var physical = temporary.ResolveLinkTarget( returnFinalTarget: true )?.FullName ?? temporary.FullName;
        Console.WriteLine( $"SPEC_TEMP path={temporary.FullName} physical={physical}" );
        Run( "synchronous SQLite I/O diagnostic", ( ) =>
            throw new InvalidOperationException( "fixture storage failure", new Microsoft.Data.Sqlite.SqliteException( "disk I/O error", 10 ) ) );
        await RunAsync( "asynchronous SQLite full diagnostic", async ( ) =>
        {
            var file = Path.Combine( temporary.FullName, "fixture.txt" );
            await File.WriteAllTextAsync( file, "synthetic fixture" );
            Equal( "synthetic fixture", await File.ReadAllTextAsync( Path.Combine( physical, "fixture.txt" ) ) );
            throw new Microsoft.Data.Sqlite.SqliteException( "database or disk is full", 13 );
        } );
    }

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
