namespace DotNetRepoInspector.ContainerDiscovery;

public sealed record ContainerDiscoveryOptions
{
    private const int MaximumAllowedBytesPerFile = 67_108_864;
    private const long MaximumAllowedTotalBytes = 1_073_741_824;
    private const int MaximumAllowedDepth = 256;

    public const int DefaultMaxVisitedPaths = 10_000;
    public const int DefaultMaxDockerfiles = 500;
    public const int DefaultMaxBytesPerFile = 1_048_576;
    public const long DefaultMaxTotalBytes = 20_971_520;
    public const int DefaultMaxDepth = 32;
    public const int DefaultMaxResults = 1_000;
    public const int DefaultMaxDiagnostics = 100;

    public int MaxVisitedPaths { get; init; } = DefaultMaxVisitedPaths;

    public int MaxDockerfiles { get; init; } = DefaultMaxDockerfiles;

    public int MaxBytesPerFile { get; init; } = DefaultMaxBytesPerFile;

    public long MaxTotalBytes { get; init; } = DefaultMaxTotalBytes;

    public int MaxDepth { get; init; } = DefaultMaxDepth;

    public int MaxResults { get; init; } = DefaultMaxResults;

    public int MaxDiagnostics { get; init; } = DefaultMaxDiagnostics;

    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaxVisitedPaths <= 0 ||
            MaxDockerfiles <= 0 ||
            MaxBytesPerFile <= 0 ||
            MaxTotalBytes <= 0 ||
            MaxDepth < 0 ||
            MaxResults <= 0 ||
            MaxDiagnostics <= 0 ||
            MaxDuration <= TimeSpan.Zero ||
            MaxBytesPerFile > MaximumAllowedBytesPerFile ||
            MaxTotalBytes > MaximumAllowedTotalBytes ||
            MaxDepth > MaximumAllowedDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ContainerDiscoveryOptions),
                "Container discovery budgets must be positive and within the supported safety bounds.");
        }
    }
}
