using System.Text.Json;
using System.Xml.Linq;

using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.Cli.Tests;

public sealed class ReleaseReadinessTests
{
    [Fact]
    public void V1Baseline_MatchesProductActionAndSchemaContracts()
    {
        JsonElement baseline = LoadBaseline();
        string productVersion = RequiredString(baseline, "productVersion");
        string schemaVersion = RequiredString(baseline, "schemaVersion");
        string actionMajorAlias = RequiredString(baseline, "actionMajorAlias");

        Assert.True(Version.TryParse(productVersion, out Version? parsedProductVersion));
        Assert.NotNull(parsedProductVersion);
        Assert.Equal("1.0.0", productVersion);

        string actionVersion = ReadActionToolVersion();
        Assert.Equal(productVersion, actionVersion);
        Assert.Equal($"v{parsedProductVersion.Major}", actionMajorAlias);

        Assert.Equal(InspectionSchema.CurrentVersion, schemaVersion);
        Assert.True(Version.TryParse(schemaVersion, out Version? parsedSchemaVersion));
        Assert.NotNull(parsedSchemaVersion);
        Assert.Equal(parsedProductVersion.Major, parsedSchemaVersion.Major);
        Assert.Equal(InspectionSchema.CurrentMajorVersion, parsedSchemaVersion.Major);
    }

    [Fact]
    public void V1Baseline_MatchesPackageMetadataAndCanonicalSchemaExample()
    {
        JsonElement baseline = LoadBaseline();
        JsonElement package = baseline.GetProperty("package");
        XDocument project = XDocument.Load(
            Path.Combine(
                RepositoryRoot,
                "src",
                "DotNetRepoInspector.Cli",
                "DotNetRepoInspector.Cli.csproj"));

        Assert.Equal("true", ProjectProperty(project, "IsPackable"));
        Assert.Equal("true", ProjectProperty(project, "PackAsTool"));
        Assert.Equal(RequiredString(package, "id"), ProjectProperty(project, "PackageId"));
        Assert.Equal(
            RequiredString(package, "toolCommandName"),
            ProjectProperty(project, "ToolCommandName"));
        Assert.Equal(
            RequiredString(package, "targetFramework"),
            ProjectProperty(project, "TargetFramework"));
        Assert.Equal(
            RequiredString(package, "license"),
            ProjectProperty(project, "PackageLicenseExpression"));
        Assert.Equal(
            RequiredString(package, "readme"),
            ProjectProperty(project, "PackageReadmeFile"));
        Assert.Equal(
            RequiredString(package, "repositoryUrl"),
            ProjectProperty(project, "RepositoryUrl"));
        Assert.Equal(
            RequiredString(package, "repositoryUrl"),
            ProjectProperty(project, "PackageProjectUrl"));

        string examplePath = Path.Combine(
            RepositoryRoot,
            RequiredString(baseline, "schemaExample").Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument example = JsonDocument.Parse(File.ReadAllText(examplePath));
        Assert.Equal(
            RequiredString(baseline, "schemaVersion"),
            example.RootElement.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public void V1Baseline_RequiresGovernanceSecurityAndReleaseDocumentation()
    {
        JsonElement baseline = LoadBaseline();
        string[] requiredFiles = baseline
            .GetProperty("requiredFiles")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        Assert.NotEmpty(requiredFiles);

        foreach (string relativePath in requiredFiles)
        {
            string fullPath = Path.Combine(
                RepositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullPath), $"Required v1 release file '{relativePath}' was not found.");
        }
    }

    [Fact]
    public void PublicReadmes_DescribeTheActualV1Contract()
    {
        string english = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));
        string portuguese = File.ReadAllText(Path.Combine(RepositoryRoot, "README.pt-BR.md"));

        Assert.Contains("v1.0.0 release candidate", english, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": \"1.3\"", english, StringComparison.Ordinal);
        Assert.DoesNotContain("Status: early development", english, StringComparison.Ordinal);
        Assert.DoesNotContain("The exact schema is not final", english, StringComparison.Ordinal);

        Assert.Contains("candidata à release v1.0.0", portuguese, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": \"1.3\"", portuguese, StringComparison.Ordinal);
        Assert.DoesNotContain("Status: desenvolvimento inicial", portuguese, StringComparison.Ordinal);
        Assert.DoesNotContain("O schema exato ainda não é definitivo", portuguese, StringComparison.Ordinal);

        string englishReadiness = File.ReadAllText(
            Path.Combine(RepositoryRoot, "docs", "en", "v1-release-readiness.md"));
        string portugueseReadiness = File.ReadAllText(
            Path.Combine(RepositoryRoot, "docs", "pt-BR", "v1-release-readiness.md"));

        Assert.Contains("GitHub Environment `release`", englishReadiness, StringComparison.Ordinal);
        Assert.Contains("Trusted Publishing", englishReadiness, StringComparison.Ordinal);
        Assert.Contains("publish=false", englishReadiness, StringComparison.Ordinal);
        Assert.Contains("publish=true", englishReadiness, StringComparison.Ordinal);

        Assert.Contains("GitHub Environment `release`", portugueseReadiness, StringComparison.Ordinal);
        Assert.Contains("Trusted Publishing", portugueseReadiness, StringComparison.Ordinal);
        Assert.Contains("publish=false", portugueseReadiness, StringComparison.Ordinal);
        Assert.Contains("publish=true", portugueseReadiness, StringComparison.Ordinal);
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static JsonElement LoadBaseline()
    {
        string path = Path.Combine(RepositoryRoot, ".github", "release-readiness-v1.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static string ReadActionToolVersion()
    {
        string[] matches = File
            .ReadLines(Path.Combine(RepositoryRoot, "action.yml"))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("DRI_TOOL_VERSION:", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(matches);

        string value = matches[0]["DRI_TOOL_VERSION:".Length..].Trim();
        return value.Trim('"', '\'');
    }

    private static string ProjectProperty(XDocument document, string name)
    {
        XElement? element = document
            .Descendants()
            .FirstOrDefault(candidate => string.Equals(candidate.Name.LocalName, name, StringComparison.Ordinal));

        Assert.NotNull(element);
        return element.Value.Trim();
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        string? value = element.GetProperty(propertyName).GetString();
        Assert.False(string.IsNullOrWhiteSpace(value));
        return value!;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DotNetRepoInspector.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the DotNetRepoInspector repository root.");
    }
}
