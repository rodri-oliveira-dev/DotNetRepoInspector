using System.Diagnostics.CodeAnalysis;

namespace DotNetRepoInspector.Persistence;

public sealed class InspectionSnapshotPublisher
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A sink is an extension boundary. Unexpected adapter exceptions are normalized without exposing exception details so non-fatal persistence remains isolated from inspection.")]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The publisher is intentionally instance-based so delivery hosts can compose it as a service and the policy can evolve without changing the public calling model.")]
    public async Task<InspectionPersistenceResult> PublishAsync(
        InspectionSnapshot snapshot,
        IInspectionSnapshotSink sink,
        InspectionPersistenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentException.ThrowIfNullOrWhiteSpace(sink.Name);
        cancellationToken.ThrowIfCancellationRequested();

        options ??= InspectionPersistenceOptions.Default;
        options.Validate();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(options.Timeout);

        try
        {
            var sinkResult = await sink.WriteAsync(snapshot, timeoutSource.Token);
            ArgumentNullException.ThrowIfNull(sinkResult);

            return new InspectionPersistenceResult(
                sink.Name,
                sinkResult.Succeeded,
                sinkResult.Failure,
                options.FailureMode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            return Failure(
                sink.Name,
                InspectionPersistenceErrorCodes.Timeout,
                "Persistence exceeded the configured timeout.",
                isTransient: true,
                options.FailureMode);
        }
        catch (Exception)
        {
            return Failure(
                sink.Name,
                InspectionPersistenceErrorCodes.UnexpectedSinkFailure,
                "The persistence sink failed unexpectedly.",
                isTransient: false,
                options.FailureMode);
        }
    }

    private static InspectionPersistenceResult Failure(
        string sinkName,
        string code,
        string message,
        bool isTransient,
        PersistenceFailureMode failureMode) =>
        new(
            sinkName,
            false,
            new InspectionSinkFailure(code, message, isTransient),
            failureMode);
}
