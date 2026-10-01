namespace DotNetRepoInspector.Core.Contracts;

public sealed record IntegrationDiscoveryMetadata(
    bool Enabled,
    bool Completed,
    bool Truncated)
{
    public static IntegrationDiscoveryMetadata NotExecuted { get; } = new(false, false, false);

    public static IntegrationDiscoveryMetadata Complete(bool truncated) => new(true, true, truncated);
}
