using System.Collections;

namespace DotNetRepoInspector.Mcp;

internal static class McpProcessEnvironment
{
    private static readonly string[] SensitiveNameFragments =
    [
        "ACCESSKEY",
        "ACCESS_KEY",
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
            "GITHUB_ENV",
            "GITHUB_OUTPUT",
            "GITHUB_PATH",
            "GITHUB_STATE",
            "GITHUB_STEP_SUMMARY",
            "GPG_AGENT_INFO",
            "KUBECONFIG",
            "SSH_AGENT_PID",
            "SSH_AUTH_SOCK",
            "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static void HardenCurrentProcess()
    {
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            var name = variable.Key.ToString();
            if (name is not null && IsSensitiveName(name))
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    internal static bool IsSensitiveName(string name) =>
        SensitiveExactNames.Contains(name) ||
        SensitiveNameFragments.Any(fragment =>
            name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
