namespace DotNetRepoInspector.Core.Classification;

public sealed record ProjectClassificationFacts(
    IReadOnlyList<string> DeclaredProjectSdks,
    string? OutputType,
    bool? IsTestProject)
{
    public bool? UsingMicrosoftNETSdkWorker
    {
        get;
        init;
    }

    public bool? IsTestingPlatformApplication
    {
        get;
        init;
    }

    public string? AzureFunctionsVersion
    {
        get;
        init;
    }

    public IReadOnlyList<string> PackageReferences
    {
        get;
        init;
    } = [];
}
