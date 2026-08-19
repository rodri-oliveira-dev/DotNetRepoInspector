using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetRepoInspector.Core.Contracts;

public static class InspectionJsonSerializer
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static string Serialize(InspectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateRequiredShape(report);
        ValidateVersion(report.SchemaVersion);

        return JsonSerializer.Serialize(Normalize(report), _jsonOptions);
    }

    public static InspectionReport Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var report = JsonSerializer.Deserialize<InspectionReport>(json, _jsonOptions)
            ?? throw new JsonException("The inspection payload is empty.");

        ValidateRequiredShape(report);
        ValidateVersion(report.SchemaVersion);

        return Normalize(report);
    }

    private static InspectionReport Normalize(InspectionReport report)
    {
        var normalizedProjects = report.Projects
            .Select(NormalizeProject)
            .OrderBy(project => project.Path, StringComparer.Ordinal)
            .ToArray();

        var normalizedSdk = report.DotNetSdk with
        {
            GlobalJsonPath = NormalizeOptionalPath(report.DotNetSdk.GlobalJsonPath)
        };

        return report with
        {
            DotNetSdk = normalizedSdk,
            Projects = normalizedProjects,
            Diagnostics = NormalizeDiagnostics(report.Diagnostics)
        };
    }

    private static ProjectInspection NormalizeProject(ProjectInspection project)
    {
        ValidateProjectShape(project);

        var normalizedClassification = project.Classification is null
            ? null
            : project.Classification with
            {
                Signals = project.Classification.Signals
                    .OrderBy(signal => signal, StringComparer.Ordinal)
                    .ToArray()
            };

        return project with
        {
            Path = NormalizePath(project.Path),
            Sdks = project.Sdks
                .OrderBy(sdk => sdk.Name, StringComparer.Ordinal)
                .ThenBy(sdk => sdk.Version ?? string.Empty, StringComparer.Ordinal)
                .ToArray(),
            TargetFrameworks = project.TargetFrameworks
                .OrderBy(framework => framework, StringComparer.Ordinal)
                .ToArray(),
            RuntimeIdentifiers = project.RuntimeIdentifiers
                .OrderBy(identifier => identifier, StringComparer.Ordinal)
                .ToArray(),
            Classification = normalizedClassification,
            References = project.References
                .Select(reference => reference with { Path = NormalizePath(reference.Path) })
                .OrderBy(reference => reference.Path, StringComparer.Ordinal)
                .ToArray(),
            Diagnostics = NormalizeDiagnostics(project.Diagnostics)
        };
    }

    private static InspectionDiagnostic[] NormalizeDiagnostics(
        IReadOnlyList<InspectionDiagnostic> diagnostics) =>
        diagnostics
            .Select(NormalizeDiagnostic)
            .OrderBy(diagnostic => diagnostic.Severity, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Source ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Details ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(diagnostic => DiagnosticContextSortKey(diagnostic.Context), StringComparer.Ordinal)
            .ToArray();

    private static InspectionDiagnostic NormalizeDiagnostic(InspectionDiagnostic diagnostic)
    {
        ValidateDiagnosticShape(diagnostic);

        IReadOnlyDictionary<string, string>? normalizedContext = null;
        if (diagnostic.Context is not null)
        {
            var context = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in diagnostic.Context)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                {
                    throw new JsonException(
                        "A diagnostic context contains an invalid key or value.");
                }

                context[pair.Key] = pair.Value;
            }

            normalizedContext = context;
        }

        return diagnostic with
        {
            Source = NormalizeOptionalPath(diagnostic.Source),
            Context = normalizedContext
        };
    }

    private static string DiagnosticContextSortKey(
        IReadOnlyDictionary<string, string>? context) =>
        context is null
            ? string.Empty
            : string.Join(
                "\u001f",
                context.Select(pair => $"{pair.Key}\u001e{pair.Value}"));

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    private static string? NormalizeOptionalPath(string? path) =>
        path is null
            ? null
            : NormalizePath(path);

    private static void ValidateVersion(string schemaVersion)
    {
        if (!InspectionSchema.IsCompatibleVersion(schemaVersion))
        {
            throw new NotSupportedException(
                $"Inspection schema version '{schemaVersion}' is not compatible with major version {InspectionSchema.CurrentMajorVersion}.");
        }
    }

    private static void ValidateRequiredShape(InspectionReport report)
    {
        if (report.Repository is null ||
            report.DotNetSdk is null ||
            report.Projects is null ||
            report.Diagnostics is null)
        {
            throw new JsonException(
                "The inspection payload is missing one or more required top-level properties.");
        }
    }

    private static void ValidateProjectShape(ProjectInspection project)
    {
        if (string.IsNullOrWhiteSpace(project.Path) ||
            project.Sdks is null ||
            project.TargetFrameworks is null ||
            project.RuntimeIdentifiers is null ||
            project.References is null ||
            project.Diagnostics is null ||
            (project.Classification is not null && project.Classification.Signals is null))
        {
            throw new JsonException(
                "A project entry is missing one or more required properties.");
        }
    }

    private static void ValidateDiagnosticShape(InspectionDiagnostic diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic.Code) ||
            !InspectionDiagnosticSeverity.IsDefined(diagnostic.Severity) ||
            string.IsNullOrWhiteSpace(diagnostic.Message))
        {
            throw new JsonException(
                "A diagnostic entry has an invalid code, severity, or message.");
        }
    }
}
