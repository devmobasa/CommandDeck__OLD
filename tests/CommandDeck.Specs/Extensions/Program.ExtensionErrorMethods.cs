using System.Text;
using System.Text.Json;
using CommandDeck.ExtensionProtocol;
using CommandDeck.Extensions;
using CommandDeck.Extensions.Views;

namespace CommandDeck.Specs;

public static partial class Program
{
    private static async Task ExtensionErrorsRequireNegotiationAsync( )
    {
        foreach ( var negotiated in new[] { false, true } )
        {
            await using var pipes = await ExtensionTestPipes.CreateAsync( );
            using CancellationTokenSource deadline = new( TimeSpan.FromSeconds( 5 ) );
            await using ExtensionPeer host = new( pipes.Host, pipes.Host, true, ["command.data"],
                ( _, _ ) => throw new ExtensionApplicationException( ExtensionErrorCodes.RevisionConflict ) );
            host.Start( );
            await using ExtensionPeer child = new( pipes.Child, pipes.Child, false, [], ( _, _ ) => throw new InvalidOperationException( ) );
            child.Start( );
            if ( negotiated )
            {
                host.EnableErrorData( );
                child.EnableErrorData( );
            }
            try
            {
                await child.CallAsync( "command.data", null, deadline.Token );
                throw new InvalidOperationException( "The conflict was accepted." );
            }
            catch ( ExtensionRpcException exception )
            {
                Equal( -32001, exception.Code );
                Equal( negotiated ? ExtensionErrorCodes.RevisionConflict : null, exception.ErrorCode );
                Equal( negotiated, exception.IsConflict );
                Equal( "The stored data changed. Reload it before saving.", exception.Message );
            }
            True( host.Fault is null && child.Fault is null );
        }
    }

    private static async Task ExtensionDataErrorsReachSdkWithSafeCodesAsync( )
    {
        foreach ( var granted in new[] { false, true } )
        {
            DataErrorCommand command = new( );
            string[] permissions = [ExtensionPermissionIds.StorageRead, ExtensionPermissionIds.StorageWrite];
            var manifest = PackageFixture( ) with
            {
                Commands = [new( ) { Id = command.Id, Title = "Data codes", Capabilities = [.. permissions] }],
                Capabilities = permissions.ToDictionary( permission => permission, _ => new CapabilityRequest( "Data code test" ), StringComparer.Ordinal )
            };
            await using var harness = await ExtensionSessionHarness.CreateAsync( command, granted ? permissions : [], manifest );
            await harness.OpenAsync( );
            Equal( granted ? ExtensionErrorCodes.QuotaExceeded : ExtensionErrorCodes.PermissionDenied, command.Codes.Last( ) );
            Equal( granted ? ExtensionErrorCodes.RevisionConflict : ExtensionErrorCodes.PermissionDenied, command.Codes.First( ) );
            Equal( 0L, harness.Registry.DataStore.Read( harness.Installation.Key ).Revision );
        }
    }
    private sealed class DataErrorCommand : IExtensionCommand
    {
        public string Id => "data-errors";
        public List<string?> Codes { get; } = [];
        public async ValueTask OpenAsync( CommandContext context, CancellationToken cancellationToken )
        {
            try
            { await context.Data.WriteAsync( "draft", JsonElement.Parse( "1" ), -1, cancellationToken ); }
            catch ( ExtensionRpcException exception ) { Codes.Add( exception.ErrorCode ); }
            var oversized = JsonElement.Parse( "\"" + new string( 'x', 17000 ) + "\"" );
            try
            { await context.Data.WriteAsync( "draft", oversized, 0, cancellationToken ); }
            catch ( ExtensionRpcException exception ) { Codes.Add( exception.ErrorCode ); }
            await context.Views.ReplaceAsync( new DetailView { Id = "data", Title = "Data" }, cancellationToken );
        }
        public ValueTask HandleAsync( CommandContext context, CommandEvent commandEvent, CancellationToken cancellationToken ) => ValueTask.CompletedTask;
    }

    private static void ExtensionErrorFramesAreStrict( )
    {
        const string valid = "{\"jsonrpc\":\"2.0\",\"id\":\"h:1\",\"error\":{\"code\":-32001,\"message\":\"safe\",\"data\":{\"code\":\"revision_conflict\"}}}";
        Equal( ExtensionErrorCodes.RevisionConflict, ExtensionFraming.Parse( Encoding.UTF8.GetBytes( valid ) ).Error!.Data!.Code );
        foreach ( var data in new[] { "null", "[]", "\"bad\"", "{\"code\":\"UPPER\"}", "{\"code\":\"a\",\"extra\":true}", "{\"code\":\"" + new string( 'a', 49 ) + "\"}" } )
        {
            var invalid = valid.Replace( "{\"code\":\"revision_conflict\"}", data, StringComparison.Ordinal );
            try
            {
                ExtensionFraming.Parse( Encoding.UTF8.GetBytes( invalid ) );
                throw new InvalidOperationException( "Invalid error data was accepted." );
            }
            catch ( Exception exception ) when ( exception is InvalidDataException or JsonException ) { }
        }
        True( new ExtensionRpcException( -32001, "safe", "future_code" ).ErrorCode is null );
        var oldShape = JsonSerializer.Serialize( new InitializeResult( new( ), "preview.6", ["command"] ), ExtensionJsonContext.Default.InitializeResult );
        True( !oldShape.Contains( "features", StringComparison.Ordinal ) );
    }
}
