namespace CommandDeck;

internal static class HerdrProjectAction
{
    public static CommandAction Create( string path ) => new( )
    {
        Id = "open-herdr",
        Title = "Open in Herdr",
        Kind = ActionKinds.HerdrOpenProject,
        Arguments = new( StringComparer.Ordinal )
        {
            ["path"] = path
        }
    };

    public static bool Matches( SearchResult result, string path ) =>
        result.Metadata.GetValueOrDefault( "path" ) == path &&
        ((result.SourceId == SourceIds.Projects && result.ProviderId == ProviderIds.Projects &&
          result.Kind == ResultKinds.CustomCommand && result.Id == StableId.Create( "project", path )) ||
         (result.SourceId == SourceIds.Folders && result.ProviderId == ProviderIds.Folders &&
          result.Kind == ResultKinds.File && result.Metadata.GetValueOrDefault( "isDirectory" ) == "true" &&
          (result.Id == StableId.Create( "browse", path ) || result.Id.StartsWith( "folder-parent:", StringComparison.Ordinal ))));
}
