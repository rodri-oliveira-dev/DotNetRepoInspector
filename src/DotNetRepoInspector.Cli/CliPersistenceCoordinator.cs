using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Persistence;
using DotNetRepoInspector.Persistence.Http;

namespace DotNetRepoInspector.Cli;

public sealed class CliPersistenceCoordinator : ICliPersistenceCoordinator
{
    private readonly Func<string, string?> _environmentVariableReader;
    private readonly InspectionSnapshotFactory _snapshotFactory;
    private readonly InspectionSnapshotPublisher _publisher;

    public CliPersistenceCoordinator()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    public CliPersistenceCoordinator(Func<string, string?> environmentVariableReader)
    {
        ArgumentNullException.ThrowIfNull(environmentVariableReader);

        _environmentVariableReader = environmentVariableReader;
        _snapshotFactory = new InspectionSnapshotFactory();
        _publisher = new InspectionSnapshotPublisher();
    }

    public async Task<InspectionPersistenceResult?> PublishAsync(
        InspectionReport report,
        string inspectorVersion,
        CliPersistenceOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(inspectorVersion);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return null;
        }

        if (!string.Equals(options.Sink, "http", StringComparison.Ordinal) ||
            options.Endpoint is null)
        {
            return InvalidConfiguration(options.FailureMode);
        }

        var execution = new InspectionExecutionMetadata(
            ReadEnvironmentVariable(CliPersistenceEnvironmentVariables.ExecutionId),
            ReadEnvironmentVariable(CliPersistenceEnvironmentVariables.ExecutionProvider),
            ReadEnvironmentVariable(CliPersistenceEnvironmentVariables.ExecutionRef));
        InspectionSnapshot snapshot = _snapshotFactory.Create(
            report,
            inspectorVersion,
            execution);

        string? bearerToken = ReadEnvironmentVariable(
            CliPersistenceEnvironmentVariables.HttpBearerToken);

        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            var sink = new HttpInspectionSnapshotSink(
                httpClient,
                new HttpInspectionSnapshotSinkOptions(options.Endpoint)
                {
                    BearerToken = bearerToken,
                    MaxAttempts = options.MaxAttempts
                });

            return await _publisher.PublishAsync(
                snapshot,
                sink,
                new InspectionPersistenceOptions
                {
                    Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                    FailureMode = options.FailureMode
                },
                cancellationToken);
        }
        catch (ArgumentException)
        {
            return InvalidConfiguration(options.FailureMode);
        }
    }

    private string? ReadEnvironmentVariable(string name)
    {
        string? value = _environmentVariableReader(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static InspectionPersistenceResult InvalidConfiguration(
        PersistenceFailureMode failureMode) =>
        new(
            "http",
            false,
            new InspectionSinkFailure(
                HttpInspectionSinkErrorCodes.InvalidConfiguration,
                "The HTTP persistence sink configuration is invalid.",
                false),
            failureMode);
}
