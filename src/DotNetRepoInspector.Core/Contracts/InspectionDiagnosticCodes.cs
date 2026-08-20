namespace DotNetRepoInspector.Core.Contracts;

public static class InspectionDiagnosticCodes
{
    public const string InvalidProject = "DRI1001";
    public const string DotNetSdkUnavailable = "DRI1002";
    public const string ProjectReferenceUnresolved = "DRI1003";
    public const string PropertyNotEvaluable = "DRI1004";
    public const string GlobalJsonInvalid = "DRI1005";
    public const string MsBuildEvaluationFailed = "DRI1006";
    public const string InvalidMsBuildOutput = "DRI1007";
    public const string DotNetHostUnavailable = "DRI1008";
    public const string InvalidInspectionRequest = "DRI1009";
    public const string RepositoryRootUnavailable = "DRI1010";
    public const string GlobalJsonReadFailed = "DRI1011";
    public const string RepositoryMetadataUnavailable = "DRI1012";
    public const string InvalidConfiguration = "DRI1013";
    public const string ClassificationOverrideTargetNotFound = "DRI1014";
}
