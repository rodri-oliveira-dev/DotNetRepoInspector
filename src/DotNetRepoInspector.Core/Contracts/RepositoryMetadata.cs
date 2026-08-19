namespace DotNetRepoInspector.Core.Contracts;

public sealed record RepositoryMetadata(
    string? Name,
    string? CommitSha,
    string? Branch,
    string? RemoteUrl);
