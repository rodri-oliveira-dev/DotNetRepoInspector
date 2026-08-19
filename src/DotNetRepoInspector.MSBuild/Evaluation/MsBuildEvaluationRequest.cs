namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildEvaluationRequest(
    string ProjectPath,
    IReadOnlyCollection<string> Properties,
    IReadOnlyCollection<string>? Items = null);
