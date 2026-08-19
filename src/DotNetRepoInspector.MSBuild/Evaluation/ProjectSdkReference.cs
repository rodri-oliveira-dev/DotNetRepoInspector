namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record ProjectSdkReference(
    string Name,
    string? Version = null);
