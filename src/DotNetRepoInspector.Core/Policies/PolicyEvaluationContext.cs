using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Core.Policies;

public sealed record PolicyEvaluationContext(
    RepositoryMetadata Repository,
    DotNetSdkMetadata DotNetSdk,
    IReadOnlyList<ProjectInspection> Projects)
{
    public static PolicyEvaluationContext FromInspection(InspectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new(
            report.Repository,
            report.DotNetSdk,
            report.Projects);
    }
}
