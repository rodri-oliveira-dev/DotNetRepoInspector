using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Core.Classification;

public sealed class DeterministicProjectClassifier : IProjectClassifier
{
    public const string WebSdk = "Microsoft.NET.Sdk.Web";
    public const string WorkerSdk = "Microsoft.NET.Sdk.Worker";

    private static readonly IReadOnlyList<string> TestSignals =
        Array.AsReadOnly(new[] { "property:IsTestProject=true" });

    private static readonly IReadOnlyList<string> WebSignals =
        Array.AsReadOnly(new[] { $"sdk:{WebSdk}" });

    private static readonly IReadOnlyList<string> WorkerSignals =
        Array.AsReadOnly(new[] { $"sdk:{WorkerSdk}" });

    private static readonly IReadOnlyList<string> ConsoleSignals =
        Array.AsReadOnly(new[] { "property:OutputType=Exe" });

    private static readonly IReadOnlyList<string> LibrarySignals =
        Array.AsReadOnly(new[] { "property:OutputType=Library" });

    private static readonly IReadOnlyList<string> SpecializedSdkConflictSignals =
        Array.AsReadOnly(new[]
        {
            $"sdk:{WebSdk}",
            $"sdk:{WorkerSdk}",
            "conflict:specialized-sdk"
        });

    public ProjectClassification Classify(ProjectClassificationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(facts.DeclaredProjectSdks);

        var declaredSdks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sdk in facts.DeclaredProjectSdks)
        {
            if (!string.IsNullOrWhiteSpace(sdk))
            {
                declaredSdks.Add(sdk.Trim());
            }
        }

        var hasWebSdk = declaredSdks.Contains(WebSdk);
        var hasWorkerSdk = declaredSdks.Contains(WorkerSdk);

        if (facts.IsTestProject is true)
        {
            return new ProjectClassification(
                ProjectClassificationKinds.Test,
                ProjectClassificationConfidence.High,
                TestSignals);
        }

        if (hasWebSdk && hasWorkerSdk)
        {
            return new ProjectClassification(
                ProjectClassificationKinds.Unknown,
                null,
                SpecializedSdkConflictSignals);
        }

        if (hasWebSdk)
        {
            return new ProjectClassification(
                ProjectClassificationKinds.Web,
                ProjectClassificationConfidence.High,
                WebSignals);
        }

        if (hasWorkerSdk)
        {
            return new ProjectClassification(
                ProjectClassificationKinds.Worker,
                ProjectClassificationConfidence.High,
                WorkerSignals);
        }

        var outputType = Normalize(facts.OutputType);
        if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectClassification(
                ProjectClassificationKinds.Console,
                ProjectClassificationConfidence.Medium,
                ConsoleSignals);
        }

        if (string.Equals(outputType, "Library", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectClassification(
                ProjectClassificationKinds.Library,
                ProjectClassificationConfidence.High,
                LibrarySignals);
        }

        IReadOnlyList<string> signals = outputType is null
            ? Array.Empty<string>()
            : new List<string> { $"property:OutputType={outputType}" };

        return new ProjectClassification(
            ProjectClassificationKinds.Unknown,
            null,
            signals);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
