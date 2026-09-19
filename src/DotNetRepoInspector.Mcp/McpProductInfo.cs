using System.Reflection;

namespace DotNetRepoInspector.Mcp;

public static class McpProductInfo
{
    public const string ServerName = "DotNetRepoInspector.Mcp";

    public static string Version
    {
        get
        {
            var informationalVersion = typeof(McpProductInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                var metadataSeparatorIndex = informationalVersion.IndexOf('+');
                return metadataSeparatorIndex >= 0
                    ? informationalVersion[..metadataSeparatorIndex]
                    : informationalVersion;
            }

            var version = typeof(McpProductInfo).Assembly.GetName().Version;
            return version is null
                ? "0.0.0"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
