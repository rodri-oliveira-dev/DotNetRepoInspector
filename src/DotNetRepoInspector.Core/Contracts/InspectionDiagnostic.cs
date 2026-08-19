namespace DotNetRepoInspector.Core.Contracts;

public sealed record InspectionDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? Source,
    string? Details,
    IReadOnlyDictionary<string, string>? Context = null);
