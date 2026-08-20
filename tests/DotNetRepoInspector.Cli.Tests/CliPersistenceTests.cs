using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;
using DotNetRepoInspector.Persistence;

using Xunit;

namespace DotNetRepoInspector.Cli.Tests;

public sealed class CliPersistenceTests
{
    [Fact]
    public void Parse_ReadsHttpSinkOptions()
    {
        CliParseResult result = CliOptionsParser.Parse(
            [
                ".",
                "--sink",
                "http",
                "--sink-url",
                "https://evidence.example/snapshots",
                "--sink-timeout-seconds",
                "30",
                "--sink-failure-mode",
                "fatal",
                "--sink-max-attempts",
                "4"
            ]);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Options);
        Assert.True(result.Options.Persistence.Enabled);
        Assert.Equal("http", result.Options.Persistence.Sink);
        Assert.Equal(
            new Uri("https://evidence.example/snapshots"),
            result.Options.Persistence.Endpoint);
        Assert.Equal(30, result.Options.Persistence.TimeoutSeconds);
        Assert.Equal(PersistenceFailureMode.Fatal, result.Options.Persistence.FailureMode);
        Assert.Equal(4, result.Options.Persistence.MaxAttempts);
    }

    [Fact]
    public void Parse_RejectsSinkWithoutEndpoint()
    {
        CliParseResult result = CliOptionsParser.Parse(["--sink", "http"]);

        Assert.False(result.Succeeded);
        Assert.Contains("--sink-url", result.Error ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsSinkSpecificOptionsWithoutSinkSelection()
    {
        CliParseResult result = CliOptionsParser.Parse(
            ["--sink-url", "https://evidence.example/snapshots"]);

        Assert.False(result.Succeeded);
        Assert.Equal("Sink-specific options require '--sink http'.", result.Error);
    }

    [Fact]
    public void Parse_RejectsEndpointWithEmbeddedCredentialsWithoutEchoingIt()
    {
        const string endpoint = "https://user:secret@example.invalid/snapshots";

        CliParseResult result = CliOptionsParser.Parse(
            ["--sink", "http", "--sink-url", endpoint]);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("secret", result.Error ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_NonFatalPersistenceFailureKeepsInspectionExitCode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var persistence = StubPersistenceCoordinator.Returning(
            Failure(PersistenceFailureMode.NonFatal));
        var application = new CliApplication(
            StubInspector.Returning(CreateReport()),
            persistence,
            "1.0.0-test");

        int exitCode = await application.RunAsync(
            SinkArguments(),
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(1, persistence.CallCount);
        _ = InspectionJsonSerializer.Deserialize(output.ToString());
        Assert.Contains("persistence.failed", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_FatalPersistenceFailureReturnsDedicatedExitCodeAfterWritingReport()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var persistence = StubPersistenceCoordinator.Returning(
            Failure(PersistenceFailureMode.Fatal));
        var application = new CliApplication(
            StubInspector.Returning(CreateReport()),
            persistence,
            "1.0.0-test");

        int exitCode = await application.RunAsync(
            [.. SinkArguments(), "--sink-failure-mode", "fatal"],
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.PersistenceFailed, exitCode);
        Assert.Equal(1, persistence.CallCount);
        _ = InspectionJsonSerializer.Deserialize(output.ToString());
        Assert.Contains("[error]", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_PersistenceCancellationReturnsCancelled()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var persistence = new StubPersistenceCoordinator(
            (_, _, _, _) => Task.FromException<InspectionPersistenceResult?>(
                new OperationCanceledException()));
        var application = new CliApplication(
            StubInspector.Returning(CreateReport()),
            persistence,
            "1.0.0-test");

        int exitCode = await application.RunAsync(
            SinkArguments(),
            output,
            error,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Cancelled, exitCode);
        _ = InspectionJsonSerializer.Deserialize(output.ToString());
    }

    private static string[] SinkArguments() =>
        [".", "--sink", "http", "--sink-url", "https://evidence.example/snapshots"];

    private static InspectionPersistenceResult Failure(PersistenceFailureMode failureMode) =>
        new(
            "http",
            false,
            new InspectionSinkFailure(
                "http-test-failure",
                "The configured persistence destination is unavailable.",
                true),
            failureMode);

    private static InspectionReport CreateReport() =>
        InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, null),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            [],
            []);

    private sealed class StubInspector : IRepositoryInspector
    {
        private readonly Func<RepositoryInspectionRequest, CancellationToken, Task<InspectionReport>> _handler;

        private StubInspector(
            Func<RepositoryInspectionRequest, CancellationToken, Task<InspectionReport>> handler)
        {
            _handler = handler;
        }

        public static StubInspector Returning(InspectionReport report) =>
            new((_, _) => Task.FromResult(report));

        public Task<InspectionReport> InspectAsync(
            RepositoryInspectionRequest request,
            CancellationToken cancellationToken = default) =>
            _handler(request, cancellationToken);
    }

    private sealed class StubPersistenceCoordinator : ICliPersistenceCoordinator
    {
        private readonly Func<
            InspectionReport,
            string,
            CliPersistenceOptions,
            CancellationToken,
            Task<InspectionPersistenceResult?>> _handler;

        public StubPersistenceCoordinator(
            Func<
                InspectionReport,
                string,
                CliPersistenceOptions,
                CancellationToken,
                Task<InspectionPersistenceResult?>> handler)
        {
            _handler = handler;
        }

        public int CallCount
        {
            get;
            private set;
        }

        public static StubPersistenceCoordinator Returning(InspectionPersistenceResult result) =>
            new((_, _, _, _) => Task.FromResult<InspectionPersistenceResult?>(result));

        public Task<InspectionPersistenceResult?> PublishAsync(
            InspectionReport report,
            string inspectorVersion,
            CliPersistenceOptions options,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return _handler(report, inspectorVersion, options, cancellationToken);
        }
    }
}
