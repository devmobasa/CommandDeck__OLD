using System.Text;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static void ExtensionResourceProfilesKeepLimits( )
    {
        var root = CreateCliSpecRoot( );
        try
        {
            (ExtensionResourceProfile Profile, string Properties)[] cases =
            [
                (ExtensionResourceProfile.Interactive, "MemoryMax=512M|MemorySwapMax=0|TasksMax=64|CPUQuota=100%|RuntimeMaxSec=1800"),
                (ExtensionResourceProfile.Search, "MemoryMax=128M|MemorySwapMax=0|TasksMax=32|CPUQuota=50%|RuntimeMaxSec=5"),
                (ExtensionResourceProfile.PackageSlice, "MemoryMax=512M|MemorySwapMax=0|TasksMax=64|CPUQuota=100%"),
                (ExtensionResourceProfile.GlobalSlice, "MemoryMax=1G|MemorySwapMax=0|TasksMax=256|CPUQuota=200%")
            ];
            foreach ( var (profile, properties) in cases )
            {
                Equal( properties, string.Join( '|', profile.SystemdProperties( ) ) );
                File.WriteAllText( Path.Combine( root, "memory.max" ), profile.MemoryBytes.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
                File.WriteAllText( Path.Combine( root, "pids.max" ), profile.Tasks.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
                File.WriteAllText( Path.Combine( root, "memory.swap.max" ), "0" );
                File.WriteAllText( Path.Combine( root, "cpu.max" ), "50000 100000" );
                profile.Verify( root );
                File.WriteAllText( Path.Combine( root, "cpu.max" ), "max 100000" );
                Throws<PlatformNotSupportedException>( ( ) => profile.Verify( root ) );
                File.WriteAllText( Path.Combine( root, "cpu.max" ), "50000 100000" );
                File.WriteAllText( Path.Combine( root, "memory.max" ), "max" );
                Throws<PlatformNotSupportedException>( ( ) => profile.Verify( root ) );
            }
            Equal( "~@debug @module @raw-io @reboot @swap @obsolete @cpu-emulation", ExtensionSyscallFilter.Deny( false ) );
            Equal( "~@module @raw-io @reboot @swap @obsolete @cpu-emulation", ExtensionSyscallFilter.Deny( true ) );
        }
        finally
        {
            Directory.Delete( root, true );
        }
    }

    private static async Task ExtensionOAuthCallbackRejectsHostileInputAsync( )
    {
        const string redirect = "http://127.0.0.1:12345/callback";
        const string query = "/callback?state=secret-state&iss=https%3A%2F%2Fissuer.example&code=code";
        var valid = "GET " + query + " HTTP/1.1\r\nHost: 127.0.0.1:12345\r\n\r\n";
        await using ( MemoryStream stream = new( Encoding.ASCII.GetBytes( valid ) ) )
        {
            Equal( "code", await OAuthLoopbackReceiver.ReadCodeAsync( stream, redirect, "secret-state", "https://issuer.example", CancellationToken.None ) );
        }
        foreach ( var input in new[]
        {
            valid.Replace( "GET ", "POST ", StringComparison.Ordinal ),
            valid.Replace( "Host: 127.0.0.1:12345\r\n", "", StringComparison.Ordinal ),
            valid.Replace( "Host:", "Host: other\r\nHost:", StringComparison.Ordinal ),
            valid.Replace( "12345", "12346", StringComparison.Ordinal ),
            valid.Replace( "secret-state", "wrong-state", StringComparison.Ordinal ),
            valid.Replace( "issuer.example", "other.example", StringComparison.Ordinal ),
            valid.Replace( "&code=code", "&code=code&code=again", StringComparison.Ordinal ),
            valid.Replace( "&code=code", "&code=code&error=denied", StringComparison.Ordinal ),
            "GET " + query + " HTTP/1.1\r\nX: " + new string( 'x', 8192 ) + "\r\n\r\n"
        } )
        {
            await using MemoryStream stream = new( Encoding.ASCII.GetBytes( input ) );
            await ThrowsAsync<InvalidDataException>( async ( ) => await OAuthLoopbackReceiver.ReadCodeAsync( stream, redirect, "secret-state", "https://issuer.example", CancellationToken.None ) );
        }
        await using var pipes = await ExtensionTestPipes.CreateAsync( );
        using CancellationTokenSource deadline = new( TimeSpan.FromMilliseconds( 100 ) );
        await ThrowsAsync<OperationCanceledException>( async ( ) => await OAuthLoopbackReceiver.ReadCodeAsync( pipes.Host, redirect, "state", "https://issuer.example", deadline.Token ) );
    }

    private static async Task ExtensionSdkRejectsModeMisuseLocallyAsync( )
    {
        foreach ( var mode in new[] { ExtensionCommandModes.View, ExtensionCommandModes.NoView } )
        {
            ModeMisuseCommand command = new( mode );
            await using var harness = await ExtensionSessionHarness.CreateAsync( command );
            await harness.OpenAsync( );
            True( command.Rejected );
            Equal( 0, harness.Effects.Actions.Count );
        }
    }

    private sealed class ModeMisuseCommand( string mode ) : IExtensionCommand
    {
        public string Id => "mode-misuse";
        public string Mode => mode;
        public bool Rejected { get; private set; }
        public async ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            try
            {
                if ( mode == ExtensionCommandModes.View )
                {
                    await context.Effects.CopyTextAsync( "must not reach host", cancellationToken );
                }
                else
                {
                    await context.Views.ReplaceAsync( new DetailView { Id = "invalid-mode", Title = "Must not reach host" }, cancellationToken );
                }
            }
            catch ( InvalidOperationException )
            {
                Rejected = true;
            }
            if ( mode == ExtensionCommandModes.View )
            {
                await context.Views.ReplaceAsync( new DetailView { Id = "valid", Title = "Valid" }, cancellationToken );
            }
        }
        public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
    }
}
