using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.Persistence.Tests;

public sealed class InspectionSnapshotFactoryTests
{
    private static readonly DateTimeOffset FirstObservation =
        new(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_CleanCanonicalRepository_IsStableAcrossReruns()
    {
        var timeProvider = new FixedTimeProvider(FirstObservation);
        var factory = new InspectionSnapshotFactory(timeProvider);
        var report = CreateReport();

        var first = factory.Create(
            report,
            "1.2.3",
            new InspectionExecutionMetadata("100", "GitHub-Actions", "refs/heads/main"));

        timeProvider.SetUtcNow(FirstObservation.AddHours(1));
        var secondReport = report with
        {
            Repository = report.Repository with
            {
                Branch = "release"
            }
        };
        var second = factory.Create(
            secondReport,
            "1.2.3",
            new InspectionExecutionMetadata("101", "github-actions", "refs/heads/release"));

        Assert.Equal(InspectionSnapshotIdempotencyScope.RepositoryState, first.IdempotencyScope);
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal("example.invalid/owner/sample", first.RepositoryIdentity);
        Assert.Equal("0123456789012345678901234567890123456789", first.CommitSha);
        Assert.Equal("refs/heads/main", first.Ref);
        Assert.Equal("github-actions", first.Execution?.Provider);
    }

    [Fact]
    public void Create_HttpsAndScpRemote_ResolveToSameRepositoryIdentityAndKey()
    {
        var factory = new InspectionSnapshotFactory(new FixedTimeProvider(FirstObservation));
        var httpsReport = CreateReport(remoteUrl: "https://github.com/Owner/Sample.git");
        var sshReport = CreateReport(remoteUrl: "git@github.com:Owner/Sample.git");

        var httpsSnapshot = factory.Create(httpsReport, "1.0.0");
        var sshSnapshot = factory.Create(sshReport, "1.0.0");

        Assert.Equal("github.com/Owner/Sample", httpsSnapshot.RepositoryIdentity);
        Assert.Equal(httpsSnapshot.RepositoryIdentity, sshSnapshot.RepositoryIdentity);
        Assert.Equal(httpsSnapshot.IdempotencyKey, sshSnapshot.IdempotencyKey);
    }

    [Fact]
    public void Create_DifferentInspectorVersion_ProducesDifferentRepositoryStateKey()
    {
        var factory = new InspectionSnapshotFactory(new FixedTimeProvider(FirstObservation));
        var report = CreateReport();

        var first = factory.Create(report, "1.0.0");
        var second = factory.Create(report, "1.1.0");

        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void Create_DifferentEvaluatedReport_ProducesDifferentRepositoryStateKey()
    {
        var factory = new InspectionSnapshotFactory(new FixedTimeProvider(FirstObservation));

        var first = factory.Create(CreateReport(resolvedSdk: "10.0.400"), "1.0.0");
        var second = factory.Create(CreateReport(resolvedSdk: "10.0.401"), "1.0.0");

        Assert.NotEqual(first.ReportSha256, second.ReportSha256);
        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void Create_DirtyRepository_UsesObservationScopeAndExecutionIdentity()
    {
        var factory = new InspectionSnapshotFactory(new FixedTimeProvider(FirstObservation));
        var report = CreateReport(isDirty: true);

        var first = factory.Create(
            report,
            "1.0.0",
            new InspectionExecutionMetadata("run-1", "gitlab-ci"));
        var second = factory.Create(
            report,
            "1.0.0",
            new InspectionExecutionMetadata("run-2", "gitlab-ci"));

        Assert.Equal(InspectionSnapshotIdempotencyScope.Observation, first.IdempotencyScope);
        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void Create_DirtyRepositoryWithoutExecutionId_UsesObservationTimestamp()
    {
        var timeProvider = new FixedTimeProvider(FirstObservation);
        var factory = new InspectionSnapshotFactory(timeProvider);
        var report = CreateReport(isDirty: true);

        var first = factory.Create(report, "1.0.0");
        timeProvider.SetUtcNow(FirstObservation.AddSeconds(1));
        var second = factory.Create(report, "1.0.0");

        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void Create_RepositoryWithoutCanonicalRemote_UsesObservationScope()
    {
        var factory = new InspectionSnapshotFactory(new FixedTimeProvider(FirstObservation));
        var report = CreateReport(remoteUrl: null);

        var snapshot = factory.Create(report, "1.0.0");

        Assert.Equal("name:sample", snapshot.RepositoryIdentity);
        Assert.Equal(InspectionSnapshotIdempotencyScope.Observation, snapshot.IdempotencyScope);
    }

    [Fact]
    public void Create_NormalizesTimestampToUtcAndPreservesGenericExecutionMetadata()
    {
        var localObservation = new DateTimeOffset(2026, 8, 20, 5, 0, 0, TimeSpan.FromHours(-3));
        var factory = new InspectionSnapshotFactory(new FixedTimeProvider(localObservation));

        var snapshot = factory.Create(
            CreateReport(),
            " 1.0.0 ",
            new InspectionExecutionMetadata(" build-42 ", " Azure-Pipelines ", " refs/tags/v1.0.0 "));

        Assert.Equal(TimeSpan.Zero, snapshot.ObservedAtUtc.Offset);
        Assert.Equal(FirstObservation, snapshot.ObservedAtUtc);
        Assert.Equal("1.0.0", snapshot.InspectorVersion);
        Assert.Equal("build-42", snapshot.Execution?.Id);
        Assert.Equal("azure-pipelines", snapshot.Execution?.Provider);
        Assert.Equal("refs/tags/v1.0.0", snapshot.Ref);
    }

    [Fact]
    public void Create_ProducesStableSha256Metadata()
    {
        var factory = new InspectionSnapshotFactory(new FixedTimeProvider(FirstObservation));

        var snapshot = factory.Create(CreateReport(), "1.0.0");

        Assert.Equal(64, snapshot.ReportSha256.Length);
        Assert.All(snapshot.ReportSha256, character => Assert.True(IsLowerHex(character)));
        Assert.StartsWith("dri1:", snapshot.IdempotencyKey, StringComparison.Ordinal);
        Assert.Equal(69, snapshot.IdempotencyKey.Length);
    }

    [Fact]
    public void Create_RejectsMissingInspectorVersion()
    {
        var factory = new InspectionSnapshotFactory(new FixedTimeProvider(FirstObservation));

        Assert.Throws<ArgumentException>(() => factory.Create(CreateReport(), " "));
    }

    private static bool IsLowerHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static InspectionReport CreateReport(
        string? remoteUrl = "https://example.invalid/owner/sample.git",
        bool isDirty = false,
        string branch = "main",
        string resolvedSdk = "10.0.400") =>
        InspectionReport.Create(
            new RepositoryMetadata(
                "sample",
                "0123456789012345678901234567890123456789",
                branch,
                remoteUrl,
                isDirty),
            new DotNetSdkMetadata(null, null, resolvedSdk),
            [],
            []);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }
    }
}
