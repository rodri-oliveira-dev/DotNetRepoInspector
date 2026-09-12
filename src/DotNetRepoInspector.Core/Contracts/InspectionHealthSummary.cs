namespace DotNetRepoInspector.Core.Contracts;

public static class InspectionHealthStatus
{
    public const string Ok = "ok";
    public const string Warning = "warning";
    public const string Error = "error";
}

public sealed record InspectionDiagnosticCounts(
    int Total,
    int Info,
    int Warning,
    int Error);

public sealed record InspectionHealthSummary(
    string OverallStatus,
    string RepositoryStatus,
    InspectionDiagnosticCounts RepositoryDiagnostics,
    InspectionDiagnosticCounts ProjectDiagnostics,
    int ProjectsWithDiagnostics,
    int ProjectsWithWarnings,
    int ProjectsWithErrors);
