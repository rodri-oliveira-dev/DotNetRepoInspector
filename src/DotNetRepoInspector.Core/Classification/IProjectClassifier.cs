using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Core.Classification;

public interface IProjectClassifier
{
    ProjectClassification Classify(ProjectClassificationFacts facts);
}
