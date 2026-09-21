namespace DotNetRepoInspector.Git.Tests;

internal static class TemporaryDirectory
{
    public static void Delete(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        ResetAttributes(path);
        Directory.Delete(path, recursive: true);
    }

    private static void ResetAttributes(string path)
    {
        foreach (var file in Directory.EnumerateFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     path,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Directory);
        }

        File.SetAttributes(path, FileAttributes.Directory);
    }
}
