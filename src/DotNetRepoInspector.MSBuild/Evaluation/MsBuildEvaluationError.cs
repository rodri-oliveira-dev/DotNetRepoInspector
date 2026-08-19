namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildEvaluationError(
    MsBuildEvaluationErrorCode Code,
    string Message,
    int? ExitCode = null,
    string? Details = null);
