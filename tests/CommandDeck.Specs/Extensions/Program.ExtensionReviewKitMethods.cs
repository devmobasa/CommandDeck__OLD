using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Testing;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionKitValidatesStorageKeySyntaxAsync( )
    {
        foreach ( var key in new[] { "bad key", "/absolute", "λ", "", new string( 'x', 65 ) } )
        {
            KitStorageKeyCommand command = new( key );
            await using var host = await ExtensionTestHost.StartAsync( command );
            await host.OpenAsync( );
            Equal( ExtensionErrorCodes.InvalidRequest, command.ErrorCode );
            Equal( 0L, command.Stored!.Revision );
            Equal( 0, command.Stored.Values.Count );
        }

        KitStorageKeyCommand allowed = new( "good-key_1.txt" );
        await using var valid = await ExtensionTestHost.StartAsync( allowed );
        await valid.OpenAsync( );
        True( allowed.ErrorCode is null );
        Equal( 1L, allowed.Stored!.Revision );
        Equal( "value", allowed.Stored.Values["good-key_1.txt"].GetString( ) );
    }

    private static async Task ExtensionKitRequiresCheckedCheckboxAsync( )
    {
        ReviewFormCommand command = new( );
        await using var host = await ExtensionTestHost.StartAsync( command );
        await host.OpenAsync( );
        await ThrowsAsync<InvalidDataException>( async ( ) => await host.SubmitAsync( new Dictionary<string, JsonElement> { ["accepted"] = JsonElement.Parse( "false" ) } ) );
        Equal( 0, command.Submissions );
        await host.SubmitAsync( new Dictionary<string, JsonElement> { ["accepted"] = JsonElement.Parse( "true" ) } );
        Equal( 1, command.Submissions );
    }

    private sealed class KitStorageKeyCommand( string key ) : IExtensionCommand
    {
        public string Id => "kit-storage-key";
        public string? ErrorCode { get; private set; }
        public CommandDataResult? Stored { get; private set; }
        public async ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            try
            {
                await context.Data.WriteAsync( key, JsonElement.Parse( "\"value\"" ), 0, cancellationToken );
            }
            catch ( ExtensionRpcException exception )
            {
                ErrorCode = exception.ErrorCode;
            }

            Stored = await context.Data.ReadAsync( cancellationToken );
            await context.Views.ReplaceAsync( new DetailView { Id = "kit-storage-key", Title = "Storage" }, cancellationToken );
        }

        public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
    }
}
