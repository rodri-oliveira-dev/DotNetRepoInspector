namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed record IntegrationDiscoveryRequest(
    string RepositoryRoot,
    IReadOnlyList<IntegrationDiscoveryProject> Projects,
    IReadOnlyList<string>? ExcludedPaths = null,
    IntegrationDiscoveryOptions? Options = null);

public sealed record IntegrationDiscoveryProject(string ProjectPath);
