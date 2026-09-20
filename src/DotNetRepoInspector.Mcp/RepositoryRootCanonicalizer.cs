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
        var segments = fullPath[pathRoot.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var candidatePath = Path.Combine(currentPath, segments[index]);
            var target = new DirectoryInfo(candidatePath).ResolveLinkTarget(returnFinalTarget: true);
            if (target is null)
            {
                currentPath = candidatePath;
                continue;
            }

            var targetWithRemainder = segments[(index + 1)..]
                .Aggregate(target.FullName, Path.Combine);
            return NormalizeExistingDirectory(targetWithRemainder);
        }

        var canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentPath));
        if (!Directory.Exists(canonicalPath))
        {
            throw new DirectoryNotFoundException();
        }

        return canonicalPath;
    }
}
