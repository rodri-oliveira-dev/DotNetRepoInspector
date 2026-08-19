namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildEvaluationResult(
    bool Succeeded,
    string? ResolvedSdkVersion,
    IReadOnlyDictionary<string, string> Properties,
    MsBuildEvaluationError? Error)
{
    public static MsBuildEvaluationResult Success(
        string resolvedSdkVersion,
        IReadOnlyDictionary<string, string> properties) =>
        new(true, resolvedSdkVersion, properties, null);

    public static MsBuildEvaluationResult Failure(MsBuildEvaluationError error) =>
        new(false, null, new Dictionary<string, string>(StringComparer.Ordinal), error);
}
