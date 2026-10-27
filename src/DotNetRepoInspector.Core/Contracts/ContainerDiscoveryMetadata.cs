namespace DotNetRepoInspector.Core.Contracts;

public sealed record ContainerDiscoveryMetadata(
    bool Enabled,
    bool Completed,
    bool Truncated)
{
    public static ContainerDiscoveryMetadata NotExecuted
    {
        get;
    } =
        new(false, false, false);

    public static ContainerDiscoveryMetadata Complete(bool truncated) =>
        new(true, true, truncated);
}
