using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Evaluation;

namespace DotNetRepoInspector.MSBuild.References;

public static class ProjectReferenceGraphBuilder
{
    public static ProjectReferenceGraph Build(
        string repositoryRoot,
        IReadOnlyDictionary<string, MsBuildProjectFacts> projects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(projects);

        var rootPath = Path.GetFullPath(repositoryRoot);
        var nodes = projects
            .Select(project => BuildNode(rootPath, project.Key, project.Value))
            .OrderBy(project => project.ProjectPath, StringComparer.Ordinal)
            .ToArray();

        return new ProjectReferenceGraph(nodes);
    }

    private static ProjectReferenceGraphNode BuildNode(
        string repositoryRoot,
        string projectPath,
        MsBuildProjectFacts facts)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("A project path is required.", nameof(projectPath));
        }

        ArgumentNullException.ThrowIfNull(facts);

        var normalizedProjectPath = ToRepositoryRelativePath(repositoryRoot, projectPath);
        var targets = new SortedDictionary<string, bool>(StringComparer.Ordinal);

        foreach (var reference in facts.ProjectReferences)
        {
            var targetFullPath = Path.GetFullPath(reference.FullPath);
            var targetPath = NormalizePath(Path.GetRelativePath(repositoryRoot, targetFullPath));
            var targetExists = File.Exists(targetFullPath);

            if (targets.TryGetValue(targetPath, out var existingTargetExists))
            {
                targets[targetPath] = existingTargetExists || targetExists;
            }
            else
            {
                targets[targetPath] = targetExists;
            }
        }

        var references = targets.Keys
            .Select(path => new ProjectReferenceMetadata(path))
            .ToArray();

        var diagnostics = targets
            .Where(target => !target.Value)
            .Select(target => InspectionDiagnostics.ProjectReferenceUnresolved(
                normalizedProjectPath,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["referencePath"] = target.Key
                }))
            .ToArray();

        return new ProjectReferenceGraphNode(
            normalizedProjectPath,
            references,
            diagnostics);
    }

    private static string ToRepositoryRelativePath(
        string repositoryRoot,
        string projectPath)
    {
        var fullPath = Path.IsPathRooted(projectPath)
            ? Path.GetFullPath(projectPath)
            : Path.GetFullPath(Path.Combine(repositoryRoot, projectPath));

        return NormalizePath(Path.GetRelativePath(repositoryRoot, fullPath));
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');
}
