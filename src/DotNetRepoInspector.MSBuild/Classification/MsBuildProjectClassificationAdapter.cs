using DotNetRepoInspector.Core.Classification;
using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Evaluation;

namespace DotNetRepoInspector.MSBuild.Classification;

public sealed class MsBuildProjectClassificationAdapter
{
    private readonly IProjectClassifier _classifier;

    public MsBuildProjectClassificationAdapter()
        : this(new DeterministicProjectClassifier())
    {
    }

    public MsBuildProjectClassificationAdapter(IProjectClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        _classifier = classifier;
    }

    public ProjectClassification Classify(MsBuildProjectFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(facts.DeclaredProjectSdks);

        var classificationFacts = MergeTargetFrameworkFacts(facts);

        var declaredSdks = facts.DeclaredProjectSdks
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Name))
            .Select(reference => reference.Name)
            .ToArray();

        return _classifier.Classify(new ProjectClassificationFacts(
            declaredSdks,
            classificationFacts.OutputType,
            classificationFacts.IsTestProject)
        {
            UsingMicrosoftNETSdkWorker = classificationFacts.UsingMicrosoftNETSdkWorker,
            IsTestingPlatformApplication = classificationFacts.IsTestingPlatformApplication,
            PackageReferences = classificationFacts.PackageReferences
        });
    }

    private static MergedClassificationFacts MergeTargetFrameworkFacts(MsBuildProjectFacts facts)
    {
        if (facts.TargetFrameworkFacts.Count == 0)
        {
            return new MergedClassificationFacts(
                facts.OutputType,
                facts.IsTestProject,
                facts.IsTestingPlatformApplication,
                facts.UsingMicrosoftNETSdkWorker,
                facts.PackageReferences);
        }

        var outputType = facts.TargetFrameworkFacts
            .Select(target => target.OutputType)
            .FirstOrDefault(value => string.Equals(value, "Exe", StringComparison.OrdinalIgnoreCase)) ??
            facts.OutputType;
        var packageReferences = facts.PackageReferences
            .Where(package =>
                !IsServiceLifetimePackage(package) ||
                facts.TargetFrameworkFacts.Any(target =>
                    string.Equals(target.OutputType, "Exe", StringComparison.OrdinalIgnoreCase) &&
                    target.PackageReferences.Contains(package, StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        return new MergedClassificationFacts(
            outputType,
            MergeBoolean(facts.IsTestProject, facts.TargetFrameworkFacts.Select(target => target.IsTestProject)),
            MergeBoolean(
                facts.IsTestingPlatformApplication,
                facts.TargetFrameworkFacts.Select(target => target.IsTestingPlatformApplication)),
            MergeBoolean(
                facts.UsingMicrosoftNETSdkWorker,
                facts.TargetFrameworkFacts.Select(target => target.UsingMicrosoftNETSdkWorker)),
            packageReferences);
    }

    private static bool? MergeBoolean(bool? outerValue, IEnumerable<bool?> innerValues) =>
        innerValues.Any(value => value is true)
            ? true
            : outerValue;

    private static bool IsServiceLifetimePackage(string packageReference) =>
        string.Equals(
            packageReference,
            DeterministicProjectClassifier.MicrosoftExtensionsHostingSystemdPackage,
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            packageReference,
            DeterministicProjectClassifier.MicrosoftExtensionsHostingWindowsServicesPackage,
            StringComparison.OrdinalIgnoreCase);

    private sealed record MergedClassificationFacts(
        string? OutputType,
        bool? IsTestProject,
        bool? IsTestingPlatformApplication,
        bool? UsingMicrosoftNETSdkWorker,
        IReadOnlyList<string> PackageReferences);
}
