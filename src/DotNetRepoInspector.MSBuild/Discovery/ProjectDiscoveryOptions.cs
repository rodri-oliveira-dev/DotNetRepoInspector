namespace DotNetRepoInspector.MSBuild.Discovery;

public sealed class ProjectDiscoveryOptions
{
    private static readonly IReadOnlyCollection<string> DefaultProjectExtensions =
        Array.AsReadOnly<string>([".csproj"]);

    private static readonly IReadOnlyCollection<string> DefaultExcludedDirectoryNames =
        Array.AsReadOnly<string>(["bin", "obj", ".git", "artifacts"]);

    public IReadOnlyCollection<string> SupportedProjectExtensions { get; init; } = DefaultProjectExtensions;

    public IReadOnlyCollection<string> ExcludedDirectoryNames { get; init; } = DefaultExcludedDirectoryNames;
}
