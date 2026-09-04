using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.Core.Tests;

public sealed class InspectionHealthEvaluatorTests
{
    [Fact]
    public void Evaluate_SeparatesRepositoryHealthFromProjectHealth()
    {
        var cleanProject = CreateProject("src/Clean/Clean.csproj");
        var warningProject = CreateProject(
            "src/Warning/Warning.csproj",
            new InspectionDiagnostic(
                "DRI1003",
                InspectionDiagnosticSeverity.Warning,
                "A project reference could not be resolved.",
                "src/Warning/Warning.csproj",
                null));
        var errorProject = CreateProject(
            "src/Broken/Broken.csproj",
            new InspectionDiagnostic(
                "DRI1006",
                InspectionDiagnosticSeverity.Error,
                "MSBuild could not evaluate the project.",
                "src/Broken/Broken.csproj",
                null));

        var report = InspectionReport.Create(
            new RepositoryMetadata("sample", null, null, null, null),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            [cleanProject, warningProject, errorProject],
            [
                new InspectionDiagnostic(
                    "DRI1012",
                    InspectionDiagnosticSeverity.Warning,
                    "Repository metadata could not be fully collected.",
                    "repository",
                    null)
            ]);

        var health = InspectionHealthEvaluator.Evaluate(report);

        Assert.Equal(InspectionHealthStatus.Error, health.OverallStatus);
        Assert.Equal(InspectionHealthStatus.Warning, health.RepositoryStatus);
        Assert.Equal(new InspectionDiagnosticCounts(1, 0, 1, 0), health.RepositoryDiagnostics);
        Assert.Equal(new InspectionDiagnosticCounts(2, 0, 1, 1), health.ProjectDiagnostics);
        Assert.Equal(2, health.ProjectsWithDiagnostics);
        Assert.Equal(1, health.ProjectsWithWarnings);
        Assert.Equal(1, health.ProjectsWithErrors);

        Assert.Equal(
            InspectionHealthStatus.Ok,
            InspectionHealthEvaluator.GetProjectStatus(cleanProject));
        Assert.Equal(
            InspectionHealthStatus.Warning,
            InspectionHealthEvaluator.GetProjectStatus(warningProject));
        Assert.Equal(
            InspectionHealthStatus.Error,
            InspectionHealthEvaluator.GetProjectStatus(errorProject));
    }

    [Fact]
    public void Evaluate_ProjectErrorDoesNotChangeRepositoryStatus()
    {
        var cleanProject = CreateProject("src/Clean/Clean.csproj");
        var brokenProject = CreateProject(
            "src/Broken/Broken.csproj",
            new InspectionDiagnostic(
                "DRI1006",
                InspectionDiagnosticSeverity.Error,
                "MSBuild could not evaluate the project.",
                "src/Broken/Broken.csproj",
                null));

        var report = InspectionReport.Create(
            new RepositoryMetadata("sample", null, null, null, null),
            new DotNetSdkMetadata(null, null, "10.0.400"),
            [cleanProject, brokenProject],
            Array.Empty<InspectionDiagnostic>());

        var health = InspectionHealthEvaluator.Evaluate(report);

        Assert.Equal(InspectionHealthStatus.Error, health.OverallStatus);
        Assert.Equal(InspectionHealthStatus.Ok, health.RepositoryStatus);
        Assert.Equal(1, health.ProjectsWithDiagnostics);
        Assert.Equal(1, health.ProjectsWithErrors);
        Assert.Equal(
            InspectionHealthStatus.Ok,
            InspectionHealthEvaluator.GetProjectStatus(cleanProject));
    }

    private static ProjectInspection CreateProject(
        string path,
        params InspectionDiagnostic[] diagnostics) =>
        new(
            path,
            Path.GetFileNameWithoutExtension(path),
            "10.0.400",
            [new ProjectSdkMetadata("Microsoft.NET.Sdk", null)],
            ["net10.0"],
            "Library",
            false,
            true,
            Array.Empty<string>(),
            new ProjectClassification("library", "high", ["output-type:Library"]),
            Array.Empty<ProjectReferenceMetadata>(),
            diagnostics);
}
