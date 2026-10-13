using Microsoft.Data.Sqlite;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task OldCatalogTokenCannotInterruptLaterBorrowerAsync( )
    {
        var root = Directory.CreateTempSubdirectory( "commanddeck-pool-token-" ).FullName;
        try
        {
            await using SqliteCatalog catalog = new( new StorageOptions { DatabasePath = Path.Combine( root, "catalog.db" ) } );
            await catalog.InitializeAsync( default );
            object handle;
            await using ( var warm = await catalog.OpenAsync( default ) )
            {
                handle = warm.Handle!;
            }

            using CancellationTokenSource old = new( );
            _ = await catalog.SearchAsync( QueryParser.Parse( "app:" ), 1, old.Token );
            await using var borrower = await catalog.OpenAsync( default );
            True( ReferenceEquals( handle, borrower.Handle ), "the fixture must borrow the exact pooled handle" );
            TaskCompletionSource entered = new( TaskCreationOptions.RunContinuationsAsynchronously );
            using ManualResetEventSlim release = new( );
            borrower.CreateFunction( "spec_pause", ( ) =>
            {
                entered.TrySetResult( );
                if ( !release.Wait( TimeSpan.FromSeconds( 5 ) ) )
                {
                    throw new TimeoutException( "The borrower was not released." );
                }

                return 7L;
            } );
            await using var statement = borrower.CreateCommand( );
            statement.CommandText = "SELECT spec_pause();";
            var executing = Task.Run( ( ) => statement.ExecuteScalar( ) );
            try
            {
                await entered.Task.WaitAsync( TimeSpan.FromSeconds( 5 ) );
                old.Cancel( );
            }
            finally
            {
                release.Set( );
                await executing.WaitAsync( TimeSpan.FromSeconds( 5 ) );
            }

            Equal( 7L, Convert.ToInt64( await executing ) );
        }
        finally
        {
            SqliteConnection.ClearAllPools( );
            Directory.Delete( root, recursive: true );
        }
    }
}
