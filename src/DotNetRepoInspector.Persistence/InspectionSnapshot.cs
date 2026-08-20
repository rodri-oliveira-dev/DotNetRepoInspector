using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Persistence;

public sealed record InspectionSnapshot
{
    public InspectionSnapshot(InspectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
    }

    public InspectionReport Report
    {
        get;
    }
}
