namespace DotNetRepoInspector.Core.Contracts;

public sealed record DotNetSdkMetadata(
    string? GlobalJsonPath,
    ConfiguredDotNetSdk? Configured,
    string? ResolvedVersion);

public sealed record ConfiguredDotNetSdk(
    string? Version,
    string? RollForward,
    bool? AllowPrerelease);
