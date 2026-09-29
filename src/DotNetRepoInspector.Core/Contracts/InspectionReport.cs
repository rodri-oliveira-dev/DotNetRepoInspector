using DotNetRepoInspector.Core.Policies;

namespace DotNetRepoInspector.Core.Contracts;

public sealed record InspectionReport(
    string SchemaVersion,
    RepositoryMetadata Repository,
    DotNetSdkMetadata DotNetSdk,
    IReadOnlyList<ProjectInspection> Projects,
    IReadOnlyList<InspectionDiagnostic> Diagnostics)
{
    public IReadOnlyList<PolicyFinding> PolicyFindings
    {
        get;
        init;
    } = Array.Empty<PolicyFinding>();

    public static InspectionReport Create(
        RepositoryMetadata repository,
        DotNetSdkMetadata dotNetSdk,
        IReadOnlyList<ProjectInspection> projects,
        IReadOnlyList<InspectionDiagnostic> diagnostics) =>
        new(
            InspectionSchema.CurrentVersion,
            repository,
            dotNetSdk,
            projects,
            diagnostics);
}
