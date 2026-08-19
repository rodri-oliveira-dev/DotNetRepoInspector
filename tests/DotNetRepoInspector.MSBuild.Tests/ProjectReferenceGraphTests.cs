using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.MSBuild.Discovery;
using DotNetRepoInspector.MSBuild.Evaluation;
using DotNetRepoInspector.MSBuild.References;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class ProjectReferenceGraphTests
{
    [Fact]
    public async Task Build_RepresentsAProjectReferenceChain()
    {
        var graph = await BuildGraphAsync("ProjectReferences", "Chain");

        Assert.Equal(
            new[] { "B/B.csproj" },
            Project(graph, "A/A.csproj").References.Select(reference => reference.Path));
        Assert.Equal(
            new[] { "C/C.csproj" },
            Project(graph, "B/B.csproj").References.Select(reference => reference.Path));
        Assert.Empty(Project(graph, "C/C.csproj").References);
    }

    [Fact]
    public async Task Build_OrdersFanOutReferencesDeterministically()
    {
        var graph = await BuildGraphAsync("ProjectReferences", "FanOut");

        Assert.Equal(
            new[] { "B/B.csproj", "C/C.csproj" },
            Project(graph, "A/A.csproj").References.Select(reference => reference.Path));
    }

    [Fact]
    public async Task Build_PreservesCircularReferencesWithoutRecursing()
    {
        var graph = await BuildGraphAsync("ProjectReferences", "Circular");

        Assert.Equal(
            new[] { "B/B.csproj" },
            Project(graph, "A/A.csproj").References.Select(reference => reference.Path));
        Assert.Equal(
            new[] { "A/A.csproj" },
            Project(graph, "B/B.csproj").References.Select(reference => reference.Path));
    }

    [Fact]
    public async Task Build_UsesEvaluatedProjectReferenceConditions()
    {
        var graph = await BuildGraphAsync("ProjectReferences", "Conditional");

        Assert.Equal(
            new[] { "Enabled/Enabled.csproj" },
            Project(graph, "App/App.csproj").References.Select(reference => reference.Path));
    }

    [Fact]
    public async Task Build_ReportsUnresolvedProjectReferencesWithoutDroppingTheEdge()
    {
        var graph = await BuildGraphAsync("ProjectReferences", "Unresolved");
        var app = Project(graph, "App/App.csproj");

        Assert.Equal(
            new[] { "Missing/Missing.csproj" },
            app.References.Select(reference => reference.Path));

        var diagnostic = Assert.Single(app.Diagnostics);
        Assert.Equal(InspectionDiagnosticCodes.ProjectReferenceUnresolved, diagnostic.Code);
        Assert.Equal(InspectionDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("App/App.csproj", diagnostic.Source);
        Assert.NotNull(diagnostic.Context);
        Assert.Equal("Missing/Missing.csproj", diagnostic.Context["referencePath"]);
    }

    [Fact]
    public async Task Build_KeepsExistingReferencesOutsideTheRepositoryRoot()
    {
        var graph = await BuildGraphAsync("ProjectReferences", "External", "Repository");
        var app = Project(graph, "App/App.csproj");

        Assert.Equal(
            new[] { "../Shared/Shared.csproj" },
            app.References.Select(reference => reference.Path));
        Assert.Empty(app.Diagnostics);
    }

    private static async Task<ProjectReferenceGraph> BuildGraphAsync(params string[] fixtureSegments)
    {
        var repositoryRoot = FixturePath(fixtureSegments);
        var discoverer = new FileSystemProjectDiscoverer();
        var evaluator = new MsBuildProjectFactsEvaluator();
        var projectFacts = new Dictionary<string, MsBuildProjectFacts>(StringComparer.Ordinal);

        foreach (var project in discoverer.Discover(new ProjectDiscoveryRequest(repositoryRoot)))
        {
            var projectPath = Path.Combine(
                repositoryRoot,
                project.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var result = await evaluator.EvaluateAsync(
                projectPath,
                TestContext.Current.CancellationToken);

            Assert.True(result.Succeeded, result.Error?.Message ?? $"Evaluation failed for {project.RelativePath}.");
            Assert.NotNull(result.Facts);
            projectFacts.Add(project.RelativePath, result.Facts);
        }

        return ProjectReferenceGraphBuilder.Build(repositoryRoot, projectFacts);
    }

    private static ProjectReferenceGraphNode Project(
        ProjectReferenceGraph graph,
        string projectPath) =>
        graph.Projects.Single(project =>
            string.Equals(project.ProjectPath, projectPath, StringComparison.Ordinal));

    private static string FixturePath(params string[] segments) =>
        segments.Aggregate(
            Path.Combine(AppContext.BaseDirectory, "Fixtures"),
            Path.Combine);
}
