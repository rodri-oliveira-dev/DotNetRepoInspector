namespace DotNetRepoInspector.IntegrationDiscovery;

public interface IIntegrationDiscoveryPipeline
{
    Task<IntegrationDiscoveryResult> DiscoverAsync(
        IntegrationDiscoveryRequest request,
        CancellationToken cancellationToken = default);
}
