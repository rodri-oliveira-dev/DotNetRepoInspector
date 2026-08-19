namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildProjectFactsResult(
    string ProjectPath,
    MsBuildProjectFacts? Facts,
    MsBuildEvaluationError? Error)
{
    public bool Succeeded => Facts is not null && Error is null;

    public static MsBuildProjectFactsResult Success(
        string projectPath,
        MsBuildProjectFacts facts) =>
        new(projectPath, facts, null);

    public static MsBuildProjectFactsResult Failure(
        string projectPath,
        MsBuildEvaluationError error) =>
        new(projectPath, null, error);
}
