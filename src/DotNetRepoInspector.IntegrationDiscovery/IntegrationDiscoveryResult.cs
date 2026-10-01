using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed record IntegrationDiscoveryResult(
    IReadOnlyList<IntegrationFinding> Findings,
    IReadOnlyList<InspectionDiagnostic> Diagnostics,
    bool Truncated)
{
    public static IntegrationDiscoveryResult Empty
    {
        get;
    } =
        new(
            Array.Empty<IntegrationFinding>(),
            Array.Empty<InspectionDiagnostic>(),
            false);
}
