using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionReadyErrorAllowsImmediateRetryAsync( )
    {
        var attempts = 0;
        SessionViewCommand command = new( )
        {
            Handler = ( _, _, _ ) =>
            {
                if ( ++attempts == 1 )
                {
                    throw new InvalidOperationException( "First attempt failed" );
                }

                return ValueTask.CompletedTask;
            }
        };
        await using var harness = await ExtensionSessionHarness.CreateAsync( command );
        await harness.OpenAsync( );
        harness.OnPublish = async ( state, token ) =>
        {
            if ( state.Status == ExtensionSessionStatuses.Ready && state.Error is not null )
            {
                await harness.ActAsync( "Update", token );
            }
        };

        await harness.ActAsync( "Update" );
        Equal( 2, attempts );
        True( harness.Latest.Error is null );
    }

    private static async Task ExtensionFailedFormCanRetrySameRevisionAsync( )
    {
        ReviewFormCommand command = new( ) { FailuresRemaining = 1 };
        await using var harness = await ExtensionSessionHarness.CreateAsync( command );
        await harness.OpenAsync( );
        Dictionary<string, JsonElement> values = new( ) { ["accepted"] = JsonElement.Parse( "true" ) };
        await harness.SubmitAsync( 1, values );
        True( harness.Latest.Error is not null );
        await harness.SubmitAsync( 1, values );
        Equal( 2, command.Submissions );
        True( harness.Latest.Error is null );
        await ThrowsAsync<InvalidOperationException>( async ( ) => await harness.SubmitAsync( 1, values ) );
        Equal( 2, command.Submissions );
    }

    private sealed class ReviewFormCommand : IExtensionCommand
    {
        public string Id => "review-form";
        public int FailuresRemaining { get; set; }
        public int Submissions { get; private set; }
        public ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken ) => context.Views.ReplaceAsync( new FormView
        {
            Id = "review-form",
            Title = "Review form",
            Fields = [FormField.Checkbox( "accepted", "Accept" ) with { Required = true }],
            Submit = ViewAction.Callback( "save", "Save" )
        }, cancellationToken );

        public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken )
        {
            Submissions++;
            if ( FailuresRemaining > 0 )
            {
                FailuresRemaining--;
                throw new InvalidOperationException( "Submit failed" );
            }

            return ValueTask.CompletedTask;
        }
    }
}
