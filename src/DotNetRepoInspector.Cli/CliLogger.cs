using System.Text;

namespace DotNetRepoInspector.Cli;

public sealed class CliLogger
{
    private const int MaxContextValueLength = 512;
    private static readonly string[] SensitiveKeyFragments =
    [
        "authorization",
        "connectionstring",
        "credential",
        "password",
        "privatekey",
        "secret",
        "token",
        "apikey",
        "api-key",
        "accesskey"
    ];

    private readonly TextWriter _writer;
    private readonly CliVerbosity _verbosity;

    public CliLogger(TextWriter writer, CliVerbosity verbosity)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _verbosity = verbosity;
    }

    public void Information(
        string eventId,
        string message,
        IReadOnlyDictionary<string, string>? context = null) =>
        Write(CliLogLevel.Information, eventId, message, context);

    public void Warning(
        string eventId,
        string message,
        IReadOnlyDictionary<string, string>? context = null) =>
        Write(CliLogLevel.Warning, eventId, message, context);

    public void Error(
        string eventId,
        string message,
        IReadOnlyDictionary<string, string>? context = null) =>
        Write(CliLogLevel.Error, eventId, message, context);

    public void Verbose(
        string eventId,
        string message,
        IReadOnlyDictionary<string, string>? context = null) =>
        Write(CliLogLevel.Verbose, eventId, message, context);

    public void Debug(
        string eventId,
        string message,
        IReadOnlyDictionary<string, string>? context = null) =>
        Write(CliLogLevel.Debug, eventId, message, context);

    private void Write(
        CliLogLevel level,
        string eventId,
        string message,
        IReadOnlyDictionary<string, string>? context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!IsEnabled(level))
        {
            return;
        }

        var line = new StringBuilder()
            .Append('[')
            .Append(LevelLabel(level))
            .Append("] ")
            .Append(NormalizeSingleLine(message, MaxContextValueLength))
            .Append(" event=")
            .Append(NormalizeSingleLine(eventId, MaxContextValueLength));

        if (context is not null)
        {
            foreach (var pair in context.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                var value = IsSensitiveKey(pair.Key)
                    ? "<redacted>"
                    : NormalizeSingleLine(pair.Value ?? string.Empty, MaxContextValueLength);

                line
                    .Append(' ')
                    .Append(NormalizeSingleLine(pair.Key, MaxContextValueLength))
                    .Append("=\"")
                    .Append(value.Replace('"', '\''))
                    .Append('"');
            }
        }

        _writer.WriteLine(line.ToString());
    }

    private bool IsEnabled(CliLogLevel level) =>
        level switch
        {
            CliLogLevel.Information or CliLogLevel.Warning or CliLogLevel.Error => true,
            CliLogLevel.Verbose => _verbosity is CliVerbosity.Verbose or CliVerbosity.Debug,
            CliLogLevel.Debug => _verbosity == CliVerbosity.Debug,
            _ => false
        };

    private static string LevelLabel(CliLogLevel level) =>
        level switch
        {
            CliLogLevel.Information => "info",
            CliLogLevel.Warning => "warning",
            CliLogLevel.Error => "error",
            CliLogLevel.Verbose => "verbose",
            CliLogLevel.Debug => "debug",
            _ => "unknown"
        };

    private static bool IsSensitiveKey(string key) =>
        SensitiveKeyFragments.Any(fragment =>
            key.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeSingleLine(string value, int maxLength)
    {
        var normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
