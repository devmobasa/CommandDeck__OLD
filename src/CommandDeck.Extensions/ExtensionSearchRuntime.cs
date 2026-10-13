using System.Text.Json;
using CommandDeck.ExtensionProtocol;

namespace CommandDeck.Extensions;

/// <summary>Opt-in search implementation on a command. Search receives no interactive context or broker handles.</summary>
public interface IExtensionSearchProvider
{
    string SearchId { get; }
    ValueTask<ExtensionSearchResponse> SearchAsync( SearchContext context, CancellationToken cancellationToken );
}

public sealed record SearchContext( string Query, int Limit, JsonElement? Cache, bool Global );

internal sealed class ExtensionSearchRuntime( IReadOnlyList<IExtensionCommand> commands, TextWriter diagnostics )
{
    private enum Phase { Inactive, AwaitInitialized, Ready, Searched }

    private static readonly JsonElement EmptyObject = JsonElement.Parse( "{}" );
    private IExtensionSearchProvider? _provider;
    private bool _global;
    private Phase _phase;
    public bool Active => _phase != Phase.Inactive;
    public JsonElement Initialize( InitializeRequest request )
    {
        if ( request.Protocol is not { Major: 1, Minor: 0 } || request.ExecutionEpoch != 1 || string.IsNullOrEmpty( request.SessionId ) ||
            request.Purpose is not (ExtensionPurposes.ScopedSearch or ExtensionPurposes.GlobalSearch) || request.Limits?.MaximumFrameBytes != ExtensionLimits.MaximumFrameBytes ||
            request.HostFeatures is null || !request.HostFeatures.SequenceEqual( new[] { ExtensionFeatures.Search } ) )
        {
            throw new InvalidDataException( "Invalid search initialization." );
        }
        var value = commands.OfType<IExtensionSearchProvider>( ).SingleOrDefault( provider => provider.SearchId == request.CommandId );
        if ( value is null )
        {
            throw new InvalidDataException( "The search provider is unavailable." );
        }

        _provider = value;
        _global = request.Purpose == ExtensionPurposes.GlobalSearch;
        _phase = Phase.AwaitInitialized;
        return JsonSerializer.SerializeToElement( new InitializeResult( new( ), ExtensionApp.SdkVersion, commands.Select( command => command.Id ).ToArray( ) ), ExtensionJsonContext.Default.InitializeResult );
    }
    public async ValueTask<JsonElement> HandleAsync( ExtensionFrame frame, CancellationToken token )
    {
        if ( frame.Method == ExtensionMethods.Initialized && _phase == Phase.AwaitInitialized )
        {
            _phase = Phase.Ready;
            return EmptyObject;
        }
        if ( frame.Method != ExtensionMethods.SearchQuery || _phase != Phase.Ready )
        {
            throw new InvalidDataException( "Invalid search operation." );
        }
        _phase = Phase.Searched;
        var input = frame.Params?.Deserialize( ExtensionSearchJsonContext.Default.ExtensionSearchRequest );
        if ( input is null )
        {
            throw new InvalidDataException( "Missing search input." );
        }

        if ( input.Query is null || input.Query.Length > 2048 || input.Limit is < 1 or > ExtensionSearchValidation.MaximumItems ||
            input.Cache?.GetRawText( ).Length > 16 * 1024 )
        {
            throw new InvalidDataException( "Search input exceeds its limits." );
        }
        ExtensionSearchResponse result;
        try
        { result = await _provider!.SearchAsync( new( input.Query, input.Limit, input.Cache?.Clone( ), _global ), token ).ConfigureAwait( false ); }
        catch ( Exception exception ) { SdkDiagnostics.Log( diagnostics, _provider!.SearchId, "SearchAsync", exception ); throw; }
        ExtensionSearchValidation.Validate( result );
        return JsonSerializer.SerializeToElement( result, ExtensionSearchJsonContext.Default.ExtensionSearchResponse );
    }
}
