using System.Globalization;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Evaluation;
using DotNetRepoInspector.MSBuild.Sdk;

namespace DotNetRepoInspector.MSBuild.Diagnostics;

public static class MsBuildDiagnosticMapper
{
    public static InspectionDiagnostic FromEvaluationError(
        MsBuildEvaluationError error,
        string? source = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        var context = CreateContext(
            "msbuild-evaluation",
            error.Code.ToString(),
            error.ExitCode);

        return error.Code switch
        {
            MsBuildEvaluationErrorCode.InvalidRequest =>
                InspectionDiagnostics.InvalidInspectionRequest(source, context),
            MsBuildEvaluationErrorCode.ProjectNotFound or
            MsBuildEvaluationErrorCode.ProjectFileReadFailed =>
                InspectionDiagnostics.InvalidProject(source, context),
            MsBuildEvaluationErrorCode.DotNetHostNotFound =>
                InspectionDiagnostics.DotNetHostUnavailable(source, context),
            MsBuildEvaluationErrorCode.SdkResolutionFailed =>
                InspectionDiagnostics.DotNetSdkUnavailable(source, context),
            MsBuildEvaluationErrorCode.MsBuildEvaluationFailed =>
                InspectionDiagnostics.MsBuildEvaluationFailed(source, context),
            MsBuildEvaluationErrorCode.InvalidMsBuildOutput =>
                InspectionDiagnostics.InvalidMsBuildOutput(source, context),
            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };
    }

    public static InspectionDiagnostic FromSdkInspectionError(
        DotNetSdkInspectionError error,
        string? source = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        var context = CreateContext(
            "sdk-inspection",
            error.Code.ToString(),
            error.ExitCode);

        return error.Code switch
        {
            DotNetSdkInspectionErrorCode.InvalidRequest =>
                InspectionDiagnostics.InvalidInspectionRequest(source, context),
            DotNetSdkInspectionErrorCode.RepositoryRootNotFound =>
                InspectionDiagnostics.RepositoryRootUnavailable(source, context),
            DotNetSdkInspectionErrorCode.GlobalJsonReadFailed =>
                InspectionDiagnostics.GlobalJsonReadFailed(source, context),
            DotNetSdkInspectionErrorCode.GlobalJsonInvalid =>
                InspectionDiagnostics.GlobalJsonInvalid(source, context),
            DotNetSdkInspectionErrorCode.DotNetHostNotFound =>
                InspectionDiagnostics.DotNetHostUnavailable(source, context),
            DotNetSdkInspectionErrorCode.SdkResolutionFailed =>
                InspectionDiagnostics.DotNetSdkUnavailable(source, context),
            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };
    }

    private static Dictionary<string, string> CreateContext(
        string component,
        string internalCode,
        int? exitCode)
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["component"] = component,
            ["internalCode"] = internalCode
        };

        if (exitCode.HasValue)
        {
            context["exitCode"] = exitCode.Value.ToString(CultureInfo.InvariantCulture);
        }

        return context;
    }
}
