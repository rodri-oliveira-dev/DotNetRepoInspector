namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildEvaluationResult(
    bool Succeeded,
    string? ResolvedSdkVersion,
    IReadOnlyDictionary<string, string> Properties,
    MsBuildEvaluationError? Error)
{
    public IReadOnlyDictionary<
        string,
        IReadOnlyList<MsBuildEvaluationItem>> Items
    {
        get; init;
    } = new Dictionary<string, IReadOnlyList<MsBuildEvaluationItem>>(StringComparer.Ordinal);

    public static MsBuildEvaluationResult Success(
        string resolvedSdkVersion,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyDictionary<string, IReadOnlyList<MsBuildEvaluationItem>>? items = null) =>
        new(true, resolvedSdkVersion, properties, null)
        {
            Items = items ?? new Dictionary<string, IReadOnlyList<MsBuildEvaluationItem>>(StringComparer.Ordinal)
        };

    public static MsBuildEvaluationResult Failure(MsBuildEvaluationError error) =>
        new(false, null, new Dictionary<string, string>(StringComparer.Ordinal), error);
}
