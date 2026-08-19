using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

using Xunit;

namespace DotNetRepoInspector.MSBuild.Tests;

public sealed class FixtureMatrixTests
{
    private static readonly string _fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static readonly string[] _expectedSignals =
    [
        "conditional-property",
        "compatibility-net10",
        "compatibility-net8",
        "console-output",
        "directory-build-props",
        "empty-repository",
        "global-json-absent",
        "global-json-present",
        "invalid-project",
        "library-output",
        "multi-targeting",
        "path-casing",
        "project-reference-chain",
        "project-reference-circular",
        "project-reference-conditional",
        "project-reference-external",
        "project-reference-fan-out",
        "project-reference-simple",
        "project-reference-unresolved",
        "sdk-resolution-missing",
        "test-project",
        "web-sdk",
        "worker-sdk"
    ];

    [Fact]
    public void Catalog_CoversAllRequiredScenarios()
    {
        using JsonDocument document = LoadCatalog();
        JsonElement fixtures = document.RootElement.GetProperty("fixtures");

        string[] actualSignals = fixtures
            .EnumerateArray()
            .SelectMany(fixture => fixture.GetProperty("proves").EnumerateArray())
            .Select(signal => signal.GetString() ?? string.Empty)
            .OrderBy(signal => signal, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(_expectedSignals, actualSignals);
    }

    [Fact]
    public void Catalog_PathsAreRelativeUniqueAndDocumented()
    {
        using JsonDocument document = LoadCatalog();
        JsonElement fixtures = document.RootElement.GetProperty("fixtures");
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> paths = new(StringComparer.Ordinal);

        foreach (JsonElement fixture in fixtures.EnumerateArray())
        {
            string id = fixture.GetProperty("id").GetString() ?? string.Empty;
            string relativePath = fixture.GetProperty("path").GetString() ?? string.Empty;

            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.True(ids.Add(id), $"Duplicate fixture id: {id}");

            Assert.False(string.IsNullOrWhiteSpace(relativePath));
            Assert.False(Path.IsPathRooted(relativePath));
            Assert.False(relativePath.Contains("..", StringComparison.Ordinal));
            Assert.True(paths.Add(relativePath), $"Duplicate fixture path: {relativePath}");

            string fullPath = ResolveFixturePath(relativePath);
            Assert.True(Directory.Exists(fullPath), $"Fixture directory not found: {relativePath}");
            Assert.True(
                File.Exists(Path.Combine(fullPath, "README.md")),
                $"Fixture documentation not found: {relativePath}/README.md");
        }
    }

    [Fact]
    public void IsolationFiles_StopParentBuildAndPackageConfiguration()
    {
        string[] isolationFiles =
        [
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props"
        ];

        foreach (string fileName in isolationFiles)
        {
            string content = File.ReadAllText(Path.Combine(_fixtureRoot, fileName)).Trim();
            Assert.Equal("<Project />", content);
        }
    }

    [Fact]
    public void ValidProjectFixtures_AreMinimalAndPortable()
    {
        string invalidProjectPath = ResolveFixturePath("InvalidProject/InvalidProject.csproj");

        string[] projectFiles = Directory
            .EnumerateFiles(_fixtureRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, invalidProjectPath, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(projectFiles);

        foreach (string projectFile in projectFiles)
        {
            XDocument document = XDocument.Load(projectFile);
            Assert.Equal("Project", document.Root?.Name.LocalName);
            Assert.Empty(document.Descendants("PackageReference"));

            foreach (XElement projectReference in document.Descendants("ProjectReference"))
            {
                string include = projectReference.Attribute("Include")?.Value ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(include));
                Assert.False(Path.IsPathRooted(include));
            }
        }
    }

    [Fact]
    public void ProjectReferenceFixtures_HaveExpectedGraphShapes()
    {
        AssertProjectReferences(
            "ProjectReferences/Simple/App/App.csproj",
            "../Library/Library.csproj");

        AssertProjectReferences(
            "ProjectReferences/Chain/A/A.csproj",
            "../B/B.csproj");
        AssertProjectReferences(
            "ProjectReferences/Chain/B/B.csproj",
            "../C/C.csproj");
        AssertProjectReferences("ProjectReferences/Chain/C/C.csproj");

        AssertProjectReferences(
            "ProjectReferences/Circular/A/A.csproj",
            "../B/B.csproj");
        AssertProjectReferences(
            "ProjectReferences/Circular/B/B.csproj",
            "../A/A.csproj");

        AssertProjectReferences(
            "ProjectReferences/FanOut/A/A.csproj",
            "../C/C.csproj",
            "../B/B.csproj");

        AssertProjectReferences(
            "ProjectReferences/Conditional/App/App.csproj",
            "../Enabled/Enabled.csproj",
            "../Disabled/Disabled.csproj");

        AssertProjectReferences(
            "ProjectReferences/Unresolved/App/App.csproj",
            "../Missing/Missing.csproj");

        AssertProjectReferences(
            "ProjectReferences/External/Repository/App/App.csproj",
            "../../Shared/Shared.csproj");
    }

    [Fact]
    public void SdkFixtures_RepresentGlobalJsonPresenceAndAbsence()
    {
        string withGlobalJson = ResolveFixturePath("Sdk/WithGlobalJson/global.json");
        string withoutGlobalJsonRoot = ResolveFixturePath("Sdk/WithoutGlobalJson");

        Assert.True(File.Exists(withGlobalJson));
        Assert.Empty(
            Directory.EnumerateFiles(
                withoutGlobalJsonRoot,
                "global.json",
                SearchOption.AllDirectories));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(withGlobalJson));
        JsonElement sdk = document.RootElement.GetProperty("sdk");

        Assert.Equal("10.0.100", sdk.GetProperty("version").GetString());
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());
    }

    [Fact]
    public void InvalidProjectFixture_IsActuallyMalformed()
    {
        string invalidProjectPath = ResolveFixturePath("InvalidProject/InvalidProject.csproj");

        Assert.Throws<XmlException>(() => XDocument.Load(invalidProjectPath));
    }

    [Fact]
    public void EmptyRepositoryFixture_ContainsNoProjects()
    {
        string emptyRepository = ResolveFixturePath("EmptyRepository");

        Assert.Empty(
            Directory.EnumerateFiles(
                emptyRepository,
                "*.csproj",
                SearchOption.AllDirectories));
    }

    private static JsonDocument LoadCatalog()
    {
        string catalogPath = Path.Combine(_fixtureRoot, "catalog.json");
        return JsonDocument.Parse(File.ReadAllText(catalogPath));
    }

    private static string ResolveFixturePath(string relativePath)
    {
        string normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_fixtureRoot, normalizedPath);
    }

    private static void AssertProjectReferences(
        string relativeProjectPath,
        params string[] expectedReferences)
    {
        string projectPath = ResolveFixturePath(relativeProjectPath);
        XDocument document = XDocument.Load(projectPath);

        string[] actualReferences = document
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.Equal(expectedReferences, actualReferences);
    }
}
