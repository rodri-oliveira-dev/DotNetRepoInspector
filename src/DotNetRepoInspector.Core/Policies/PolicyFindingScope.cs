namespace DotNetRepoInspector.Core.Policies;

public sealed record PolicyFindingScope
{
    public const string RepositoryKind = "repository";
    public const string ProjectKind = "project";

    private PolicyFindingScope(string kind, string? projectPath)
    {
        Kind = kind;
        ProjectPath = projectPath;
    }

    public string Kind { get; }

    public string? ProjectPath { get; }

    public static PolicyFindingScope Repository { get; } =
        new(RepositoryKind, null);

    public static PolicyFindingScope Project(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        return new(ProjectKind, projectPath);
    }
}
