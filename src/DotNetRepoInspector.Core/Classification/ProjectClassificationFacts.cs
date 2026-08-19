namespace DotNetRepoInspector.Core.Classification;

public sealed record ProjectClassificationFacts(
    IReadOnlyList<string> DeclaredProjectSdks,
    string? OutputType,
    bool? IsTestProject);
