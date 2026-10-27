namespace DotNetRepoInspector.ContainerDiscovery;

public sealed record ContainerDiscoveryResult(
    IReadOnlyList<DockerImageEvidence> Images,
    IReadOnlyList<ContainerDiscoveryDiagnostic> Diagnostics,
    bool Truncated)
{
    public static ContainerDiscoveryResult Empty
    {
        get;
    } = new(
        Array.Empty<DockerImageEvidence>(),
        Array.Empty<ContainerDiscoveryDiagnostic>(),
        false);
}
