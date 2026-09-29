namespace DotNetRepoInspector.Core.Policies;

public sealed record PolicyFindingScope
{
    public const string RepositoryKind = "repository";
    public const string ProjectKind = "project";

    public PolicyFindingScope(string kind, string? projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var repositoryScope =
            string.Equals(kind, RepositoryKind, StringComparison.Ordinal) &&
            projectPath is null;
        var projectScope =
            string.Equals(kind, ProjectKind, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(projectPath);

        if (!repositoryScope && !projectScope)
        {
            throw new ArgumentException(
                "Policy scope must be repository without a project path or project with a project path.",
                nameof(projectPath));
        }

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
