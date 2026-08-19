using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Engine;

public interface IRepositoryInspector
{
    Task<InspectionReport> InspectAsync(
        RepositoryInspectionRequest request,
        CancellationToken cancellationToken = default);
}
