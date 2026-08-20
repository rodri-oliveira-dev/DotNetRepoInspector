using System.Text.Json;

using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.Persistence.Tests;

public sealed class InspectionSnapshotJsonSerializerTests
{
    [Fact]
    public void Serialize_EmitsStableProvenanceEnvelope()
    {
        var observedAt = new DateTimeOffset(2026, 8, 20, 8, 53, 12, TimeSpan.Zero);
        var factory = new InspectionSnapshotFactory(new FixedTimeProvider(observedAt));
        var snapshot = factory.Create(
            CreateReport(),
            "1.2.3",
            new InspectionExecutionMetadata("run-42", "github-actions", "refs/heads/main"));

        string first = InspectionSnapshotJsonSerializer.Serialize(snapshot);
        string second = InspectionSnapshotJsonSerializer.Serialize(snapshot);

        Assert.Equal(first, second);

        using JsonDocument document = JsonDocument.Parse(first);
        JsonElement root = document.RootElement;

        Assert.Equal(InspectionSchema.CurrentVersion, root.GetProperty("schemaVersion").GetString());
        Assert.Equal("1.2.3", root.GetProperty("inspectorVersion").GetString());
        Assert.Equal("example.invalid/owner/sample", root.GetProperty("repositoryIdentity").GetString());
        Assert.Equal(
            "0123456789012345678901234567890123456789",
            root.GetProperty("commitSha").GetString());
        Assert.Equal("refs/heads/main", root.GetProperty("ref").GetString());
        Assert.Equal(observedAt, root.GetProperty("observedAtUtc").GetDateTimeOffset());
        Assert.Equal("run-42", root.GetProperty("execution").GetProperty("id").GetString());
        Assert.Equal(
            "github-actions",
            root.GetProperty("execution").GetProperty("provider").GetString());
        Assert.Equal("repositoryState", root.GetProperty("idempotencyScope").GetString());
        Assert.Equal(
            InspectionSchema.CurrentVersion,
            root.GetProperty("report").GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public void Serialize_RedactsSensitiveDiagnosticContextInsideReport()
    {
        const string secret = "must-not-leak";
        var report = CreateReport() with
        {
            Diagnostics =
            [
                new InspectionDiagnostic(
                    "DRI9999",
                    InspectionDiagnosticSeverity.Warning,
                    "Diagnostic for serializer regression test.",
                    "test",
                    null,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["accessToken"] = secret,
                        ["safeValue"] = "visible"
                    })
            ]
        };
        var snapshot = new InspectionSnapshotFactory(
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 20, 8, 53, 12, TimeSpan.Zero)))
            .Create(report, "1.0.0");

        string json = InspectionSnapshotJsonSerializer.Serialize(snapshot);

        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.Contains("<redacted>", json, StringComparison.Ordinal);
        Assert.Contains("visible", json, StringComparison.Ordinal);
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
