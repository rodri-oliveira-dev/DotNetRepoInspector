namespace DotNetRepoInspector.MSBuild.Evaluation;

public sealed record MsBuildProjectReference(
    string Include,
    string FullPath);
