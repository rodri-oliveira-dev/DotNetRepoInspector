namespace DotNetRepoInspector.ContainerDiscovery;

public interface IContainerDiscoveryPipeline
{
    Task<ContainerDiscoveryResult> DiscoverAsync(
        ContainerDiscoveryRequest request,
        CancellationToken cancellationToken = default);
}
