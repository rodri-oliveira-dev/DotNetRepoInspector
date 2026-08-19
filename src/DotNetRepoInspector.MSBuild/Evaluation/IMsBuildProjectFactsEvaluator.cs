namespace DotNetRepoInspector.MSBuild.Evaluation;

public interface IMsBuildProjectFactsEvaluator
{
    Task<MsBuildProjectFactsResult> EvaluateAsync(
        string projectPath,
        CancellationToken cancellationToken = default);
}
