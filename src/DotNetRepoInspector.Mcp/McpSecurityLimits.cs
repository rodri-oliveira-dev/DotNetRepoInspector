namespace DotNetRepoInspector.Mcp;

internal static class McpSecurityLimits
{
    public const int MaxRelativePathLength = 1_024;
    public const int MaxExcludedPaths = 256;
    public const int MaxClassificationOverrides = 256;
    public const int MaxClassificationValueLength = 128;
    public const long MaxConfigurationFileBytes = 1_048_576;
    public const int MaxToolResultUtf8Bytes = 8 * 1_048_576;
}
