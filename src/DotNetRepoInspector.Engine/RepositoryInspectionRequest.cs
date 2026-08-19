namespace DotNetRepoInspector.Engine;

public sealed record RepositoryInspectionRequest(
    string RepositoryRoot,
    IReadOnlyCollection<string>? ExcludedDirectories = null);
