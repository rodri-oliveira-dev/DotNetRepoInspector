namespace DotNetRepoInspector.MSBuild.Discovery;

public sealed record ProjectDiscoveryRequest(
    string RepositoryRoot,
    IReadOnlyCollection<string>? ExcludedDirectories = null);
