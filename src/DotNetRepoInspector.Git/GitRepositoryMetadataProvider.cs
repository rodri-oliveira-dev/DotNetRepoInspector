using System.ComponentModel;
using System.Diagnostics;

using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Git;

public sealed class GitRepositoryMetadataProvider : IGitRepositoryMetadataProvider
{
    private static readonly RepositoryMetadata _emptyMetadata = new(null, null, null, null, null);
    private readonly string _gitExecutable;

    public GitRepositoryMetadataProvider(string gitExecutable = "git")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);
        _gitExecutable = gitExecutable;
    }

    public async Task<GitRepositoryMetadataResult> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return NotAvailable("The inspection path is invalid.");
        }

        var workingDirectory = File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath)
            : fullPath;

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return NotAvailable("The inspection path does not exist.");
        }

        var rootResult = await RunGitAsync(
            workingDirectory,
            ["rev-parse", "--show-toplevel"],
            cancellationToken);

        if (!rootResult.Started)
        {
            return NotAvailable("The Git executable could not be started.");
        }

        if (rootResult.ExitCode != 0 || string.IsNullOrWhiteSpace(rootResult.StandardOutput))
        {
            return new GitRepositoryMetadataResult(
                _emptyMetadata,
                false,
                null,
                Array.Empty<string>());
        }

        var repositoryRoot = Path.GetFullPath(rootResult.StandardOutput.Trim());
        var warnings = new List<string>();

        var commitResult = await RunGitAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "HEAD"],
            cancellationToken);
        var commitSha = SuccessfulOutput(commitResult);

        var branchResult = await RunGitAsync(
            repositoryRoot,
            ["symbolic-ref", "--quiet", "--short", "HEAD"],
            cancellationToken);
        var branch = SuccessfulOutput(branchResult);

        var remoteResult = await RunGitAsync(
            repositoryRoot,
            ["config", "--get", "remote.origin.url"],
            cancellationToken);
        var remoteUrl = SanitizeRemoteUrl(SuccessfulOutput(remoteResult));

        var statusResult = await RunGitAsync(
            repositoryRoot,
            ["status", "--porcelain=v1", "--untracked-files=normal"],
            cancellationToken);
        bool? isDirty = null;
        if (statusResult.Started && statusResult.ExitCode == 0)
        {
            isDirty = !string.IsNullOrWhiteSpace(statusResult.StandardOutput);
        }
        else
        {
            warnings.Add("Git working-tree state could not be determined.");
        }

        var name = RepositoryName(remoteUrl, repositoryRoot);
        var metadata = new RepositoryMetadata(
            name,
            commitSha,
            branch,
            remoteUrl,
            isDirty);

        return new GitRepositoryMetadataResult(
            metadata,
            true,
            repositoryRoot,
            warnings.ToArray());
    }

    private async Task<GitCommandResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _gitExecutable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return GitCommandResult.NotStarted;
            }
        }
        catch (Win32Exception)
        {
            return GitCommandResult.NotStarted;
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new GitCommandResult(
            true,
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string? SuccessfulOutput(GitCommandResult result) =>
        result.Started && result.ExitCode == 0
            ? NormalizeOptionalText(result.StandardOutput)
            : null;

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? SanitizeRemoteUrl(string? remoteUrl)
    {
        if (remoteUrl is null ||
            !Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.UserInfo))
        {
            return remoteUrl;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty
        };

        return builder.Uri.AbsoluteUri;
    }

    private static string RepositoryName(string? remoteUrl, string repositoryRoot)
    {
        if (!string.IsNullOrWhiteSpace(remoteUrl))
        {
            var candidate = remoteUrl.TrimEnd('/', '\\');
            var separatorIndex = candidate.LastIndexOfAny(['/', '\\', ':']);
            if (separatorIndex >= 0 && separatorIndex < candidate.Length - 1)
            {
                candidate = candidate[(separatorIndex + 1)..];
            }

            if (candidate.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[..^4];
            }

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return new DirectoryInfo(repositoryRoot).Name;
    }

    private static GitRepositoryMetadataResult NotAvailable(string warning) =>
        new(
            _emptyMetadata,
            false,
            null,
            [warning]);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // The process exited or became unavailable between the state check and Kill.
        }
    }

    private sealed record GitCommandResult(
        bool Started,
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public static GitCommandResult NotStarted { get; } = new(false, -1, string.Empty, string.Empty);
    }
}
