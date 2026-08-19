namespace DotNetRepoInspector.Git;

public interface IGitRepositoryMetadataProvider
{
    Task<GitRepositoryMetadataResult> InspectAsync(
        string path,
        CancellationToken cancellationToken = default);
}
