using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Git;

public sealed record GitRepositoryMetadataResult(
    RepositoryMetadata Metadata,
    bool IsGitRepository,
    string? RepositoryRoot,
    IReadOnlyList<string> Warnings);
