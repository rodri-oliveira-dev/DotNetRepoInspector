namespace DotNetRepoInspector.ContainerDiscovery;

public static class DockerImageResolutionStatus
{
    public const string Resolved = "resolved";
    public const string Unresolved = "unresolved";
}

public static class DockerImageReferenceKind
{
    public const string External = "external";
    public const string Stage = "stage";
    public const string Scratch = "scratch";
}

public static class DotNetContainerImageFamily
{
    public const string Sdk = "sdk";
    public const string AspNet = "aspnet";
    public const string Runtime = "runtime";
    public const string RuntimeDeps = "runtime-deps";
}

public sealed record DockerImageEvidence(
    string SourcePath,
    int Line,
    string? ImageReference,
    string? Registry,
    string? Repository,
    string? Tag,
    string? Digest,
    string? StageAlias,
    string ReferenceKind,
    string ResolutionStatus,
    string? DotNetFamily,
    bool IsOfficialDotNetImage);
