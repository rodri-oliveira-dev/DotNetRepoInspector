using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Persistence;

namespace DotNetRepoInspector.Cli;

public interface ICliPersistenceCoordinator
{
    Task<InspectionPersistenceResult?> PublishAsync(
        InspectionReport report,
        string inspectorVersion,
        CliPersistenceOptions options,
        CancellationToken cancellationToken = default);
}
