namespace DotNetRepoInspector.MSBuild.Sdk;

public sealed record DotNetSdkConfiguration(
    string? Version,
    string? RollForward,
    bool? AllowPrerelease);
