using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Mcp;

public sealed record ListProjectsResponse(
    string McpSchemaVersion,
    bool Ok,
    ListProjectsData? Data,
    McpToolError? Error);

public sealed record ListProjectsData(
    string InspectionSchemaVersion,
    IReadOnlyList<ProjectSummary> Projects);

public sealed record ProjectSummary(
    string Path,
    string? Name,
    IReadOnlyList<string> TargetFrameworks,
    ProjectClassification? Classification,
    int DiagnosticCount,
    int ErrorCount,
    int WarningCount);

public sealed record GetProjectDetailsResponse(
    string McpSchemaVersion,
    bool Ok,
    GetProjectDetailsData? Data,
    McpToolError? Error);

public sealed record GetProjectDetailsData(
    string InspectionSchemaVersion,
    ProjectInspection Project);

public sealed record GetProjectReferenceGraphResponse(
    string McpSchemaVersion,
    bool Ok,
    GetProjectReferenceGraphData? Data,
    McpToolError? Error);

public sealed record GetProjectReferenceGraphData(
    string InspectionSchemaVersion,
    IReadOnlyList<ProjectReferenceGraphEntry> Projects);

public sealed record ProjectReferenceGraphEntry(
    string Path,
    IReadOnlyList<ProjectReferenceMetadata> References,
    IReadOnlyList<InspectionDiagnostic> Diagnostics);

public sealed record GetRepositoryDiagnosticsResponse(
    string McpSchemaVersion,
    bool Ok,
    GetRepositoryDiagnosticsData? Data,
    McpToolError? Error);

public sealed record GetRepositoryDiagnosticsData(
    string InspectionSchemaVersion,
    IReadOnlyList<ContextualDiagnostic> Diagnostics);

public sealed record ContextualDiagnostic(
    string? ProjectPath,
    InspectionDiagnostic Diagnostic);

public sealed record GetSdkMetadataResponse(
    string McpSchemaVersion,
    bool Ok,
    GetSdkMetadataData? Data,
    McpToolError? Error);

public sealed record GetSdkMetadataData(
    string InspectionSchemaVersion,
    DotNetSdkMetadata DotNetSdk);
