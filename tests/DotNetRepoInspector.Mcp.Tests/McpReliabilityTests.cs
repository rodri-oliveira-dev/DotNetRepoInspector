using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

using Xunit;

namespace DotNetRepoInspector.Mcp.Tests;

public sealed class McpReliabilityTests
{
    [Fact]
    public async Task Executor_SerializesConcurrentInspectionsForConfiguredRoot()
    {
        var active = 0;
        var maximumActive = 0;
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspector = new StubInspector(async cancellationToken =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maximumActive, current);
            firstEntered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref active);
            return CreateReport();
        });
        var executor = CreateExecutor(inspector);

        var first = ExecuteAsync(executor);
        await firstEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = ExecuteAsync(executor);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, inspector.CallCount);
        release.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        Assert.All(outcomes, static outcome => Assert.True(outcome.Succeeded));
        Assert.Equal(2, inspector.CallCount);
        Assert.Equal(1, maximumActive);
    }

    [Fact]
    public async Task Executor_RejectsRequestsBeyondBoundedQueue()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspector = new StubInspector(async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return CreateReport();
        });
        var executor = CreateExecutor(inspector, maxQueuedInspections: 0);

        var active = ExecuteAsync(executor);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var rejected = await ExecuteAsync(executor);
        release.SetResult();
        await active;

        Assert.False(rejected.Succeeded);
        Assert.Equal("server_busy", rejected.ErrorCode);
        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public async Task Executor_TimesOutInspection()
    {
        var inspector = new StubInspector(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreateReport();
        });
        var executor = CreateExecutor(
            inspector,
            TimeSpan.FromMilliseconds(50));

        var outcome = await ExecuteAsync(executor);

        Assert.False(outcome.Succeeded);
        Assert.Equal("inspection_timed_out", outcome.ErrorCode);
    }

    private static RepositoryInspectionExecutor CreateExecutor(
        IRepositoryInspector inspector,
        TimeSpan? timeout = null,
        int maxQueuedInspections = 8) =>
        new(
            inspector,
            new RepositoryRoot(FixturePath("EmptyRepository")),
            timeout ?? TimeSpan.FromSeconds(10),
            maxQueuedInspections);

    private static Task<RepositoryInspectionOutcome> ExecuteAsync(
        RepositoryInspectionExecutor executor) =>
        executor.ExecuteAsync(
            configurationPath: null,
            disableConfigurationFile: false,
            excludedPaths: null,
            classificationOverrides: null,
            TestContext.Current.CancellationToken);

    private static InspectionReport CreateReport() =>
        InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, false),
            new DotNetSdkMetadata("10.0.400", null, "10.0.401"),
            Array.Empty<ProjectInspection>(),
            Array.Empty<InspectionDiagnostic>());

    private static string FixturePath(string relativePath) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    private sealed class StubInspector : IRepositoryInspector
    {
        private readonly Func<CancellationToken, Task<InspectionReport>> _handler;
        private int _callCount;

        public StubInspector(Func<CancellationToken, Task<InspectionReport>> handler)
        {
            _handler = handler;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<InspectionReport> InspectAsync(
            RepositoryInspectionRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return _handler(cancellationToken);
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref location, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
