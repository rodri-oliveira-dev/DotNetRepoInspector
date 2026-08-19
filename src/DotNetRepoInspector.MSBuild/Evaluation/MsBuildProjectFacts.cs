namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildProjectFacts(
    string ResolvedSdkVersion,
    IReadOnlyList<ProjectSdkReference> DeclaredProjectSdks,
    IReadOnlyList<string> TargetFrameworks,
    string? OutputType,
    bool? IsTestProject,
    bool? IsPackable,
    IReadOnlyList<string> RuntimeIdentifiers,
    IReadOnlyDictionary<string, string> Properties);
