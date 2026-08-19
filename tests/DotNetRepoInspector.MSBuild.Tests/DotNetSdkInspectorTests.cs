using System.Text.Json;

using DotNetRepoInspector.MSBuild.Sdk;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class DotNetSdkInspectorTests
{
    [Fact]
    public async Task InspectAsync_ReadsConfiguredAndResolvedSdkSeparately()
    {
        var repositoryRoot = FixturePath("Sdk", "WithGlobalJson");
        var inspector = new DotNetSdkInspector();

        var result = await inspector.InspectAsync(
            repositoryRoot,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Error?.Message ?? "SDK inspection failed.");
        Assert.Equal(Path.GetFullPath(repositoryRoot), result.RepositoryRoot);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(repositoryRoot), "global.json"),
            result.GlobalJsonPath);
        Assert.NotNull(result.Configuration);
        Assert.Equal("10.0.100", result.Configuration.Version);
        Assert.Equal("latestFeature", result.Configuration.RollForward);
        Assert.False(result.Configuration.AllowPrerelease);
        Assert.False(string.IsNullOrWhiteSpace(result.ResolvedSdkVersion));
    }

    [Fact]
    public async Task InspectAsync_SupportsRepositoryWithoutGlobalJson()
    {
        var repositoryRoot = CreateTemporaryDirectory();

        try
        {
            var inspector = new DotNetSdkInspector();
            var result = await inspector.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, result.Error?.Message ?? "SDK inspection failed.");
            Assert.Null(result.GlobalJsonPath);
            Assert.Null(result.Configuration);
            Assert.False(string.IsNullOrWhiteSpace(result.ResolvedSdkVersion));
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_FindsGlobalJsonInAncestorDirectory()
    {
        var repositoryRoot = CreateTemporaryDirectory();

        try
        {
            var inspector = new DotNetSdkInspector();
            var baseline = await inspector.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.True(baseline.Succeeded, baseline.Error?.Message ?? "SDK inspection failed.");
            Assert.False(string.IsNullOrWhiteSpace(baseline.ResolvedSdkVersion));

            var nestedRoot = Path.Combine(repositoryRoot, "src", "App");
            Directory.CreateDirectory(nestedRoot);
            var globalJsonPath = Path.Combine(repositoryRoot, "global.json");
            await WriteGlobalJsonAsync(
                globalJsonPath,
                baseline.ResolvedSdkVersion!,
                "disable",
                allowPrerelease: true);

            var result = await inspector.InspectAsync(
                nestedRoot,
                TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, result.Error?.Message ?? "SDK inspection failed.");
            Assert.Equal(globalJsonPath, result.GlobalJsonPath);
            Assert.NotNull(result.Configuration);
            Assert.Equal(baseline.ResolvedSdkVersion, result.Configuration.Version);
            Assert.Equal("disable", result.Configuration.RollForward);
            Assert.True(result.Configuration.AllowPrerelease);
            Assert.Equal(baseline.ResolvedSdkVersion, result.ResolvedSdkVersion);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_ReturnsExplicitFailureWhenRequestedSdkIsUnavailable()
    {
        var repositoryRoot = CreateTemporaryDirectory();

        try
        {
            var globalJsonPath = Path.Combine(repositoryRoot, "global.json");
            await WriteGlobalJsonAsync(
                globalJsonPath,
                "999.0.100",
                "disable",
                allowPrerelease: false);

            var inspector = new DotNetSdkInspector();
            var result = await inspector.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.False(result.Succeeded);
            Assert.Equal(globalJsonPath, result.GlobalJsonPath);
            Assert.NotNull(result.Configuration);
            Assert.Equal("999.0.100", result.Configuration.Version);
            Assert.Null(result.ResolvedSdkVersion);
            Assert.Equal(DotNetSdkInspectionErrorCode.SdkResolutionFailed, result.Error?.Code);
            Assert.NotEqual(0, result.Error?.ExitCode);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_ReturnsInvalidGlobalJsonForMalformedSdkConfiguration()
    {
        var repositoryRoot = CreateTemporaryDirectory();

        try
        {
            var globalJsonPath = Path.Combine(repositoryRoot, "global.json");
            await File.WriteAllTextAsync(
                globalJsonPath,
                "{ \"sdk\": { \"allowPrerelease\": \"false\" } }",
                TestContext.Current.CancellationToken);

            var inspector = new DotNetSdkInspector();
            var result = await inspector.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.False(result.Succeeded);
            Assert.Equal(DotNetSdkInspectionErrorCode.GlobalJsonInvalid, result.Error?.Code);
            Assert.Equal(globalJsonPath, result.GlobalJsonPath);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_ReturnsDotNetHostNotFoundWhenDotNetCannotStart()
    {
        var repositoryRoot = CreateTemporaryDirectory();

        try
        {
            var inspector = new DotNetSdkInspector($"dotnet-missing-{Guid.NewGuid():N}");
            var result = await inspector.InspectAsync(
                repositoryRoot,
                TestContext.Current.CancellationToken);

            Assert.False(result.Succeeded);
            Assert.Equal(DotNetSdkInspectionErrorCode.DotNetHostNotFound, result.Error?.Code);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    private static string FixturePath(params string[] segments) =>
        segments.Aggregate(
            Path.Combine(AppContext.BaseDirectory, "Fixtures"),
            Path.Combine);

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-repo-inspector-sdk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WriteGlobalJsonAsync(
        string path,
        string version,
        string rollForward,
        bool allowPrerelease)
    {
        var payload = new
        {
            sdk = new
            {
                version,
                rollForward,
                allowPrerelease
            }
        };

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(payload),
            TestContext.Current.CancellationToken);
    }
}
