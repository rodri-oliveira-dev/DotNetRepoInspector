namespace DotNetRepoInspector.IntegrationDiscovery;

public sealed record IntegrationDetectionContext(
    string ProjectPath,
    string SourcePath,
    ReadOnlyMemory<char> SourceText);
