using System.Diagnostics;

namespace DotNetRepoInspector.MSBuild;

internal static class SecureProcessEnvironment
{
    private static readonly string[] SensitiveNameFragments =
    [
        "ACCESSTOKEN",
        "ACCESS_TOKEN",
        "APIKEY",
        "API_KEY",
        "AUTHORIZATION",
        "BEARER",
        "CLIENTSECRET",
        "CLIENT_SECRET",
        "CONNECTIONSTRING",
        "CONNECTION_STRING",
        "CREDENTIAL",
        "PASSWORD",
        "PRIVATEKEY",
        "PRIVATE_KEY",
        "SECRET",
        "SHAREDACCESSKEY",
        "SHARED_ACCESS_KEY",
        "TOKEN"
    ];

    private static readonly HashSet<string> SensitiveExactNames = new(
        [
            "DOCKER_CONFIG",
            "GPG_AGENT_INFO",
            "KUBECONFIG",
            "SSH_AGENT_PID",
            "SSH_AUTH_SOCK"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static void HardenDotNetProcess(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        foreach (var name in startInfo.Environment.Keys.ToArray())
        {
            if (IsSensitiveEnvironmentVariable(name))
            {
                startInfo.Environment.Remove(name);
            }
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true";
        startInfo.Environment["DOTNET_NOLOGO"] = "true";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
    }

    private static bool IsSensitiveEnvironmentVariable(string name) =>
        SensitiveExactNames.Contains(name) ||
        SensitiveNameFragments.Any(fragment =>
            name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
