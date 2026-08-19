namespace DotNetRepoInspector.MSBuild.Evaluation;

public interface IMsBuildProjectEvaluator
{
    Task<MsBuildEvaluationResult> EvaluateAsync(
        MsBuildEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
