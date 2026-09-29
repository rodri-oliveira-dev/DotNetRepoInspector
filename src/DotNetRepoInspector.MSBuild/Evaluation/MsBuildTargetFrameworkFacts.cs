namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildTargetFrameworkFacts(
    string TargetFramework,
    string? OutputType,
    bool? IsTestProject,
    bool? IsTestingPlatformApplication,
    bool? UsingMicrosoftNETSdkWorker,
    IReadOnlyList<string> PackageReferences)
{
    public string? AzureFunctionsVersion
    {
        get;
        init;
    }
}
