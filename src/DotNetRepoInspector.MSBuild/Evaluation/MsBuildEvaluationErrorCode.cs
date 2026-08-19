namespace DotNetRepoInspector.MSBuild.Evaluation;

public enum MsBuildEvaluationErrorCode
{
    InvalidRequest,
    ProjectNotFound,
    DotNetHostNotFound,
    SdkResolutionFailed,
    MsBuildEvaluationFailed,
    InvalidMsBuildOutput
}
