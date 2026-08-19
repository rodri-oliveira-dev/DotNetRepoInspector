namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildEvaluationItem(
    string Identity,
    IReadOnlyDictionary<string, string> Metadata);
