using System.Text.Json;

using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.Core.Tests;

public sealed class DiagnosticContractTests
{
    private static readonly string[] ExpectedContextKeys = ["alpha", "zeta"];

    [Fact]
    public void PrincipalDiagnostics_HaveStableCodesAndSeverities()
    {
        var invalidProject = InspectionDiagnostics.InvalidProject();
        var sdkUnavailable = InspectionDiagnostics.DotNetSdkUnavailable();
        var unresolvedReference = InspectionDiagnostics.ProjectReferenceUnresolved();
        var propertyNotEvaluable = InspectionDiagnostics.PropertyNotEvaluable();

        Assert.Equal(InspectionDiagnosticCodes.InvalidProject, invalidProject.Code);
        Assert.Equal(InspectionDiagnosticSeverity.Error, invalidProject.Severity);
        Assert.Equal(InspectionDiagnosticCodes.DotNetSdkUnavailable, sdkUnavailable.Code);
        Assert.Equal(InspectionDiagnosticSeverity.Error, sdkUnavailable.Severity);
        Assert.Equal(InspectionDiagnosticCodes.ProjectReferenceUnresolved, unresolvedReference.Code);
        Assert.Equal(InspectionDiagnosticSeverity.Warning, unresolvedReference.Severity);
        Assert.Equal(InspectionDiagnosticCodes.PropertyNotEvaluable, propertyNotEvaluable.Code);
        Assert.Equal(InspectionDiagnosticSeverity.Warning, propertyNotEvaluable.Severity);
    }

    [Fact]
    public void Serialize_CanonicalizesDiagnosticContext()
    {
        var diagnostic = InspectionDiagnostics.ProjectReferenceUnresolved(
            "src\\App\\App.csproj",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["zeta"] = "last",
                ["alpha"] = "first"
            });
        var report = CreateReport(diagnostic);

        var json = InspectionJsonSerializer.Serialize(report);

        using var document = JsonDocument.Parse(json);
        var serializedDiagnostic = document.RootElement
            .GetProperty("diagnostics")[0];

        Assert.Equal("warning", serializedDiagnostic.GetProperty("severity").GetString());
        Assert.Equal("src/App/App.csproj", serializedDiagnostic.GetProperty("source").GetString());
        Assert.Equal(
            ExpectedContextKeys,
            serializedDiagnostic
                .GetProperty("context")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray());
    }

    [Fact]
    public void Serialize_RejectsUnknownDiagnosticSeverity()
    {
        var diagnostic = new InspectionDiagnostic(
            "DRI9999",
            "fatal",
            "Unsupported severity.",
            null,
            null);
        var report = CreateReport(diagnostic);

        Assert.Throws<JsonException>(() => InspectionJsonSerializer.Serialize(report));
    }

    [Fact]
    public void SchemaVersion_RepresentsAdditiveDiagnosticContextChange()
    {
        Assert.Equal("1.1", InspectionSchema.CurrentVersion);
        Assert.True(InspectionSchema.IsCompatibleVersion("1.0"));
        Assert.True(InspectionSchema.IsCompatibleVersion("1.1"));
    }

    private static InspectionReport CreateReport(InspectionDiagnostic diagnostic) =>
        new(
            InspectionSchema.CurrentVersion,
            new RepositoryMetadata(null, null, null, null),
            new DotNetSdkMetadata(null, null, null),
            Array.Empty<ProjectInspection>(),
            new[] { diagnostic });
}
