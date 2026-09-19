namespace DotNetRepoInspector.Mcp;

internal static class RepositoryRootCanonicalizer
{
    public static string NormalizeExistingDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException();
        }

        var pathRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(pathRoot))
        {
            throw new ArgumentException("The repository root has no filesystem root.", nameof(path));
        }

        var currentPath = pathRoot;
        var relativePath = fullPath[pathRoot.Length..];
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidatePath = Path.Combine(currentPath, segment);
            var target = new DirectoryInfo(candidatePath).ResolveLinkTarget(returnFinalTarget: true);
            currentPath = target?.FullName ?? candidatePath;
        }

        var canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentPath));
        if (!Directory.Exists(canonicalPath))
        {
            throw new DirectoryNotFoundException();
        }

        return canonicalPath;
    }
}
