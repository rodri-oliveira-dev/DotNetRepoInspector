namespace DotNetRepoInspector.MSBuild.Sdk;

public sealed record DotNetSdkInspectionError(
    DotNetSdkInspectionErrorCode Code,
    string Message,
    int? ExitCode = null,
    string? Details = null);
