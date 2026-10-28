namespace DotNetRepoInspector.Core.Contracts;

public static class ContainerImageResolutionStatus
{
    public const string Resolved = "resolved";
    public const string Unresolved = "unresolved";

    public static bool IsDefined(string? value) => value is Resolved or Unresolved;
}

public static class ContainerImageReferenceKind
{
    public const string External = "external";
    public const string Stage = "stage";
    public const string Scratch = "scratch";

    public static bool IsDefined(string? value) => value is External or Stage or Scratch;
}

public static class ContainerDotNetImageFamily
{
    public const string Sdk = "sdk";
    public const string AspNet = "aspnet";
    public const string Runtime = "runtime";
    public const string RuntimeDeps = "runtime-deps";

    public static bool IsDefined(string? value) =>
        value is Sdk or AspNet or Runtime or RuntimeDeps;
}

public sealed record ContainerSourceLocation(string Path, int Line);

public sealed record ContainerImageEvidence(
    string? ImageReference,
    string? Registry,
    string? Repository,
    string? Tag,
    string? Digest,
    string? StageAlias,
    string ReferenceKind,
    string ResolutionStatus,
    string? DotNetFamily,
    bool IsOfficialDotNetImage,
    ContainerSourceLocation Source);
