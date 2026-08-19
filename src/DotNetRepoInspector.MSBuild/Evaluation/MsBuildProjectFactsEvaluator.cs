using System.Xml;
using System.Xml.Linq;

namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed class MsBuildProjectFactsEvaluator : IMsBuildProjectFactsEvaluator
{
    private static readonly string[] EvaluatedPropertyNames =
    [
        "IsPackable",
        "IsTestProject",
        "OutputType",
        "RuntimeIdentifier",
        "RuntimeIdentifiers",
        "TargetFramework",
        "TargetFrameworks"
    ];

    private readonly IMsBuildProjectEvaluator _projectEvaluator;

    public MsBuildProjectFactsEvaluator()
        : this(new DotNetMsBuildProjectEvaluator())
    {
    }

    public MsBuildProjectFactsEvaluator(IMsBuildProjectEvaluator projectEvaluator)
    {
        ArgumentNullException.ThrowIfNull(projectEvaluator);
        _projectEvaluator = projectEvaluator;
    }

    public async Task<MsBuildProjectFactsResult> EvaluateAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var evaluation = await _projectEvaluator.EvaluateAsync(
            new MsBuildEvaluationRequest(projectPath, EvaluatedPropertyNames),
            cancellationToken);

        if (!evaluation.Succeeded)
        {
            var error = evaluation.Error ?? new MsBuildEvaluationError(
                MsBuildEvaluationErrorCode.InvalidMsBuildOutput,
                "MSBuild evaluation failed without an error description.");

            return MsBuildProjectFactsResult.Failure(projectPath, error);
        }

        if (string.IsNullOrWhiteSpace(evaluation.ResolvedSdkVersion))
        {
            return MsBuildProjectFactsResult.Failure(
                projectPath,
                new MsBuildEvaluationError(
                    MsBuildEvaluationErrorCode.InvalidMsBuildOutput,
                    "MSBuild evaluation succeeded without a resolved .NET SDK version."));
        }

        IReadOnlyList<ProjectSdkReference> projectSdks;
        try
        {
            projectSdks = ReadDeclaredProjectSdks(projectPath);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            XmlException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException)
        {
            return MsBuildProjectFactsResult.Failure(
                projectPath,
                new MsBuildEvaluationError(
                    MsBuildEvaluationErrorCode.ProjectFileReadFailed,
                    "The project SDK declaration could not be read.",
                    Details: exception.Message));
        }

        var properties = new Dictionary<string, string>(evaluation.Properties, StringComparer.Ordinal);
        var facts = new MsBuildProjectFacts(
            evaluation.ResolvedSdkVersion,
            projectSdks,
            NormalizeList(properties, "TargetFrameworks", "TargetFramework"),
            NormalizeScalar(properties, "OutputType"),
            NormalizeBoolean(properties, "IsTestProject"),
            NormalizeBoolean(properties, "IsPackable"),
            NormalizeList(properties, "RuntimeIdentifiers", "RuntimeIdentifier"),
            properties);

        return MsBuildProjectFactsResult.Success(projectPath, facts);
    }

    private static IReadOnlyList<ProjectSdkReference> ReadDeclaredProjectSdks(string projectPath)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var reader = XmlReader.Create(Path.GetFullPath(projectPath), settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root;

        if (root is null || !string.Equals(root.Name.LocalName, "Project", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The project file does not contain a root Project element.");
        }

        var sdkReferences = new List<ProjectSdkReference>();
        AddSdkAttributeReferences(root.Attribute("Sdk")?.Value, sdkReferences);

        foreach (var sdkElement in root.Elements().Where(element =>
                     string.Equals(element.Name.LocalName, "Sdk", StringComparison.Ordinal)))
        {
            var name = sdkElement.Attribute("Name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var version = NormalizeText(sdkElement.Attribute("Version")?.Value);
            sdkReferences.Add(new ProjectSdkReference(name, version));
        }

        return sdkReferences
            .Distinct()
            .ToArray();
    }

    private static void AddSdkAttributeReferences(
        string? sdkAttribute,
        ICollection<ProjectSdkReference> sdkReferences)
    {
        if (string.IsNullOrWhiteSpace(sdkAttribute))
        {
            return;
        }

        foreach (var sdkReference in sdkAttribute.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = sdkReference.LastIndexOf('/');
            if (separatorIndex <= 0 || separatorIndex == sdkReference.Length - 1)
            {
                sdkReferences.Add(new ProjectSdkReference(sdkReference));
                continue;
            }

            sdkReferences.Add(new ProjectSdkReference(
                sdkReference[..separatorIndex],
                sdkReference[(separatorIndex + 1)..]));
        }
    }

    private static IReadOnlyList<string> NormalizeList(
        IReadOnlyDictionary<string, string> properties,
        string pluralProperty,
        string singularProperty)
    {
        return GetListValues(properties, pluralProperty)
            .Concat(GetListValues(properties, singularProperty))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> GetListValues(
        IReadOnlyDictionary<string, string> properties,
        string propertyName)
    {
        var value = GetPropertyValue(properties, propertyName);
        return value is null
            ? []
            : value.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? NormalizeScalar(
        IReadOnlyDictionary<string, string> properties,
        string propertyName) =>
        NormalizeText(GetPropertyValue(properties, propertyName));

    private static bool? NormalizeBoolean(
        IReadOnlyDictionary<string, string> properties,
        string propertyName)
    {
        var value = NormalizeScalar(properties, propertyName);
        return bool.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private static string? GetPropertyValue(
        IReadOnlyDictionary<string, string> properties,
        string propertyName) =>
        properties.TryGetValue(propertyName, out var value)
            ? value
            : null;

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
