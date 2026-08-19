namespace DotNetRepoInspector.MSBuild.Sdk;

public sealed record DotNetSdkInspectionResult(
    bool Succeeded,
    string RepositoryRoot,
    string? GlobalJsonPath,
    DotNetSdkConfiguration? Configuration,
    string? ResolvedSdkVersion,
    DotNetSdkInspectionError? Error)
{
    public static DotNetSdkInspectionResult Success(
        string repositoryRoot,
        string? globalJsonPath,
        DotNetSdkConfiguration? configuration,
        string resolvedSdkVersion) =>
        new(true, repositoryRoot, globalJsonPath, configuration, resolvedSdkVersion, null);

    public static DotNetSdkInspectionResult Failure(
        string repositoryRoot,
        DotNetSdkInspectionError error,
        string? globalJsonPath = null,
        DotNetSdkConfiguration? configuration = null) =>
        new(false, repositoryRoot, globalJsonPath, configuration, null, error);
}
