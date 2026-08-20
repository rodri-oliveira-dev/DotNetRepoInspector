using System.Text.Json;

using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.Core.Tests;

public sealed class ClassificationOverrideContractTests
{
    [Fact]
    public void Serialize_EmitsOverrideSourceAndAutomaticKindOnlyWhenConfigured()
    {
        var project = new ProjectInspection(
            "src/App/App.csproj",
            "App",
            "10.0.400",
            Array.Empty<ProjectSdkMetadata>(),
            ["net10.0"],
            "Library",
            false,
            true,
            Array.Empty<string>(),
            new ProjectClassification(
                "web",
                null,
                ["output-type:library"],
                "configuration",
                "library"),
            Array.Empty<ProjectReferenceMetadata>(),
            Array.Empty<InspectionDiagnostic>());
        var report = InspectionReport.Create(
            new RepositoryMetadata(null, null, null, null, null),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            [project],
            Array.Empty<InspectionDiagnostic>());

        var json = InspectionJsonSerializer.Serialize(report);

        using var document = JsonDocument.Parse(json);
        var classification = document.RootElement
            .GetProperty("projects")[0]
            .GetProperty("classification");
        Assert.Equal("web", classification.GetProperty("kind").GetString());
        Assert.Equal("configuration", classification.GetProperty("source").GetString());
        Assert.Equal("library", classification.GetProperty("automaticKind").GetString());
        Assert.False(classification.TryGetProperty("confidence", out _));
    }
}
