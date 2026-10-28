namespace DotNetRepoInspector.ContainerDiscovery;

public sealed record ContainerDiscoveryRequest(
    string RepositoryRoot,
    IReadOnlyList<string>? ExcludedPaths = null,
    ContainerDiscoveryOptions? Options = null);
