namespace DotNetRepoInspector.Core.Contracts;

public static class InspectionDiagnostics
{
    public static InspectionDiagnostic InvalidProject(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.InvalidProject,
            InspectionDiagnosticSeverity.Error,
            "The project could not be inspected.",
            source,
            context);

    public static InspectionDiagnostic DotNetSdkUnavailable(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.DotNetSdkUnavailable,
            InspectionDiagnosticSeverity.Error,
            "The required .NET SDK could not be resolved.",
            source,
            context);

    public static InspectionDiagnostic ProjectReferenceUnresolved(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.ProjectReferenceUnresolved,
            InspectionDiagnosticSeverity.Warning,
            "A project reference could not be resolved.",
            source,
            context);

    public static InspectionDiagnostic PropertyNotEvaluable(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.PropertyNotEvaluable,
            InspectionDiagnosticSeverity.Warning,
            "An expected project property could not be evaluated.",
            source,
            context);

    public static InspectionDiagnostic GlobalJsonInvalid(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.GlobalJsonInvalid,
            InspectionDiagnosticSeverity.Error,
            "The applicable global.json is invalid.",
            source,
            context);

    public static InspectionDiagnostic MsBuildEvaluationFailed(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.MsBuildEvaluationFailed,
            InspectionDiagnosticSeverity.Error,
            "MSBuild could not evaluate the project.",
            source,
            context);

    public static InspectionDiagnostic InvalidMsBuildOutput(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.InvalidMsBuildOutput,
            InspectionDiagnosticSeverity.Error,
            "MSBuild returned an invalid structured result.",
            source,
            context);

    public static InspectionDiagnostic DotNetHostUnavailable(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.DotNetHostUnavailable,
            InspectionDiagnosticSeverity.Error,
            "The .NET host is unavailable.",
            source,
            context);

    public static InspectionDiagnostic InvalidInspectionRequest(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.InvalidInspectionRequest,
            InspectionDiagnosticSeverity.Error,
            "The inspection request is invalid.",
            source,
            context);

    public static InspectionDiagnostic RepositoryRootUnavailable(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.RepositoryRootUnavailable,
            InspectionDiagnosticSeverity.Error,
            "The repository root is unavailable.",
            source,
            context);

    public static InspectionDiagnostic GlobalJsonReadFailed(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.GlobalJsonReadFailed,
            InspectionDiagnosticSeverity.Error,
            "The applicable global.json could not be read.",
            source,
            context);

    public static InspectionDiagnostic RepositoryMetadataUnavailable(
        string? source = null,
        IReadOnlyDictionary<string, string>? context = null) =>
        Create(
            InspectionDiagnosticCodes.RepositoryMetadataUnavailable,
            InspectionDiagnosticSeverity.Warning,
            "Repository metadata could not be fully collected.",
            source,
            context);

    private static InspectionDiagnostic Create(
        string code,
        string severity,
        string message,
        string? source,
        IReadOnlyDictionary<string, string>? context) =>
        new(code, severity, message, source, null, context);
}
