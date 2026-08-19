namespace DotNetRepoInspector.MSBuild.Sdk;

public enum DotNetSdkInspectionErrorCode
{
    InvalidRequest,
    RepositoryRootNotFound,
    GlobalJsonReadFailed,
    GlobalJsonInvalid,
    DotNetHostNotFound,
    SdkResolutionFailed
}
