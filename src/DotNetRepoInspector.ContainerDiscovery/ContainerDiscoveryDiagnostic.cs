namespace DotNetRepoInspector.ContainerDiscovery;

public static class ContainerDiscoveryDiagnosticCodes
{
    public const string LimitReached = "DRC1001";
    public const string FileSkipped = "DRC1002";
    public const string DockerfileParseFailed = "DRC1003";
}

public sealed record ContainerDiscoveryDiagnostic(
    string Code,
    string Reason,
    string? SourcePath = null,
    int? Line = null);
