namespace DotNetRepoInspector.Persistence;

public sealed record InspectionExecutionMetadata(
    string? Id = null,
    string? Provider = null,
    string? Ref = null);
