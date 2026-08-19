using DotNetRepoInspector.MSBuild.Discovery;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class ProjectDiscoveryTests
{
    private static readonly string[] ProjectReferenceChain =
    [
        "A/A.csproj",
        "B/B.csproj",
        "C/C.csproj"
    ];

    private static readonly string[] ProjectKinds =
    [
        "Console/Console.csproj",
        "Library/Library.csproj",
        "MultiTargeting/MultiTargeting.csproj",
        "Test/Test.csproj",
        "Web/Web.csproj",
        "Worker/Worker.csproj"
    ];

    private static readonly string[] DiscoverableProjectOnly = ["App/App.csproj"];
    private static readonly string[] GeneratedDirectory = ["generated"];

    [Fact]
    public void Discover_ReturnsEmptyForRepositoryWithoutProjects()
    {
        var discoverer = new FileSystemProjectDiscoverer();

        var projects = discoverer.Discover(
            new ProjectDiscoveryRequest(FixturePath("EmptyRepository")));

        Assert.Empty(projects);
    }

    [Fact]
    public void Discover_ReturnsSingleProjectUsingRepositoryRelativePath()
    {
        var discoverer = new FileSystemProjectDiscoverer();

        var projects = discoverer.Discover(
            new ProjectDiscoveryRequest(FixturePath("ProjectKinds/Library")));

        var project = Assert.Single(projects);
        Assert.Equal("Library.csproj", project.RelativePath);
        Assert.False(Path.IsPathRooted(project.RelativePath));
    }

    [Fact]
    public void Discover_ReturnsMultipleProjectsInDeterministicOrder()
    {
        var discoverer = new FileSystemProjectDiscoverer();

        var projects = discoverer.Discover(
            new ProjectDiscoveryRequest(FixturePath("ProjectReferences/Chain")));

        Assert.Equal(ProjectReferenceChain, projects.Select(static project => project.RelativePath));
    }

    [Fact]
    public void Discover_FindsAllProjectKindFixtures()
    {
        var discoverer = new FileSystemProjectDiscoverer();

        var projects = discoverer.Discover(
            new ProjectDiscoveryRequest(FixturePath("ProjectKinds")));

        Assert.Equal(ProjectKinds, projects.Select(static project => project.RelativePath));
    }

    [Fact]
    public void Discover_DoesNotParseProjectContents()
    {
        var discoverer = new FileSystemProjectDiscoverer();

        var projects = discoverer.Discover(
            new ProjectDiscoveryRequest(FixturePath("InvalidProject")));

        var project = Assert.Single(projects);
        Assert.Equal("InvalidProject.csproj", project.RelativePath);
    }

    [Fact]
    public void Discover_IgnoresDefaultAndConfiguredExcludedDirectories()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Discovery-").FullName;

        try
        {
            CreateProject(repositoryRoot, "App/App.csproj");
            CreateProject(repositoryRoot, "bin/Bin.csproj");
            CreateProject(repositoryRoot, "obj/Obj.csproj");
            CreateProject(repositoryRoot, ".git/Git.csproj");
            CreateProject(repositoryRoot, "artifacts/Artifact.csproj");
            CreateProject(repositoryRoot, "generated/Generated.csproj");

            var discoverer = new FileSystemProjectDiscoverer();
            var projects = discoverer.Discover(
                new ProjectDiscoveryRequest(repositoryRoot, GeneratedDirectory));

            Assert.Equal(DiscoverableProjectOnly, projects.Select(static project => project.RelativePath));
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void Discover_IgnoresUnsupportedProjectExtensionsByDefault()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Discovery-").FullName;

        try
        {
            CreateProject(repositoryRoot, "App/App.csproj");
            CreateProject(repositoryRoot, "Other/Other.fsproj");

            var discoverer = new FileSystemProjectDiscoverer();
            var projects = discoverer.Discover(new ProjectDiscoveryRequest(repositoryRoot));

            Assert.Equal(DiscoverableProjectOnly, projects.Select(static project => project.RelativePath));
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void Discover_AllowsFutureProjectFormatsThroughDiscoveryOptions()
    {
        var repositoryRoot = Directory.CreateTempSubdirectory("DotNetRepoInspector-Discovery-").FullName;

        try
        {
            CreateProject(repositoryRoot, "Other/Other.fsproj");

            var options = new ProjectDiscoveryOptions
            {
                SupportedProjectExtensions = [".fsproj"]
            };
            var discoverer = new FileSystemProjectDiscoverer(options);
            var projects = discoverer.Discover(new ProjectDiscoveryRequest(repositoryRoot));

            var project = Assert.Single(projects);
            Assert.Equal("Other/Other.fsproj", project.RelativePath);
        }
        finally
        {
            Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Fact]
    public void Discover_RejectsConfiguredExclusionOutsideRepositoryRoot()
    {
        var discoverer = new FileSystemProjectDiscoverer();
        var request = new ProjectDiscoveryRequest(
            FixturePath("ProjectKinds"),
            ["../outside"]);

        Assert.Throws<ArgumentException>(() => discoverer.Discover(request));
    }

    [Fact]
    public void Discover_PropagatesCancellation()
    {
        var discoverer = new FileSystemProjectDiscoverer();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Assert.Throws<OperationCanceledException>(() => discoverer.Discover(
            new ProjectDiscoveryRequest(FixturePath("ProjectKinds")),
            cancellationSource.Token));
    }

    private static string FixturePath(string relativePath)
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void CreateProject(string repositoryRoot, string relativePath)
    {
        var projectPath = Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    }
}
