using System.Text.Json;
using System.Text.Json.Serialization;

using DotNetRepoInspector.Core.Policies;

namespace DotNetRepoInspector.Core.Contracts;

public static class InspectionJsonSerializer
{
    private const string RedactedValue = "<redacted>";

    private static readonly string[] SensitiveContextKeyFragments =
    [
        "authorization",
        "connectionstring",
        "connection_string",
        "credential",
        "password",
        "privatekey",
        "private_key",
        "secret",
        "token",
        "apikey",
        "api_key",
        "accesskey",
        "access_key"
    ];

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

        return NormalizeJsonNewLines(JsonSerializer.Serialize(Normalize(report), _jsonOptions));
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
            Diagnostics = NormalizeDiagnostics(report.Diagnostics),
            PolicyFindings = NormalizePolicyFindings(report.PolicyFindings),
            Integrations = NormalizeIntegrations(report.Integrations)
        };
    }

    private static IntegrationFinding[] NormalizeIntegrations(
        IReadOnlyList<IntegrationFinding> findings) =>
        findings
            .Select(NormalizeIntegration)
            .OrderBy(static finding => finding.ProjectPath, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Source.Path, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Source.Line)
            .ThenBy(static finding => finding.Kind, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Direction, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Technology, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Target ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Id, StringComparer.Ordinal)
            .ToArray();

    private static IntegrationFinding NormalizeIntegration(IntegrationFinding finding)
    {
        if (finding is null ||
            !IsDeterministicIntegrationId(finding.Id) ||
            !IsSafeRelativePath(finding.ProjectPath) ||
            !IntegrationKind.IsDefined(finding.Kind) ||
            !IntegrationDirection.IsDefined(finding.Direction) ||
            !IsSafeSlug(finding.Technology) ||
            !IsSafeOptionalIdentifier(finding.Target) ||
            !IsSafeOptionalSlug(finding.ResourceType) ||
            !IsSafeOptionalConfigurationKey(finding.ConfigurationKey) ||
            !IsSafeOptionalIdentifier(finding.Contract) ||
            finding.Source is null ||
            !IsSafeRelativePath(finding.Source.Path) ||
            finding.Source.Line < 1 ||
            !IntegrationConfidence.IsDefined(finding.Confidence) ||
            finding.Signals is null ||
            finding.Signals.Count == 0 ||
            finding.Signals.Any(signal => !IsSafeSignal(signal)))
        {
            throw new JsonException("An integration finding contains invalid or unsafe evidence.");
        }

        var normalized = finding with
        {
            ProjectPath = NormalizePath(finding.ProjectPath),
            Source = finding.Source with
            {
                Path = NormalizePath(finding.Source.Path)
            },
            Signals = finding.Signals
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static signal => signal, StringComparer.Ordinal)
                .ToArray()
        };

        string expectedId = IntegrationFinding.Create(
            normalized.ProjectPath,
            normalized.Kind,
            normalized.Direction,
            normalized.Technology,
            normalized.Source,
            normalized.Confidence,
            normalized.Signals,
            normalized.Target,
            normalized.ResourceType,
            normalized.ConfigurationKey,
            normalized.Contract).Id;

        if (!string.Equals(normalized.Id, expectedId, StringComparison.Ordinal))
        {
            throw new JsonException("An integration finding id does not match its canonical evidence.");
        }

        return normalized;
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
            .ThenBy(diagnostic => ContextSortKey(diagnostic.Context), StringComparer.Ordinal)
            .ToArray();

    private static PolicyFinding[] NormalizePolicyFindings(
        IReadOnlyList<PolicyFinding> findings) =>
        findings
            .Select(NormalizePolicyFinding)
            .OrderBy(static finding => finding.RuleCode, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Severity, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Scope.Kind, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Scope.ProjectPath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Message, StringComparer.Ordinal)
            .ThenBy(static finding => ContextSortKey(finding.Context), StringComparer.Ordinal)
            .ToArray();

    private static PolicyFinding NormalizePolicyFinding(PolicyFinding finding)
    {
        if (finding is null ||
            !IsValidPolicyRuleCode(finding.RuleCode) ||
            !PolicySeverity.IsDefined(finding.Severity) ||
            string.IsNullOrWhiteSpace(finding.Message) ||
            finding.Scope is null)
        {
            throw new JsonException(
                "A policy finding has an invalid rule code, severity, message, or scope.");
        }

        PolicyFindingScope normalizedScope;
        if (string.Equals(
                finding.Scope.Kind,
                PolicyFindingScope.RepositoryKind,
                StringComparison.Ordinal) &&
            finding.Scope.ProjectPath is null)
        {
            normalizedScope = PolicyFindingScope.Repository;
        }
        else if (string.Equals(
                     finding.Scope.Kind,
                     PolicyFindingScope.ProjectKind,
                     StringComparison.Ordinal) &&
                 !string.IsNullOrWhiteSpace(finding.Scope.ProjectPath))
        {
            normalizedScope = PolicyFindingScope.Project(
                NormalizePath(finding.Scope.ProjectPath));
        }
        else
        {
            throw new JsonException("A policy finding contains an invalid scope.");
        }

        IReadOnlyDictionary<string, string>? normalizedContext = null;
        if (finding.Context is not null)
        {
            var context = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in finding.Context)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                {
                    throw new JsonException(
                        "A policy finding context contains an invalid key or value.");
                }

                context[pair.Key] = IsSensitiveContextKey(pair.Key)
                    ? RedactedValue
                    : pair.Value;
            }

            normalizedContext = context;
        }

        return new PolicyFinding(
            finding.RuleCode,
            finding.Severity,
            finding.Message,
            normalizedScope,
            normalizedContext);
    }

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

                context[pair.Key] = IsSensitiveContextKey(pair.Key)
                    ? RedactedValue
                    : pair.Value;
            }

            normalizedContext = context;
        }

        return diagnostic with
        {
            Source = NormalizeOptionalPath(diagnostic.Source),
            Context = normalizedContext
        };
    }

    private static bool IsSensitiveContextKey(string key) =>
        SensitiveContextKeyFragments.Any(fragment =>
            key.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string ContextSortKey(
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

    private static string NormalizeJsonNewLines(string json) =>
        json
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

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
            report.Diagnostics is null ||
            report.PolicyFindings is null ||
            report.Integrations is null)
        {
            throw new JsonException(
                "The inspection payload is missing one or more required top-level properties.");
        }
    }

    private static bool IsDeterministicIntegrationId(string? id)
    {
        if (id is not { Length: 28 } ||
            !id.StartsWith("integration-", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 12; index < id.Length; index++)
        {
            if (id[index] is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeRelativePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !Path.IsPathRooted(path) &&
        !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..") &&
        IsSafeEvidence(path, 1024);

    private static bool IsSafeOptionalIdentifier(string? value) =>
        value is null ||
        IsSafeRestrictedEvidence(
            value,
            static character =>
                char.IsLetterOrDigit(character) ||
                character is '.' or '_' or '-' or '/' or ':' or '+' or '<' or '>' or '[' or ']');

    private static bool IsSafeOptionalSlug(string? value) =>
        value is null || IsSafeSlug(value);

    private static bool IsSafeSlug(string? value) =>
        IsSafeRestrictedEvidence(
            value,
            static character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool IsSafeSignal(string? value) =>
        IsSafeRestrictedEvidence(
            value,
            static character =>
                char.IsAsciiLetterOrDigit(character) || character is ':' or '.' or '_' or '-');

    private static bool IsSafeOptionalConfigurationKey(string? value) =>
        value is null ||
        IsSafeRestrictedEvidence(
            value,
            static character =>
                char.IsLetterOrDigit(character) || character is ':' or '.' or '_' or '-' or '[' or ']');

    private static bool IsSafeRestrictedEvidence(
        string? value,
        Func<char, bool> isAllowedCharacter) =>
        value is not null &&
        IsSafeEvidence(value) &&
        value.All(isAllowedCharacter);

    private static bool IsSafeEvidence(string? value, int maximumLength = 256) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Contains('\r') &&
        !value.Contains('\n') &&
        !value.Contains('\0');

    private static void ValidateProjectShape(ProjectInspection project)
    {
        if (string.IsNullOrWhiteSpace(project.Path) ||
            project.Sdks is null ||
            project.TargetFrameworks is null ||
            project.RuntimeIdentifiers is null ||
            project.References is null ||
            project.Diagnostics is null ||
            !IsValidClassification(project.Classification))
        {
            throw new JsonException(
                "A project entry is missing one or more required properties.");
        }
    }

    private static bool IsValidClassification(ProjectClassification? classification)
    {
        if (classification is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(classification.Kind) || classification.Signals is null)
        {
            return false;
        }

        if (classification.Subtype is not null && string.IsNullOrWhiteSpace(classification.Subtype))
        {
            return false;
        }

        if (classification.Source is null)
        {
            return classification.AutomaticKind is null;
        }

        return classification.Source is "configuration" or "request" &&
               !string.IsNullOrWhiteSpace(classification.AutomaticKind);
    }

    private static void ValidateDiagnosticShape(InspectionDiagnostic diagnostic)
    {
        if (!IsValidDiagnosticCode(diagnostic.Code) ||
            !InspectionDiagnosticSeverity.IsDefined(diagnostic.Severity) ||
            string.IsNullOrWhiteSpace(diagnostic.Message))
        {
            throw new JsonException(
                "A diagnostic entry has an invalid code, severity, or message.");
        }
    }

    private static bool IsValidPolicyRuleCode(string? code)
    {
        if (code is null ||
            code.Length != 7 ||
            !code.StartsWith("DRP", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 3; index < code.Length; index++)
        {
            if (code[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidDiagnosticCode(string? code)
    {
        if (code is null ||
            code.Length != 7 ||
            !code.StartsWith("DRI", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 3; index < code.Length; index++)
        {
            if (code[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
