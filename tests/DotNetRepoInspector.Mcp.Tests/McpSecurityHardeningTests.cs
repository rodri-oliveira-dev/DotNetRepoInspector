using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

using Xunit;

namespace DotNetRepoInspector.Mcp.Tests;

public sealed class McpSecurityHardeningTests
{
    [Theory]
    [InlineData("GITHUB_TOKEN")]
    [InlineData("AWS_ACCESS_KEY_ID")]
    [InlineData("ConnectionStrings__Database")]
    [InlineData("SSH_AUTH_SOCK")]
    public void ProcessEnvironment_ClassifiesCredentialBearingNamesAsSensitive(string name)
    {
        Assert.True(McpProcessEnvironment.IsSensitiveName(name));
    }

    [Theory]
    [InlineData("PATH")]
    [InlineData("DOTNET_ROOT")]
    [InlineData("HOME")]
    public void ProcessEnvironment_PreservesRequiredNonSensitiveNames(string name)
    {
        Assert.False(McpProcessEnvironment.IsSensitiveName(name));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsExplicitConfigurationThroughSymbolicLink()
    {
        using var fixture = LinkedPathFixture.TryCreate(fileLink: true);
        if (fixture is null)
        {
            return;
        }

        var inspector = StubInspector.Returning(EmptyReport());
        var handler = CreateHandler(inspector, fixture.RepositoryRoot);

        var result = await handler.ExecuteAsync(
            "linked-config.json",
            disableConfigurationFile: false,
            excludedPaths: null,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        AssertSecurityError(result, "path_through_link");
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsDefaultConfigurationThroughSymbolicLink()
    {
        using var fixture = LinkedPathFixture.TryCreate(
            fileLink: true,
            linkName: ".dotnetrepoinspector.json");
        if (fixture is null)
        {
            return;
        }

        var inspector = StubInspector.Returning(EmptyReport());
        var handler = CreateHandler(inspector, fixture.RepositoryRoot);

        var result = await handler.ExecuteAsync(
            configurationPath: null,
            disableConfigurationFile: false,
            excludedPaths: null,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        AssertSecurityError(result, "path_through_link");
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsDirectoryLinkInToolPath()
    {
        using var fixture = LinkedPathFixture.TryCreate(fileLink: false);
        if (fixture is null)
        {
            return;
        }

        var inspector = StubInspector.Returning(EmptyReport());
        var handler = CreateHandler(inspector, fixture.RepositoryRoot);

        var result = await handler.ExecuteAsync(
            configurationPath: null,
            disableConfigurationFile: true,
            excludedPaths: ["linked/Outside.csproj"],
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        AssertSecurityError(result, "path_through_link");
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsOversizedInputCollections()
    {
        var inspector = StubInspector.Returning(EmptyReport());
        var handler = CreateHandler(inspector, Path.GetFullPath("."));
        var excludedPaths = Enumerable.Range(0, McpSecurityLimits.MaxExcludedPaths + 1)
            .Select(static index => $"src/Project{index}.csproj")
            .ToArray();

        var result = await handler.ExecuteAsync(
            configurationPath: null,
            disableConfigurationFile: true,
            excludedPaths,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        AssertSecurityError(result, "input_too_large");
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsOversizedPathAndOverrideValue()
    {
        var inspector = StubInspector.Returning(EmptyReport());
        var handler = CreateHandler(inspector, Path.GetFullPath("."));
        var oversizedPath = new string('a', McpSecurityLimits.MaxRelativePathLength + 1);

        var pathResult = await handler.ExecuteAsync(
            configurationPath: null,
            disableConfigurationFile: true,
            excludedPaths: [oversizedPath],
            classificationOverrides: null,
            TestContext.Current.CancellationToken);
        var overrideResult = await handler.ExecuteAsync(
            configurationPath: null,
            disableConfigurationFile: true,
            excludedPaths: null,
            classificationOverrides: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["App.csproj"] = new string('x', McpSecurityLimits.MaxClassificationValueLength + 1)
            },
            TestContext.Current.CancellationToken);

        AssertSecurityError(pathResult, "input_too_large");
        AssertSecurityError(overrideResult, "invalid_tool_input");
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsOversizedConfigurationFile()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-McpSecurity-").FullName;
        try
        {
            var configurationPath = Path.Combine(repositoryRoot, "oversized.json");
            await File.WriteAllBytesAsync(
                configurationPath,
                new byte[McpSecurityLimits.MaxConfigurationFileBytes + 1],
                TestContext.Current.CancellationToken);
            var inspector = StubInspector.Returning(EmptyReport());
            var handler = CreateHandler(inspector, repositoryRoot);

            var result = await handler.ExecuteAsync(
                "oversized.json",
                disableConfigurationFile: false,
                excludedPaths: null,
                classificationOverrides: null,
                TestContext.Current.CancellationToken);

            AssertSecurityError(result, "input_too_large");
            Assert.Equal(0, inspector.CallCount);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RedactsSensitiveDiagnosticContextBeforeProjection()
    {
        const string secret = "must-not-cross-mcp-boundary";
        var diagnostic = new InspectionDiagnostic(
            "DRI1999",
            InspectionDiagnosticSeverity.Warning,
            "Controlled message.",
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["accessToken"] = secret,
                ["setting"] = "visible"
            });
        var report = InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, false),
            new DotNetSdkMetadata(null, null, null),
            [],
            [diagnostic]);
        var handler = CreateHandler(StubInspector.Returning(report), Path.GetFullPath("."));

        var result = await handler.ExecuteAsync(
            null,
            disableConfigurationFile: true,
            excludedPaths: null,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        var json = result.StructuredContent!.Value.GetRawText();
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        var canonicalReport = InspectionJsonSerializer.Deserialize(
            result.StructuredContent.Value
                .GetProperty("data")
                .GetProperty("report")
                .GetRawText());
        var context = Assert.Single(canonicalReport.Diagnostics).Context!;
        Assert.Equal("<redacted>", context["accessToken"]);
        Assert.Equal("visible", context["setting"]);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsToolResultAboveResponseLimit()
    {
        var oversizedMessage = new string('x', McpSecurityLimits.MaxToolResultUtf8Bytes);
        var report = InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, false),
            new DotNetSdkMetadata(null, null, null),
            [],
            [new InspectionDiagnostic(
                "DRI1999",
                InspectionDiagnosticSeverity.Warning,
                oversizedMessage,
                null,
                null)]);
        var handler = CreateHandler(StubInspector.Returning(report), Path.GetFullPath("."));

        var result = await handler.ExecuteAsync(
            null,
            disableConfigurationFile: true,
            excludedPaths: null,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

        AssertSecurityError(result, "result_too_large");
    }

    private static InspectRepositoryHandler CreateHandler(
        IRepositoryInspector inspector,
        string repositoryRoot) =>
        new(new RepositoryInspectionExecutor(
            inspector,
            new RepositoryRoot(repositoryRoot)));

    private static InspectionReport EmptyReport() =>
        InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, false),
            new DotNetSdkMetadata(null, null, null),
            [],
            []);

    private static void AssertSecurityError(
        ModelContextProtocol.Protocol.CallToolResult result,
        string code)
    {
        Assert.True(result.IsError);
        Assert.Equal(
            code,
            result.StructuredContent!.Value
                .GetProperty("error")
                .GetProperty("code")
                .GetString());
    }

    private sealed class StubInspector : IRepositoryInspector
    {
        private readonly InspectionReport _report;

        private StubInspector(InspectionReport report)
        {
            _report = report;
        }

        public int CallCount
        {
            get;
            private set;
        }

        public static StubInspector Returning(InspectionReport report) => new(report);

        public Task<InspectionReport> InspectAsync(
            RepositoryInspectionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_report);
        }
    }

    private sealed class LinkedPathFixture : IDisposable
    {
        private LinkedPathFixture(string repositoryRoot, string outsideRoot)
        {
            RepositoryRoot = repositoryRoot;
            OutsideRoot = outsideRoot;
        }

        public string RepositoryRoot
        {
            get;
        }

        private string OutsideRoot
        {
            get;
        }

        public static LinkedPathFixture? TryCreate(
            bool fileLink,
            string? linkName = null)
        {
            var repositoryRoot = Directory.CreateTempSubdirectory(
                "DotNetRepoInspector-McpRoot-").FullName;
            var outsideRoot = Directory.CreateTempSubdirectory(
                "DotNetRepoInspector-McpOutside-").FullName;

            try
            {
                if (fileLink)
                {
                    var outsideFile = Path.Combine(outsideRoot, "outside.json");
                    File.WriteAllText(outsideFile, "{}");
                    File.CreateSymbolicLink(
                        Path.Combine(repositoryRoot, linkName ?? "linked-config.json"),
                        outsideFile);
                }
                else
                {
                    Directory.CreateSymbolicLink(
                        Path.Combine(repositoryRoot, linkName ?? "linked"),
                        outsideRoot);
                }

                return new LinkedPathFixture(repositoryRoot, outsideRoot);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Directory.Delete(repositoryRoot, recursive: true);
                Directory.Delete(outsideRoot, recursive: true);
                return null;
            }
        }

        public void Dispose()
        {
            Directory.Delete(RepositoryRoot, recursive: true);
            Directory.Delete(OutsideRoot, recursive: true);
        }
    }
}
