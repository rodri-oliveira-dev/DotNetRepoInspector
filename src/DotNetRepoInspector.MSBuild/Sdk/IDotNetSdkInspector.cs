namespace DotNetRepoInspector.MSBuild.Sdk;

public interface IDotNetSdkInspector
{
    Task<DotNetSdkInspectionResult> InspectAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);
}
