using System.Security.Cryptography;
using System.Text;

namespace DotNetRepoInspector.Core.Contracts;

public sealed record IntegrationFinding(
    string Id,
    string ProjectPath,
    string Kind,
    string Direction,
    string Technology,
    string? Target,
    string? ResourceType,
    string? ConfigurationKey,
    string? Contract,
    IntegrationSourceLocation Source,
    string Confidence,
    IReadOnlyList<string> Signals)
{
    public static IntegrationFinding Create(
        string projectPath,
        string kind,
        string direction,
        string technology,
        IntegrationSourceLocation source,
        string confidence,
        IReadOnlyList<string> signals,
        string? target = null,
        string? resourceType = null,
        string? configurationKey = null,
        string? contract = null)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(signals);

        string identity = string.Join(
            "\u001f",
            NormalizePath(projectPath),
            kind,
            direction,
            technology,
            target ?? string.Empty,
            resourceType ?? string.Empty,
            configurationKey ?? string.Empty,
            contract ?? string.Empty,
            NormalizePath(source.Path),
            source.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));

        return new IntegrationFinding(
            $"integration-{hash[..16]}",
            projectPath,
            kind,
            direction,
            technology,
            target,
            resourceType,
            configurationKey,
            contract,
            source,
            confidence,
            signals);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed record IntegrationSourceLocation(string Path, int Line);

public static class IntegrationKind
{
    public const string Http = "http";
    public const string Messaging = "messaging";
    public const string Database = "database";
    public const string Cache = "cache";
    public const string Storage = "storage";
    public const string Rpc = "rpc";
    public const string Unknown = "unknown";

    public static bool IsDefined(string? value) =>
        value is Http or Messaging or Database or Cache or Storage or Rpc or Unknown;
}

public static class IntegrationDirection
{
    public const string Outbound = "outbound";
    public const string Publish = "publish";
    public const string Consume = "consume";
    public const string Read = "read";
    public const string Write = "write";
    public const string Bidirectional = "bidirectional";
    public const string Unknown = "unknown";

    public static bool IsDefined(string? value) =>
        value is Outbound or Publish or Consume or Read or Write or Bidirectional or Unknown;
}

public static class IntegrationConfidence
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    public static bool IsDefined(string? value) => value is High or Medium or Low;
}
