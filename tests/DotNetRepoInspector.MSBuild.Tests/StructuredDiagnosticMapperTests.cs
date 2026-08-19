using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Diagnostics;
using DotNetRepoInspector.MSBuild.Evaluation;
using DotNetRepoInspector.MSBuild.Sdk;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class StructuredDiagnosticMapperTests
{
    [Theory]
    [InlineData(MsBuildEvaluationErrorCode.ProjectNotFound, InspectionDiagnosticCodes.InvalidProject)]
    [InlineData(MsBuildEvaluationErrorCode.ProjectFileReadFailed, InspectionDiagnosticCodes.InvalidProject)]
    [InlineData(MsBuildEvaluationErrorCode.DotNetHostNotFound, InspectionDiagnosticCodes.DotNetHostUnavailable)]
    [InlineData(MsBuildEvaluationErrorCode.SdkResolutionFailed, InspectionDiagnosticCodes.DotNetSdkUnavailable)]
    [InlineData(MsBuildEvaluationErrorCode.MsBuildEvaluationFailed, InspectionDiagnosticCodes.MsBuildEvaluationFailed)]
    [InlineData(MsBuildEvaluationErrorCode.InvalidMsBuildOutput, InspectionDiagnosticCodes.InvalidMsBuildOutput)]
    public void FromEvaluationError_MapsToStableDiagnosticCode(
        MsBuildEvaluationErrorCode errorCode,
        string expectedDiagnosticCode)
    {
        var error = new MsBuildEvaluationError(errorCode, "localized message", 42, "raw details");

        var diagnostic = MsBuildDiagnosticMapper.FromEvaluationError(
            error,
            "src/App/App.csproj");

        Assert.Equal(expectedDiagnosticCode, diagnostic.Code);
        Assert.Equal(InspectionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("src/App/App.csproj", diagnostic.Source);
        Assert.Null(diagnostic.Details);
        Assert.NotNull(diagnostic.Context);
        Assert.Equal(errorCode.ToString(), diagnostic.Context["internalCode"]);
        Assert.Equal("42", diagnostic.Context["exitCode"]);
        Assert.DoesNotContain("localized message", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("raw details", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DotNetSdkInspectionErrorCode.RepositoryRootNotFound, InspectionDiagnosticCodes.RepositoryRootUnavailable)]
    [InlineData(DotNetSdkInspectionErrorCode.GlobalJsonReadFailed, InspectionDiagnosticCodes.GlobalJsonReadFailed)]
    [InlineData(DotNetSdkInspectionErrorCode.GlobalJsonInvalid, InspectionDiagnosticCodes.GlobalJsonInvalid)]
    [InlineData(DotNetSdkInspectionErrorCode.DotNetHostNotFound, InspectionDiagnosticCodes.DotNetHostUnavailable)]
    [InlineData(DotNetSdkInspectionErrorCode.SdkResolutionFailed, InspectionDiagnosticCodes.DotNetSdkUnavailable)]
    public void FromSdkInspectionError_MapsToStableDiagnosticCode(
        DotNetSdkInspectionErrorCode errorCode,
        string expectedDiagnosticCode)
    {
        var error = new DotNetSdkInspectionError(errorCode, "mensagem localizada", 17, "detalhes");

        var diagnostic = MsBuildDiagnosticMapper.FromSdkInspectionError(
            error,
            "global.json");

        Assert.Equal(expectedDiagnosticCode, diagnostic.Code);
        Assert.Equal(InspectionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.NotNull(diagnostic.Context);
        Assert.Equal("sdk-inspection", diagnostic.Context["component"]);
        Assert.Equal("17", diagnostic.Context["exitCode"]);
        Assert.Null(diagnostic.Details);
    }
}
