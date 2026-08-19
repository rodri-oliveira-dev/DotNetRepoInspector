using System.Diagnostics;

using Xunit;

namespace DotNetRepoInspector.Git.Tests;

public sealed class GitRepositoryMetadataProviderTests
{
    [Fact]
    public async Task InspectAsync_ReturnsRepositoryMetadataFromNestedDirectory()
    {
        var repositoryRoot = CreateTemporaryDirectory();

        try
        {
            await InitializeRepositoryAsync(repositoryRoot);
            await RunGitAsync(
                repositoryRoot,
                "remote",
                "add",
                "origin",
                "https://github.com/example/sample-repository.git");

            var nestedDirectory = Path.Combine(repositoryRoot, "src", "App");
            Directory.CreateDirectory(nestedDirectory);

            var provider = new GitRepositoryMetadataProvider();
            var result = await provider.InspectAsync(
                nestedDirectory,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsGitRepository);
            Assert.Equal(Path.GetFullPath(repositoryRoot), result.RepositoryRoot);
            Assert.Empty(result.Warnings);
            Assert.Equal("sample-repository", result.Metadata.Name);
            Assert.Matches("^[0-9a-f]{40}$", result.Metadata.CommitSha ?? string.Empty);
            Assert.Equal("main", result.Metadata.Branch);
            Assert.Equal(
                "https://github.com/example/sample-repository.git",
                result.Metadata.RemoteUrl);
            Assert.False(result.Metadata.IsDirty);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_DetectsDirtyWorkingTree()
    {
        var repositoryRoot = CreateTemporaryDirectory();

        try
        {
            await InitializeRepositoryAsync(repositoryRoot);
            await File.AppendAllTextAsync(
                Path.Combine(repositoryRoot, "README.md"),
                "changed\n",
                TestContext.Current.CancellationToken);

            var provider = new GitRepositoryMetadataProvider();
            var result = await provider.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsGitRepository);
            Assert.True(result.Metadata.IsDirty);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_DetachedHeadKeepsCommitAndOmitsBranch()
    {
        var repositoryRoot = CreateTemporaryDirectory();

        try
        {
            await InitializeRepositoryAsync(repositoryRoot);
            var commitSha = await RunGitAsync(repositoryRoot, "rev-parse", "HEAD");
            await RunGitAsync(repositoryRoot, "checkout", "--detach", commitSha);

            var provider = new GitRepositoryMetadataProvider();
            var result = await provider.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsGitRepository);
            Assert.Equal(commitSha, result.Metadata.CommitSha);
            Assert.Null(result.Metadata.Branch);
            Assert.False(result.Metadata.IsDirty);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_NonGitDirectoryReturnsEmptyMetadataWithoutWarning()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var provider = new GitRepositoryMetadataProvider();
            var result = await provider.InspectAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsGitRepository);
            Assert.Null(result.RepositoryRoot);
            Assert.Empty(result.Warnings);
            Assert.Null(result.Metadata.Name);
            Assert.Null(result.Metadata.CommitSha);
            Assert.Null(result.Metadata.Branch);
            Assert.Null(result.Metadata.RemoteUrl);
            Assert.Null(result.Metadata.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_MissingGitExecutableReturnsWarningInsteadOfThrowing()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var provider = new GitRepositoryMetadataProvider($"git-missing-{Guid.NewGuid():N}");
            var result = await provider.InspectAsync(
                directory,
                TestContext.Current.CancellationToken);

            Assert.False(result.IsGitRepository);
            Assert.Single(result.Warnings);
            Assert.Equal("The Git executable could not be started.", result.Warnings[0]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_RemovesHttpCredentialsFromRemoteUrl()
    {
        var repositoryRoot = CreateTemporaryDirectory();

        try
        {
            await InitializeRepositoryAsync(repositoryRoot);
            await RunGitAsync(
                repositoryRoot,
                "remote",
                "add",
                "origin",
                "https://user:secret@example.com/owner/private-repository.git");

            var provider = new GitRepositoryMetadataProvider();
            var result = await provider.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.Equal("private-repository", result.Metadata.Name);
            Assert.Equal(
                "https://example.com/owner/private-repository.git",
                result.Metadata.RemoteUrl);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-repo-inspector-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task InitializeRepositoryAsync(string repositoryRoot)
    {
        await RunGitAsync(repositoryRoot, "init", "-b", "main");
        await RunGitAsync(repositoryRoot, "config", "user.name", "DotNetRepoInspector Tests");
        await RunGitAsync(repositoryRoot, "config", "user.email", "tests@example.invalid");
        await File.WriteAllTextAsync(
            Path.Combine(repositoryRoot, "README.md"),
            "fixture\n",
            TestContext.Current.CancellationToken);
        await RunGitAsync(repositoryRoot, "add", "README.md");
        await RunGitAsync(repositoryRoot, "commit", "-m", "initial commit");
    }

    private static async Task<string> RunGitAsync(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start(), "Git test process could not be started.");

        var standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        var output = await standardOutput;
        var error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {error}");

        return output.Trim();
    }
}
