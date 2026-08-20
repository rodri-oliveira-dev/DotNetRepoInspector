using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Persistence;

public sealed class InspectionSnapshotFactory
{
    private const string KeyVersion = "dri-snapshot-v1";

    private readonly TimeProvider _timeProvider;

    public InspectionSnapshotFactory(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public InspectionSnapshot Create(
        InspectionReport report,
        string inspectorVersion,
        InspectionExecutionMetadata? execution = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(inspectorVersion);

        string normalizedInspectorVersion = inspectorVersion.Trim();
        DateTimeOffset observedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        InspectionExecutionMetadata? normalizedExecution = NormalizeExecution(execution);
        (string repositoryIdentity, bool hasCanonicalRemoteIdentity) =
            ResolveRepositoryIdentity(report.Repository);
        string? commitSha = NormalizeOptional(report.Repository.CommitSha)?.ToLowerInvariant();
        string? sourceRef = normalizedExecution?.Ref ?? NormalizeOptional(report.Repository.Branch);
        string normalizedReport = InspectionJsonSerializer.Serialize(report);
        string reportSha256 = ComputeSha256(normalizedReport);

        InspectionSnapshotIdempotencyScope idempotencyScope =
            hasCanonicalRemoteIdentity
            && report.Repository.IsDirty is false
            && commitSha is not null
                ? InspectionSnapshotIdempotencyScope.RepositoryState
                : InspectionSnapshotIdempotencyScope.Observation;

        string keyReportSha256 = idempotencyScope == InspectionSnapshotIdempotencyScope.RepositoryState
            ? ComputeRepositoryStateReportSha256(report, repositoryIdentity)
            : reportSha256;

        string idempotencyKey = BuildIdempotencyKey(
            idempotencyScope,
            repositoryIdentity,
            commitSha,
            normalizedInspectorVersion,
            report.SchemaVersion,
            keyReportSha256,
            normalizedExecution,
            observedAtUtc);

        return new InspectionSnapshot(
            report.SchemaVersion,
            normalizedInspectorVersion,
            repositoryIdentity,
            commitSha,
            sourceRef,
            observedAtUtc,
            normalizedExecution,
            reportSha256,
            idempotencyKey,
            idempotencyScope,
            report);
    }

    private static InspectionExecutionMetadata? NormalizeExecution(
        InspectionExecutionMetadata? execution)
    {
        if (execution is null)
        {
            return null;
        }

        string? id = NormalizeOptional(execution.Id);
        string? provider = NormalizeOptional(execution.Provider)?.ToLowerInvariant();
        string? sourceRef = NormalizeOptional(execution.Ref);

        return id is null && provider is null && sourceRef is null
            ? null
            : new InspectionExecutionMetadata(id, provider, sourceRef);
    }

    private static (string Identity, bool HasCanonicalRemoteIdentity) ResolveRepositoryIdentity(
        RepositoryMetadata repository)
    {
        string? remoteUrl = NormalizeOptional(repository.RemoteUrl);
        if (remoteUrl is not null && TryNormalizeRemoteIdentity(remoteUrl, out string? remoteIdentity))
        {
            return (remoteIdentity, true);
        }

        string? name = NormalizeOptional(repository.Name);
        return name is null
            ? ("unknown", false)
            : ($"name:{name}", false);
    }

    private static bool TryNormalizeRemoteIdentity(string remoteUrl, out string identity)
    {
        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? uri)
            && !string.IsNullOrWhiteSpace(uri.Host))
        {
            string? path = NormalizeRemotePath(Uri.UnescapeDataString(uri.AbsolutePath));
            if (path is null)
            {
                identity = string.Empty;
                return false;
            }

            string host = uri.IdnHost.ToLowerInvariant();
            if (!uri.IsDefaultPort)
            {
                host = string.Concat(
                    host,
                    ":",
                    uri.Port.ToString(CultureInfo.InvariantCulture));
            }

            identity = $"{host}/{path}";
            return true;
        }

        string scpLike = remoteUrl.Trim();
        int userSeparator = scpLike.LastIndexOf('@');
        if (userSeparator >= 0 && userSeparator < scpLike.Length - 1)
        {
            scpLike = scpLike[(userSeparator + 1)..];
        }

        int pathSeparator = scpLike.IndexOf(':');
        if (pathSeparator <= 1 || pathSeparator == scpLike.Length - 1)
        {
            identity = string.Empty;
            return false;
        }

        string hostPart = scpLike[..pathSeparator].Trim().ToLowerInvariant();
        string? pathPart = NormalizeRemotePath(scpLike[(pathSeparator + 1)..]);
        if (hostPart.Length == 0 || pathPart is null)
        {
            identity = string.Empty;
            return false;
        }

        identity = $"{hostPart}/{pathPart}";
        return true;
    }

    private static string? NormalizeRemotePath(string value)
    {
        string normalized = value.Replace('\\', '/').Trim('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.Length == 0 ? null : normalized;
    }

    private static string ComputeRepositoryStateReportSha256(
        InspectionReport report,
        string repositoryIdentity)
    {
        RepositoryMetadata canonicalRepository = report.Repository with
        {
            Branch = null,
            RemoteUrl = repositoryIdentity
        };
        InspectionReport canonicalReport = report with { Repository = canonicalRepository };
        return ComputeSha256(InspectionJsonSerializer.Serialize(canonicalReport));
    }

    private static string BuildIdempotencyKey(
        InspectionSnapshotIdempotencyScope scope,
        string repositoryIdentity,
        string? commitSha,
        string inspectorVersion,
        string schemaVersion,
        string keyReportSha256,
        InspectionExecutionMetadata? execution,
        DateTimeOffset observedAtUtc)
    {
        string discriminator = scope == InspectionSnapshotIdempotencyScope.RepositoryState
            ? "repository-state"
            : BuildObservationDiscriminator(execution, observedAtUtc);

        string canonicalInput = string.Join(
            '\n',
            KeyVersion,
            scope.ToString(),
            repositoryIdentity,
            commitSha ?? string.Empty,
            inspectorVersion,
            schemaVersion,
            keyReportSha256,
            discriminator);

        return $"dri1:{ComputeSha256(canonicalInput)}";
    }

    private static string BuildObservationDiscriminator(
        InspectionExecutionMetadata? execution,
        DateTimeOffset observedAtUtc)
    {
        if (execution?.Id is not null)
        {
            return $"execution:{execution.Provider ?? "unknown"}:{execution.Id}";
        }

        return $"observed:{observedAtUtc.ToString("O", CultureInfo.InvariantCulture)}";
    }

    private static string ComputeSha256(string value)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
