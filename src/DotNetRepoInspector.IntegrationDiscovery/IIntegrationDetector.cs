using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.IntegrationDiscovery;

public interface IIntegrationDetector
{
    string Id
    {
        get;
    }

    ValueTask<IReadOnlyList<IntegrationFinding>> DetectAsync(
        IntegrationDetectionContext context,
        CancellationToken cancellationToken);
}
