namespace CommandDeck;

internal static class ContributionSnapshotBuilder
{
    public static ContributionRegistrySnapshot Build( ExtensionRegistryDocument document )
    {
        List<SourceDescriptor> sources = [];
        List<FilterDescriptor> filters = [];
        List<ExtensionCommandContribution> commands = [];
        foreach ( var installation in document.Installations )
        {
            var source = ExtensionStableIds.SourceId( installation.Key );
            var available = document.Enabled && installation.Enabled && !installation.Removed;
            sources.Add( new SourceDescriptor { Id = source, Label = installation.Manifest.Name, Icon = ExtensionIcons.Addon, Available = available } );
            filters.Add( new FilterDescriptor
            {
                Id = source,
                Label = installation.Manifest.Name,
                Token = ExtensionStableIds.FilterToken( installation.Key ),
                ProviderId = ProviderIds.Extensions,
                SourceId = source,
                Kind = ResultKinds.ExtensionCommand,
                Available = available
            } );
            commands.AddRange( installation.Manifest.Commands.Select( command => new ExtensionCommandContribution(
                ExtensionStableIds.CommandId( installation.Key, command.Id ), installation.Key, command.Id, source, command.Title,
                command.Description, Array.AsReadOnly( command.Keywords.ToArray( ) ), available, installation.Origin == ExtensionOrigins.Development ) ) );
            filters.AddRange( installation.Manifest.SearchProviders.Select( provider => new FilterDescriptor
            {
                Id = ExtensionStableIds.SearchProviderId( installation.Key, provider.Id ),
                Label = installation.Manifest.Name + " · " + provider.Title,
                Token = ExtensionStableIds.FilterToken( installation.Key, provider.Id ),
                ProviderId = ProviderIds.Extensions,
                SourceId = source,
                Kind = ResultKinds.ExtensionCommand,
                Available = available
            } ) );
        }
        return new ContributionRegistrySnapshot( document.Revision, sources, filters, commands );
    }
}
