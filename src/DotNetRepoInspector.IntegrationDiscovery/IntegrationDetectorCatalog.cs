namespace DotNetRepoInspector.IntegrationDiscovery;

public static class IntegrationDetectorCatalog
{
    public static IReadOnlyList<IIntegrationDetector> CreateDefault() =>
        [new HttpIntegrationDetector(), new MessagingIntegrationDetector()];
}
