using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

using Xunit;

namespace DotNetRepoInspector.Cli.Tests;

public sealed class CliConfigurationTests
{
    [Fact]
    public async Task RunAsync_ForwardsConfigurationOptionsToEngine()
    {
        RepositoryInspectionRequest? observedRequest = null;
        var inspector = new RecordingInspector(request =>
        {
            observedRequest = request;
            return InspectionReport.Create(
                new RepositoryMetadata(null, null, null, null, null),
                new DotNetSdkMetadata(null, null, "10.0.400"),
                Array.Empty<ProjectInspection>(),
                Array.Empty<InspectionDiagnostic>());
        });
        var application = new CliApplication(inspector, "1.0.0-test");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await application.RunAsync(
            [
                "repository",
                "--config",
                "config/inspector.json",
                "--exclude",
                "generated",
                "--classify",
                "src/App/App.csproj=web"
            ],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.NotNull(observedRequest);
        Assert.Equal("repository", observedRequest.RepositoryRoot);
        Assert.Equal("config/inspector.json", observedRequest.ConfigurationPath);
        Assert.False(observedRequest.DisableConfigurationFile);
        Assert.Equal(["generated"], observedRequest.ExcludedPaths);
        Assert.Equal("web", observedRequest.ClassificationOverrides?["src/App/App.csproj"]);
    }

    private sealed class RecordingInspector(
        Func<RepositoryInspectionRequest, InspectionReport> handler) : IRepositoryInspector
    {
        public Task<InspectionReport> InspectAsync(
            RepositoryInspectionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(request));
        }
    }
}
