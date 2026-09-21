using Xunit;

namespace DotNetRepoInspector.Mcp.Tests;

public sealed class McpStartupOptionsTests
{
    [Fact]
    public void Parse_RequiresExplicitRoot()
    {
        var result = McpStartupOptions.Parse(Array.Empty<string>());

        Assert.False(result.Succeeded);
        Assert.Contains("--root", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NormalizesExistingRoot()
    {
        var result = McpStartupOptions.Parse(["--root", "."]);

        Assert.True(result.Succeeded);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(".")),
            result.Options!.RepositoryRoot.FullPath);
    }

    [Fact]
    public void Parse_RejectsMissingRootWithoutEchoingItsValue()
    {
        var missingRoot = Path.Combine(
            Path.GetTempPath(),
            $"DotNetRepoInspector-Missing-{Guid.NewGuid():N}");

        var result = McpStartupOptions.Parse(["--root", missingRoot]);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(missingRoot, result.Error, StringComparison.Ordinal);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsUnknownArguments()
    {
        var result = McpStartupOptions.Parse(["--repository-root", "."]);

        Assert.False(result.Succeeded);
        Assert.Contains("unknown", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ResolvesSymbolicLinkRootToItsFinalTarget()
    {
        var target = Directory.CreateTempSubdirectory("DotNetRepoInspector-McpTarget-").FullName;
        var parent = Directory.CreateTempSubdirectory("DotNetRepoInspector-McpLink-").FullName;
        var link = Path.Combine(parent, "repository");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return;
            }

            var result = McpStartupOptions.Parse(["--root", link]);

            Assert.True(result.Succeeded);
            Assert.Equal(
                RepositoryRootCanonicalizer.NormalizeExistingDirectory(target),
                result.Options!.RepositoryRoot.FullPath);
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            Directory.Delete(parent, recursive: true);
            Directory.Delete(target, recursive: true);
        }
    }
}
