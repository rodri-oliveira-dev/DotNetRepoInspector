using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.Persistence.Tests;

public sealed class InspectionSnapshotPublisherTests
{
    [Fact]
    public async Task PublishAsync_PassesSnapshotToSinkAndReturnsSuccess()
    {
        var report = CreateReport();
        InspectionSnapshot? observedSnapshot = null;
        var sink = new DelegateSink(
            "test-sink",
            (snapshot, _) =>
            {
                observedSnapshot = snapshot;
                return Task.FromResult(InspectionSinkWriteResult.Success());
            });
        var publisher = new InspectionSnapshotPublisher();

        var result = await publisher.PublishAsync(
            new InspectionSnapshot(report),
            sink,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.ShouldFailExecution);
        Assert.Null(result.Failure);
        Assert.Same(report, observedSnapshot?.Report);
    }

    [Theory]
    [InlineData(PersistenceFailureMode.NonFatal, false)]
    [InlineData(PersistenceFailureMode.Fatal, true)]
    public async Task PublishAsync_MapsSinkFailureWithoutChangingInspection(
        PersistenceFailureMode failureMode,
        bool expectedFatal)
    {
        var report = CreateReport();
        var sink = new DelegateSink(
            "test-sink",
            (_, _) => Task.FromResult(
                InspectionSinkWriteResult.Failed(
                    "remote-unavailable",
                    "The destination is unavailable.",
                    isTransient: true)));
        var publisher = new InspectionSnapshotPublisher();

        var result = await publisher.PublishAsync(
            new InspectionSnapshot(report),
            sink,
            new InspectionPersistenceOptions { FailureMode = failureMode },
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedFatal, result.ShouldFailExecution);
        Assert.Equal("remote-unavailable", result.Failure?.Code);
        Assert.True(result.Failure?.IsTransient);
        Assert.Equal(InspectionSchema.CurrentVersion, report.SchemaVersion);
    }

    [Fact]
    public async Task PublishAsync_ReturnsTimeoutFailureWhenSinkExceedsDeadline()
    {
        var sink = new DelegateSink(
            "slow-sink",
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return InspectionSinkWriteResult.Success();
            });
        var publisher = new InspectionSnapshotPublisher();

        var result = await publisher.PublishAsync(
            new InspectionSnapshot(CreateReport()),
            sink,
            new InspectionPersistenceOptions
            {
                Timeout = TimeSpan.FromMilliseconds(50),
                FailureMode = PersistenceFailureMode.Fatal
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.True(result.ShouldFailExecution);
        Assert.Equal(InspectionPersistenceErrorCodes.Timeout, result.Failure?.Code);
        Assert.True(result.Failure?.IsTransient);
    }

    [Fact]
    public async Task PublishAsync_PropagatesCallerCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var publisher = new InspectionSnapshotPublisher();
        var sink = new DelegateSink(
            "test-sink",
            (_, _) => Task.FromResult(InspectionSinkWriteResult.Success()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => publisher.PublishAsync(
                new InspectionSnapshot(CreateReport()),
                sink,
                cancellationToken: cancellationSource.Token));
    }

    [Fact]
    public async Task PublishAsync_DoesNotExposeUnexpectedExceptionDetails()
    {
        const string secret = "Bearer must-not-leak";
        var sink = new DelegateSink(
            "broken-sink",
            (_, _) => Task.FromException<InspectionSinkWriteResult>(
                new InvalidOperationException(secret)));
        var publisher = new InspectionSnapshotPublisher();

        var result = await publisher.PublishAsync(
            new InspectionSnapshot(CreateReport()),
            sink,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(
            InspectionPersistenceErrorCodes.UnexpectedSinkFailure,
            result.Failure?.Code);
        Assert.DoesNotContain(secret, result.Failure?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsync_RejectsNonPositiveTimeout()
    {
        var publisher = new InspectionSnapshotPublisher();
        var sink = new DelegateSink(
            "test-sink",
            (_, _) => Task.FromResult(InspectionSinkWriteResult.Success()));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => publisher.PublishAsync(
                new InspectionSnapshot(CreateReport()),
                sink,
                new InspectionPersistenceOptions { Timeout = TimeSpan.Zero },
                TestContext.Current.CancellationToken));
    }

    private static InspectionReport CreateReport() =>
        InspectionReport.Create(
            new RepositoryMetadata(
                "sample",
                "0123456789012345678901234567890123456789",
                "main",
                "https://example.invalid/owner/sample.git",
                false),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            [],
            []);

    private sealed class DelegateSink : IInspectionSnapshotSink
    {
        private readonly Func<InspectionSnapshot, CancellationToken, Task<InspectionSinkWriteResult>> _write;

        public DelegateSink(
            string name,
            Func<InspectionSnapshot, CancellationToken, Task<InspectionSinkWriteResult>> write)
        {
            Name = name;
            _write = write;
        }

        public string Name
        {
            get;
        }

        public Task<InspectionSinkWriteResult> WriteAsync(
            InspectionSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            _write(snapshot, cancellationToken);
    }
}
