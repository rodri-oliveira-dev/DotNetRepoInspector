namespace DotNetRepoInspector.Core.Contracts;

public static class InspectionHealthEvaluator
{
    public static InspectionHealthSummary Evaluate(InspectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var repositoryDiagnostics = CountDiagnostics(report.Diagnostics);
        var projectDiagnostics = CountDiagnostics(
            report.Projects.SelectMany(static project => project.Diagnostics));

        var projectsWithDiagnostics = report.Projects.Count(
            static project => project.Diagnostics.Count > 0);
        var projectsWithWarnings = report.Projects.Count(
            static project => project.Diagnostics.Any(IsWarning));
        var projectsWithErrors = report.Projects.Count(
            static project => project.Diagnostics.Any(IsError));

        return new InspectionHealthSummary(
            GetStatus(repositoryDiagnostics, projectDiagnostics),
            GetStatus(repositoryDiagnostics),
            repositoryDiagnostics,
            projectDiagnostics,
            projectsWithDiagnostics,
            projectsWithWarnings,
            projectsWithErrors);
    }

    public static string GetProjectStatus(ProjectInspection project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return GetStatus(CountDiagnostics(project.Diagnostics));
    }

    private static InspectionDiagnosticCounts CountDiagnostics(
        IEnumerable<InspectionDiagnostic> diagnostics)
    {
        var total = 0;
        var info = 0;
        var warning = 0;
        var error = 0;

        foreach (var diagnostic in diagnostics)
        {
            total++;

            if (string.Equals(
                    diagnostic.Severity,
                    InspectionDiagnosticSeverity.Info,
                    StringComparison.Ordinal))
            {
                info++;
            }
            else if (IsWarning(diagnostic))
            {
                warning++;
            }
            else if (IsError(diagnostic))
            {
                error++;
            }
        }

        return new InspectionDiagnosticCounts(total, info, warning, error);
    }

    private static string GetStatus(
        InspectionDiagnosticCounts first,
        InspectionDiagnosticCounts? second = null)
    {
        var errorCount = first.Error + (second?.Error ?? 0);
        if (errorCount > 0)
        {
            return InspectionHealthStatus.Error;
        }

        var warningCount = first.Warning + (second?.Warning ?? 0);
        return warningCount > 0
            ? InspectionHealthStatus.Warning
            : InspectionHealthStatus.Ok;
    }

    private static bool IsWarning(InspectionDiagnostic diagnostic) =>
        string.Equals(
            diagnostic.Severity,
            InspectionDiagnosticSeverity.Warning,
            StringComparison.Ordinal);

    private static bool IsError(InspectionDiagnostic diagnostic) =>
        string.Equals(
            diagnostic.Severity,
            InspectionDiagnosticSeverity.Error,
            StringComparison.Ordinal);
}
