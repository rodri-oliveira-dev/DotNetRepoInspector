using System.Text.Json;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Engine;

namespace DotNetRepoInspector.Mcp;

public sealed class RepositoryInspectionExecutor
{
    private readonly IRepositoryInspector _repositoryInspector;
    private readonly RepositoryRoot _repositoryRoot;

    public RepositoryInspectionExecutor(
        IRepositoryInspector repositoryInspector,
        RepositoryRoot repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryInspector);
        ArgumentNullException.ThrowIfNull(repositoryRoot);

        _repositoryInspector = repositoryInspector;
        _repositoryRoot = repositoryRoot;
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

        try
        {
            var report = await _repositoryInspector.InspectAsync(
                new RepositoryInspectionRequest(
                    _repositoryRoot.FullPath,
                    ConfigurationPath: validation.ConfigurationPath,
                    DisableConfigurationFile: disableConfigurationFile,
                    ExcludedPaths: validation.ExcludedPaths,
                    ClassificationOverrides: validation.ClassificationOverrides),
                cancellationToken);

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
