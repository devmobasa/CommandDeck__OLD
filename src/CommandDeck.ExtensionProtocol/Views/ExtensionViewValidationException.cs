namespace CommandDeck.Extensions.Views;

/// <summary>Structural author diagnostics, carried as the inner exception of InvalidDataException.</summary>
/// <remarks>InvalidDataException is sealed. The outer exception preserves existing rejection catch sites.</remarks>
public sealed class ExtensionViewValidationException( string path, string reason ) : Exception( path + ": " + reason )
{
    public string Path { get; } = path;
    public string Reason { get; } = reason;
}
