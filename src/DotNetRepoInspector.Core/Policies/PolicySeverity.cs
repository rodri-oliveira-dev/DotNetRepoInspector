namespace DotNetRepoInspector.Core.Policies;

public static class PolicySeverity
{
    public const string Warning = "warning";
    public const string Error = "error";

    public static bool IsDefined(string? severity) =>
        string.Equals(severity, Warning, StringComparison.Ordinal) ||
        string.Equals(severity, Error, StringComparison.Ordinal);
}
