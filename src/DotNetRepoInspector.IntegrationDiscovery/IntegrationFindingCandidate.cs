namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed record IntegrationFindingCandidate(
    string Kind,
    string Direction,
    string Technology,
    int Line,
    string Confidence,
    IReadOnlyList<string> Signals,
    string? Target = null,
    string? ResourceType = null,
    string? ConfigurationKey = null,
    string? Contract = null);

public interface IIntegrationFindingCollector
{
    bool TryAdd(IntegrationFindingCandidate candidate);
}
