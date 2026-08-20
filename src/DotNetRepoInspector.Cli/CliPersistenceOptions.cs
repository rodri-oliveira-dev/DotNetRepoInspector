using DotNetRepoInspector.Persistence;

namespace DotNetRepoInspector.Cli;

public sealed record CliPersistenceOptions(
    string? Sink,
    Uri? Endpoint,
    int TimeoutSeconds,
    PersistenceFailureMode FailureMode,
    int MaxAttempts)
{
    public const int DefaultTimeoutSeconds = 15;
    public const int DefaultMaxAttempts = 3;

    public static CliPersistenceOptions Disabled =>
        new(
            null,
            null,
            DefaultTimeoutSeconds,
            PersistenceFailureMode.NonFatal,
            DefaultMaxAttempts);

    public bool Enabled => Sink is not null;
}
