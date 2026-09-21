using System.Text.Json;
using System.Threading.Channels;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

namespace DotNetRepoInspector.Mcp;

public sealed class RepositoryInspectionExecutor
{
    private const int MaxQueuedInspections = 8;
    private static readonly TimeSpan InspectionTimeout = TimeSpan.FromMinutes(5);
    private readonly IRepositoryInspector _repositoryInspector;
    private readonly RepositoryRoot _repositoryRoot;
    private readonly Channel<bool> _inspectionGate;
    private readonly TimeSpan _inspectionTimeout;
    private readonly int _maxPendingInspections;
    private int _pendingInspections;

    public RepositoryInspectionExecutor(
        IRepositoryInspector repositoryInspector,
        RepositoryRoot repositoryRoot)
        : this(
            repositoryInspector,
            repositoryRoot,
            InspectionTimeout,
            MaxQueuedInspections)
    {
    }

    internal RepositoryInspectionExecutor(
        IRepositoryInspector repositoryInspector,
        RepositoryRoot repositoryRoot,
        TimeSpan inspectionTimeout,
        int maxQueuedInspections)
    {
        ArgumentNullException.ThrowIfNull(repositoryInspector);
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(inspectionTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(maxQueuedInspections);

        _repositoryInspector = repositoryInspector;
        _repositoryRoot = repositoryRoot;
        _inspectionTimeout = inspectionTimeout;
        _maxPendingInspections = maxQueuedInspections + 1;
        _inspectionGate = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
        _inspectionGate.Writer.TryWrite(true);
    }

    public RepositoryRoot RepositoryRoot => _repositoryRoot;

    public async Task<RepositoryInspectionOutcome> ExecuteAsync(
        string? configurationPath,
        bool disableConfigurationFile,
        IReadOnlyCollection<string>? excludedPaths,
        IReadOnlyDictionary<string, string>? classificationOverrides,
        CancellationToken cancellationToken)
    {
        var validation = RepositoryPathBoundary.ValidateAndNormalize(
            _repositoryRoot,
            configurationPath,
            disableConfigurationFile,
            excludedPaths,
            classificationOverrides);
        if (!validation.Succeeded)
        {
            return RepositoryInspectionOutcome.Failure(
                validation.ErrorCode!,
                validation.ErrorMessage!);
        }

        if (Interlocked.Increment(ref _pendingInspections) > _maxPendingInspections)
        {
            Interlocked.Decrement(ref _pendingInspections);
            return RepositoryInspectionOutcome.Failure(
                "server_busy",
                "The MCP server inspection queue is full. Retry the request later.");
        }

        var gateEntered = false;
        using var timeoutSource = new CancellationTokenSource(_inspectionTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            await _inspectionGate.Reader.ReadAsync(linkedSource.Token);
            gateEntered = true;

            var report = await _repositoryInspector.InspectAsync(
                new RepositoryInspectionRequest(
                    _repositoryRoot.FullPath,
                    ConfigurationPath: validation.ConfigurationPath,
                    DisableConfigurationFile: disableConfigurationFile,
                    ExcludedPaths: validation.ExcludedPaths,
                    ClassificationOverrides: validation.ClassificationOverrides),
                linkedSource.Token);

            // Apply the canonical serializer's normalization and sensitive-context redaction
            // before any granular MCP projection can serialize contract records directly.
            report = InspectionJsonSerializer.Deserialize(
                InspectionJsonSerializer.Serialize(report));

            return RepositoryInspectionOutcome.Success(report);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            return RepositoryInspectionOutcome.Failure(
                "inspection_timed_out",
                "Repository inspection exceeded the server time limit.");
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            IOException or
            JsonException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException)
        {
            return RepositoryInspectionOutcome.Failure(
                "inspection_failed",
                "Repository inspection failed before a report could be produced.");
        }
        finally
        {
            if (gateEntered)
            {
                _inspectionGate.Writer.TryWrite(true);
            }

            Interlocked.Decrement(ref _pendingInspections);
        }
    }
}

public sealed record RepositoryInspectionOutcome(
    InspectionReport? Report,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Succeeded => Report is not null;

    public static RepositoryInspectionOutcome Success(InspectionReport report) =>
        new(report, null, null);

    public static RepositoryInspectionOutcome Failure(string code, string message) =>
        new(null, code, message);
}
