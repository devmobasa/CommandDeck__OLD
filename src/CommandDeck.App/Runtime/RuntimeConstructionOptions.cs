namespace CommandDeck;

internal sealed class RuntimeConstructionOptions
{
    internal bool StartIndexer { get; init; } = true;

    internal Func<AgentOptions, CodexCredentialStore, IAgentBackend>? AgentBackendFactory { get; init; }

    internal Func<CliAgentProfile, AgentOptions, IAgentBackend?>? CliAgentBackendFactory { get; init; }

    internal Func<PacmanCatalog>? PackageCatalogFactory { get; init; }

    internal Func<IPackageHelper?>? PackageHelperFactory { get; init; }

    // The assistant's clock for trust leases and conversation expiry; specs move it.
    internal TimeProvider? AgentClock { get; init; }

    internal TimeSpan OpenWithWait { get; init; } = OpenWithDetection.DefaultWait;
}
