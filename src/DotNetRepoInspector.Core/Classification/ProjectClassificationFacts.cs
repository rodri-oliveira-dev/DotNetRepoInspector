namespace DotNetRepoInspector.Core.Classification;

public sealed record ProjectClassificationFacts(
    IReadOnlyList<string> DeclaredProjectSdks,
    string? OutputType,
    bool? IsTestProject)
{
    public bool? IsTestingPlatformApplication { get; init; }

    public IReadOnlyList<string> PackageReferences { get; init; } = [];
}
