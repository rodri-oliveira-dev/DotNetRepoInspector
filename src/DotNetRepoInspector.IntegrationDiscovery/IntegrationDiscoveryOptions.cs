namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed record IntegrationDiscoveryOptions
{
    private const int MaximumAllowedBytesPerFile = 67_108_864;
    private const long MaximumAllowedTotalBytes = 1_073_741_824;

    public const int DefaultMaxVisitedPaths = 10_000;
    public const int DefaultMaxSourceFiles = 2_000;
    public const int DefaultMaxBytesPerFile = 1_048_576;
    public const long DefaultMaxTotalBytes = 20_971_520;
    public const int DefaultMaxFindings = 1_000;
    public const int DefaultMaxDiagnostics = 100;

    public int MaxVisitedPaths { get; init; } = DefaultMaxVisitedPaths;

    public int MaxSourceFiles { get; init; } = DefaultMaxSourceFiles;

    public int MaxBytesPerFile { get; init; } = DefaultMaxBytesPerFile;

    public long MaxTotalBytes { get; init; } = DefaultMaxTotalBytes;

    public int MaxFindings { get; init; } = DefaultMaxFindings;

    public int MaxDiagnostics { get; init; } = DefaultMaxDiagnostics;

    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaxVisitedPaths <= 0 ||
            MaxSourceFiles <= 0 ||
            MaxBytesPerFile <= 0 ||
            MaxTotalBytes <= 0 ||
            MaxFindings <= 0 ||
            MaxDiagnostics <= 0 ||
            MaxDuration <= TimeSpan.Zero ||
            MaxBytesPerFile > MaximumAllowedBytesPerFile ||
            MaxTotalBytes > MaximumAllowedTotalBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(IntegrationDiscoveryOptions),
                "Integration discovery budgets must be positive and within the supported safety bounds.");
        }
    }
}
