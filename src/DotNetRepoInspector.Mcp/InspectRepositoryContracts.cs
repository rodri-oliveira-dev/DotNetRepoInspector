using DotNetRepoInspector.Core.Contracts;

namespace DotNetRepoInspector.Mcp;

public sealed record InspectRepositoryResponse(
    string McpSchemaVersion,
    bool Ok,
    InspectRepositoryData? Data,
    McpToolError? Error);

public sealed record InspectRepositoryData(InspectionReport Report);

public sealed record McpToolError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string> Details);
