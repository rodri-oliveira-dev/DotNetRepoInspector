namespace DotNetRepoInspector.Core.Contracts;

public static class InspectionDiagnosticSeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";

    public static bool IsDefined(string? severity) =>
        string.Equals(severity, Info, StringComparison.Ordinal) ||
        string.Equals(severity, Warning, StringComparison.Ordinal) ||
        string.Equals(severity, Error, StringComparison.Ordinal);
}
