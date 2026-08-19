namespace DotNetRepoInspector.MSBuild.Evaluation;

public enum MsBuildEvaluationErrorCode
{
    InvalidRequest,
    ProjectNotFound,
    ProjectFileReadFailed,
    DotNetHostNotFound,
    SdkResolutionFailed,
    MsBuildEvaluationFailed,
    InvalidMsBuildOutput
}
