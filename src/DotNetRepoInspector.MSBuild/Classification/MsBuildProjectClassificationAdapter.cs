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

        var declaredSdks = facts.DeclaredProjectSdks
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Name))
            .Select(reference => reference.Name)
            .ToArray();

        return _classifier.Classify(new ProjectClassificationFacts(
            declaredSdks,
            facts.OutputType,
            facts.IsTestProject));
    }
}
