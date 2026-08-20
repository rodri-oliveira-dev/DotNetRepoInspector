using System.Text.Json;
using System.Text.Json.Serialization;

using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Persistence;

public static class InspectionSnapshotJsonSerializer
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public static string Serialize(InspectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string reportJson = InspectionJsonSerializer.Serialize(snapshot.Report);
        using JsonDocument reportDocument = JsonDocument.Parse(reportJson);

        var payload = new SnapshotPayload(
            snapshot.SchemaVersion,
            snapshot.InspectorVersion,
            snapshot.RepositoryIdentity,
            snapshot.CommitSha,
            snapshot.Ref,
            snapshot.ObservedAtUtc,
            snapshot.Execution,
            snapshot.ReportSha256,
            snapshot.IdempotencyKey,
            snapshot.IdempotencyScope,
            reportDocument.RootElement.Clone());

        return JsonSerializer.Serialize(payload, _jsonOptions);
    }

    private sealed record SnapshotPayload(
        string SchemaVersion,
        string InspectorVersion,
        string RepositoryIdentity,
        string? CommitSha,
        string? Ref,
        DateTimeOffset ObservedAtUtc,
        InspectionExecutionMetadata? Execution,
        string ReportSha256,
        string IdempotencyKey,
        InspectionSnapshotIdempotencyScope IdempotencyScope,
        JsonElement Report);
}
