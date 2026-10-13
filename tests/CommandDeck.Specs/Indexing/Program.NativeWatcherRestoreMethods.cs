using System.Collections.Concurrent;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task WatcherReplacementReconcilesOtherRootsAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        try
        {
            var original = Directory.CreateDirectory( Path.Combine( sandbox, "original" ) ).FullName;
            var replacement = Directory.CreateDirectory( Path.Combine( sandbox, "replacement" ) ).FullName;
            var first = Path.Combine( sandbox, "first" );
            Directory.CreateSymbolicLink( first, original );
            var second = Directory.CreateDirectory( Path.Combine( sandbox, "second" ) ).FullName;
            var options = CreateIncrementalIndexOptions( Path.Combine( sandbox, "catalog.db" ), first, second );
            ConcurrentQueue<string> reconciled = new( );
            ConcurrentQueue<string> applied = new( );
            RestoreInotifySystem system = new( );
            await using IncrementalIndexEngine engine = new( options,
                new IndexExclusionPolicy( options.Indexing, options.Privacy, options.Storage.DatabasePath ),
                ( paths, _ ) =>
                {
                    foreach ( var request in paths )
                    {
                        applied.Enqueue( request.Path );
                    }
                    return ValueTask.FromResult( true );
                },
                ( roots, _ ) =>
                {
                    foreach ( var root in roots )
                    {
                        reconciled.Enqueue( root );
                    }
                    return ValueTask.CompletedTask;
                }, inotify: system );
            await engine.StartAsync( default );
            True( await WaitUntilAsync( ( ) => Task.FromResult( !engine.GetStatus( ).ReconciliationRequired ), TimeSpan.FromSeconds( 5 ) ) );
            while ( reconciled.TryDequeue( out _ ) )
            {
            }

            Directory.Delete( first );
            Directory.CreateSymbolicLink( first, replacement );
            system.BeforeCreate = ( ) => File.WriteAllText( Path.Combine( second, "missed.txt" ), "during replacement" );
            engine.RestoreWatchers( engine.GetStatus( ).Roots.Single( root => root.Path == first ).Id );
            True( await WaitUntilAsync( ( ) => Task.FromResult( reconciled.Contains( second ) || applied.Contains( Path.Combine( second, "missed.txt" ) ) ), TimeSpan.FromSeconds( 5 ) ) );
            True( File.Exists( Path.Combine( second, "missed.txt" ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task RetargetedRootResumeRemovesOldResultsAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        try
        {
            var first = Directory.CreateDirectory( Path.Combine( sandbox, "first" ) ).FullName;
            var second = Directory.CreateDirectory( Path.Combine( sandbox, "second" ) ).FullName;
            var link = Path.Combine( sandbox, "root" );
            await File.WriteAllTextAsync( Path.Combine( first, "old.txt" ), "oldtargetmarker" );
            await File.WriteAllTextAsync( Path.Combine( second, "new.txt" ), "newtargetmarker" );
            Directory.CreateSymbolicLink( link, first );
            var options = CreateIncrementalIndexOptions( Path.Combine( sandbox, "catalog.db" ), link );
            await using SqliteCatalog catalog = new( options.Storage );
            await using IndexCoordinator indexer = new( catalog, catalog, options );
            await indexer.ReindexAsync( null, default );
            Equal( 1, (await SearchAsync( catalog, "oldtargetmarker" )).Count );
            var id = (await indexer.GetIncrementalStatusAsync( default )).Roots.Single( ).Id;
            await indexer.PauseAsync( id, default );
            Directory.Delete( link );
            Directory.CreateSymbolicLink( link, second );
            await indexer.ResumeAsync( id, default );
            Equal( 0, (await SearchAsync( catalog, "oldtargetmarker" )).Count );
            Equal( 1, (await SearchAsync( catalog, "newtargetmarker" )).Count );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private static async Task RetargetedRootDropsObsoleteExternalControlsAsync( )
    {
        var sandbox = CreateIncrementalSpecRoot( );
        try
        {
            var first = Directory.CreateDirectory( Path.Combine( sandbox, "first" ) ).FullName;
            var second = Directory.CreateDirectory( Path.Combine( sandbox, "second" ) ).FullName;
            foreach ( var root in new[] { first, second } )
            {
                Directory.CreateDirectory( Path.Combine( root, ".git", "info" ) );
                Directory.CreateDirectory( Path.Combine( root, "notes" ) );
                await File.WriteAllTextAsync( Path.Combine( root, ".gitignore" ), "ignored.txt\n" );
                await File.WriteAllTextAsync( Path.Combine( root, "notes", "plain.txt" ), "text" );
            }
            var link = Path.Combine( sandbox, "root" );
            Directory.CreateSymbolicLink( link, Path.Combine( first, "notes" ) );
            var options = CreateIncrementalIndexOptions( Path.Combine( sandbox, "catalog.db" ), link );
            using FileIndexSource source = new( options.Indexing, options.Privacy );
            await source.IndexPathAsync( Path.Combine( link, "plain.txt" ), default );
            True( source.Exclusions.ExternalControlDirectories( ).Any( path => path.StartsWith( first, StringComparison.Ordinal ) ) );
            Directory.Delete( link );
            Directory.CreateSymbolicLink( link, Path.Combine( second, "notes" ) );
            await source.IndexPathAsync( Path.Combine( link, "plain.txt" ), default );
            var current = source.Exclusions.ExternalControlDirectories( );
            True( current.Any( path => path.StartsWith( second, StringComparison.Ordinal ) ) );
            True( !current.Any( path => path.StartsWith( first, StringComparison.Ordinal ) ) );
        }
        finally
        {
            DeleteIncrementalSpecRoot( sandbox );
        }
    }

    private sealed class RestoreInotifySystem : IInotifySystem
    {
        public Action? BeforeCreate { get; set; }
        public Action<string>? AfterAdd { get; set; }
        public Func<byte[], byte[]?>? ReplaceEvents { get; set; }

        public int CreateInstance( )
        {
            var callback = BeforeCreate;
            BeforeCreate = null;
            callback?.Invoke( );
            return LinuxInotifySystem.Instance.CreateInstance( );
        }

        public int AddWatch( int instance, string path, uint mask )
        {
            var watch = LinuxInotifySystem.Instance.AddWatch( instance, path, mask );
            if ( watch >= 0 )
            {
                AfterAdd?.Invoke( path );
            }

            return watch;
        }
        public void RemoveWatch( int instance, int watch ) => LinuxInotifySystem.Instance.RemoveWatch( instance, watch );
        public int CreateWake( ) => LinuxInotifySystem.Instance.CreateWake( );
        public void Wake( int wake ) => LinuxInotifySystem.Instance.Wake( wake );
        public int Poll( ReadOnlySpan<int> descriptors, Span<bool> readable ) => LinuxInotifySystem.Instance.Poll( descriptors, readable );
        public int Read( int descriptor, Span<byte> buffer )
        {
            var count = LinuxInotifySystem.Instance.Read( descriptor, buffer );
            if ( count > 0 && ReplaceEvents?.Invoke( buffer[..count].ToArray( ) ) is { } replacement )
            {
                replacement.CopyTo( buffer );
                return replacement.Length;
            }

            return count;
        }
        public void Close( int descriptor ) => LinuxInotifySystem.Instance.Close( descriptor );
    }
}
