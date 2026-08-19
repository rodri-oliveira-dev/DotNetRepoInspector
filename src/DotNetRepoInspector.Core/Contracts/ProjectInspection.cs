namespace DotNetRepoInspector.Core.Contracts;

public sealed record ProjectInspection(
    string Path,
    string? Name,
    string? ResolvedSdkVersion,
    IReadOnlyList<ProjectSdkMetadata> Sdks,
    IReadOnlyList<string> TargetFrameworks,
    string? OutputType,
    bool? IsTestProject,
    bool? IsPackable,
    IReadOnlyList<string> RuntimeIdentifiers,
    ProjectClassification? Classification,
    IReadOnlyList<ProjectReferenceMetadata> References,
    IReadOnlyList<InspectionDiagnostic> Diagnostics);
