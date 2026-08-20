namespace DotNetRepoInspector.Persistence;

public sealed record InspectionSinkFailure(
    string Code,
    string Message,
    bool IsTransient = false);
