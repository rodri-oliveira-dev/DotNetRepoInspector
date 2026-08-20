namespace DotNetRepoInspector.Engine;

public sealed record RepositoryInspectionRequest(
    string RepositoryRoot,
    IReadOnlyCollection<string>? ExcludedDirectories = null,
    string? ConfigurationPath = null,
    bool DisableConfigurationFile = false,
    IReadOnlyCollection<string>? ExcludedPaths = null,
    IReadOnlyDictionary<string, string>? ClassificationOverrides = null);
