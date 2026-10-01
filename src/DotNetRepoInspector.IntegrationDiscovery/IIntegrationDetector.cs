namespace DotNetRepoInspector.IntegrationDiscovery;

public interface IIntegrationDetector
{
    string Id
    {
        get;
    }

    ValueTask DetectAsync(
        IntegrationDetectionContext context,
        CancellationToken cancellationToken);
}
