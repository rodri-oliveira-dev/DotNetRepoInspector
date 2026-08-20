using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Persistence;

public sealed record InspectionSnapshot
{
    internal InspectionSnapshot(
        string schemaVersion,
        string inspectorVersion,
        string repositoryIdentity,
        string? commitSha,
        string? sourceRef,
        DateTimeOffset observedAtUtc,
        InspectionExecutionMetadata? execution,
        string reportSha256,
        string idempotencyKey,
        InspectionSnapshotIdempotencyScope idempotencyScope,
        InspectionReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(inspectorVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(report);

        if (!string.Equals(schemaVersion, report.SchemaVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Snapshot schema version must match the inspection report schema version.",
                nameof(schemaVersion));
        }

        SchemaVersion = schemaVersion;
        InspectorVersion = inspectorVersion;
        RepositoryIdentity = repositoryIdentity;
        CommitSha = commitSha;
        Ref = sourceRef;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
        Execution = execution;
        ReportSha256 = reportSha256;
        IdempotencyKey = idempotencyKey;
        IdempotencyScope = idempotencyScope;
        Report = report;
    }

    public string SchemaVersion
    {
        get;
    }

    public string InspectorVersion
    {
        get;
    }

    public string RepositoryIdentity
    {
        get;
    }

    public string? CommitSha
    {
        get;
    }

    public string? Ref
    {
        get;
    }

    public DateTimeOffset ObservedAtUtc
    {
        get;
    }

    public InspectionExecutionMetadata? Execution
    {
        get;
    }

    public string ReportSha256
    {
        get;
    }

    public string IdempotencyKey
    {
        get;
    }

    public InspectionSnapshotIdempotencyScope IdempotencyScope
    {
        get;
    }

    public InspectionReport Report
    {
        get;
    }
}
