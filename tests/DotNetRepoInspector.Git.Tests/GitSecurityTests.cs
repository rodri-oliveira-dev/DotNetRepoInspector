using System.Diagnostics;

using Xunit;

namespace DotNetRepoInspector.Git.Tests;

public sealed class GitSecurityTests
{
    [Fact]
    public async Task InspectAsync_StripsUserInfoQueryAndFragmentFromRemoteUrl()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-GitSecurity-").FullName;

        try
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
            await RunGitAsync(
                repositoryRoot,
                "remote",
                "add",
                "origin",
                "https://user:password@example.com/owner/private-repository.git?access_token=query-secret#fragment-secret");

            var provider = new GitRepositoryMetadataProvider();
            var result = await provider.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.Equal("private-repository", result.Metadata.Name);
            Assert.Equal(
                "https://example.com/owner/private-repository.git",
                result.Metadata.RemoteUrl);
            Assert.DoesNotContain("password", result.Metadata.RemoteUrl ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("query-secret", result.Metadata.RemoteUrl ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("fragment-secret", result.Metadata.RemoteUrl ?? string.Empty, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static async Task RunGitAsync(
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

        var error = await standardError;
        _ = await standardOutput;
        Assert.True(
            process.ExitCode == 0,
            $"Git test command failed with exit code {process.ExitCode}: {error}");
    }
}
