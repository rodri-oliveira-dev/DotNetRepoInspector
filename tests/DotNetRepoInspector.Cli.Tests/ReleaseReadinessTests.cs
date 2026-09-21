using System.Text.Json;
using System.Xml.Linq;

using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.Cli.Tests;

public sealed class ReleaseReadinessTests
{
    [Fact]
    public void V1Baseline_MatchesProductAndSchemaContracts()
    {
        JsonElement baseline = LoadBaseline();
        string productVersion = RequiredString(baseline, "productVersion");
        string schemaVersion = RequiredString(baseline, "schemaVersion");
        string actionMajorAlias = RequiredString(baseline, "actionMajorAlias");

        Assert.True(Version.TryParse(productVersion, out Version? parsedProductVersion));
        Assert.NotNull(parsedProductVersion);
        Assert.Equal("1.0.0", productVersion);

        Assert.Equal($"v{parsedProductVersion.Major}", actionMajorAlias);

        string actionMetadata = File.ReadAllText(Path.Combine(RepositoryRoot, "action.yml"));
        Assert.DoesNotContain("DRI_TOOL_VERSION", actionMetadata, StringComparison.Ordinal);

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
    public void NuGetPackageIcon_ExistsAndRespectsNuGetSizeLimit()
    {
        string iconPath = Path.Combine(RepositoryRoot, "resource", "nuget-icon.png");
        var icon = new FileInfo(iconPath);

        Assert.True(icon.Exists, "The NuGet package icon was not found.");
        Assert.InRange(icon.Length, 1, 1_000_000);

        foreach (string projectPath in new[]
        {
            Path.Combine("src", "DotNetRepoInspector.Cli", "DotNetRepoInspector.Cli.csproj"),
            Path.Combine("src", "DotNetRepoInspector.Mcp", "DotNetRepoInspector.Mcp.csproj"),
        })
        {
            XDocument project = XDocument.Load(Path.Combine(RepositoryRoot, projectPath));
            Assert.Equal("nuget-icon.png", ProjectProperty(project, "PackageIcon"));
        }
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
    public void V1Baseline_RecognizesMcpAsReleaseReadyPackage()
    {
        JsonElement mcp = LoadBaseline().GetProperty("mcp");
        string projectPath = RequiredString(mcp, "project");
        XDocument project = XDocument.Load(Path.Combine(
            RepositoryRoot,
            projectPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal("DotNetRepoInspector.Mcp", RequiredString(mcp, "packageId"));
        Assert.Equal(RequiredString(mcp, "packageId"), ProjectProperty(project, "PackageId"));
        Assert.Equal("net10.0", ProjectProperty(project, "TargetFramework"));
        Assert.Equal("true", ProjectProperty(project, "IsPackable"));
        Assert.Equal("true", ProjectProperty(project, "PackAsTool"));
        Assert.Equal("dotnet-repo-inspector-mcp", RequiredString(mcp, "toolCommandName"));
        Assert.Equal("McpServer", RequiredString(mcp, "packageType"));
        Assert.Equal("McpServer", ProjectProperty(project, "PackageType"));
        Assert.Equal(".mcp/server.json", RequiredString(mcp, "manifest"));
        Assert.Equal("framework-dependent", RequiredString(mcp, "distribution"));
        Assert.Equal("stdio", RequiredString(mcp, "transport"));
        Assert.Equal("release-ready", RequiredString(mcp, "publicationStatus"));
        Assert.Equal(136, mcp.GetProperty("publicationIssue").GetInt32());

        var controls = mcp.GetProperty("requiredControls")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Contains("hermetic-e2e", controls);
        Assert.Contains("security-tests", controls);
        Assert.Contains("performance-baseline", controls);
    }

    [Fact]
    public void McpReleaseCandidate_RecordsVersionAndUnresolvedPromotionGates()
    {
        JsonElement releaseCandidate = LoadBaseline()
            .GetProperty("mcp")
            .GetProperty("releaseCandidate");

        Assert.Equal("1.2.0-rc.1", RequiredString(releaseCandidate, "version"));
        Assert.Equal("blocked", RequiredString(releaseCandidate, "status"));
        Assert.Equal(137, releaseCandidate.GetProperty("readinessIssue").GetInt32());
        Assert.Equal(
            "docs/en/mcp-release-candidate.md",
            RequiredString(releaseCandidate, "evidence"));

        int[] blockingIssues = releaseCandidate
            .GetProperty("blockingIssues")
            .EnumerateArray()
            .Select(static item => item.GetInt32())
            .ToArray();
        Assert.Empty(blockingIssues);

        string[] externalBlockers = releaseCandidate
            .GetProperty("externalBlockers")
            .EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        Assert.Contains("nuget-trusted-publishing-policy", externalBlockers);
    }

    [Fact]
    public void McpGeneralAvailability_FreezesContractAndRemainsBlockedByRequiredEvidence()
    {
        JsonElement generalAvailability = LoadBaseline()
            .GetProperty("mcp")
            .GetProperty("generalAvailability");

        Assert.Equal("1.2.0", RequiredString(generalAvailability, "version"));
        Assert.Equal("blocked", RequiredString(generalAvailability, "status"));
        Assert.Equal(138, generalAvailability.GetProperty("readinessIssue").GetInt32());
        Assert.Equal(
            ".github/mcp-performance-baseline.json",
            RequiredString(generalAvailability, "performanceBaseline"));

        string[] tools = generalAvailability
            .GetProperty("frozenTools")
            .EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        Assert.Equal(
            [
                "inspect_repository",
                "list_projects",
                "get_project_details",
                "get_project_reference_graph",
                "get_repository_diagnostics",
                "get_sdk_metadata",
            ],
            tools);

        int[] blockingIssues = generalAvailability
            .GetProperty("blockingIssues")
            .EnumerateArray()
            .Select(static item => item.GetInt32())
            .ToArray();
        Assert.Equal([137], blockingIssues);

        string[] externalBlockers = generalAvailability
            .GetProperty("externalBlockers")
            .EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        Assert.Contains("nuget-trusted-publishing-policy", externalBlockers);
        Assert.Contains("protected-release-approval", externalBlockers);
        Assert.Contains("merge-to-allowed-release-ref", externalBlockers);
    }

    [Fact]
    public void PublicReadmes_DescribeTheActualV1Contract()
    {
        string english = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));
        string portuguese = File.ReadAllText(Path.Combine(RepositoryRoot, "README.pt-BR.md"));

        Assert.Contains("v1.0.0 stable baseline", english, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": \"1.3\"", english, StringComparison.Ordinal);
        Assert.DoesNotContain("Status: early development", english, StringComparison.Ordinal);
        Assert.DoesNotContain("The exact schema is not final", english, StringComparison.Ordinal);
        Assert.DoesNotContain("release candidate", english, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("baseline estável v1.0.0", portuguese, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": \"1.3\"", portuguese, StringComparison.Ordinal);
        Assert.DoesNotContain("Status: desenvolvimento inicial", portuguese, StringComparison.Ordinal);
        Assert.DoesNotContain("O schema exato ainda não é definitivo", portuguese, StringComparison.Ordinal);
        Assert.DoesNotContain("candidata à release", portuguese, StringComparison.OrdinalIgnoreCase);

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

    [Fact]
    public void McpPackageMetadata_IsBomFreeAndValidatedBeforePublication()
    {
        string projectPath = Path.Combine(
            RepositoryRoot,
            "src",
            "DotNetRepoInspector.Mcp",
            "DotNetRepoInspector.Mcp.csproj");
        string projectText = File.ReadAllText(projectPath);
        XDocument project = XDocument.Load(projectPath);

        XElement? manifestWriter = project
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "WriteLinesToFile", StringComparison.Ordinal) &&
                string.Equals(
                    element.Attribute("File")?.Value,
                    "$(McpManifestPath)",
                    StringComparison.Ordinal));

        Assert.NotNull(manifestWriter);
        Assert.Null(manifestWriter.Attribute("Encoding"));
        Assert.DoesNotContain("&quot;valueHint&quot;: &quot;repository_root&quot;", projectText, StringComparison.Ordinal);

        string validator = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "scripts",
            "validate_mcp_package.ps1"));

        Assert.Contains("UTF-8 without a BOM", validator, StringComparison.Ordinal);
        Assert.Contains("20000", validator, StringComparison.Ordinal);
        Assert.Contains("mcp-publisher", validator, StringComparison.Ordinal);

        string workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "workflows",
            "release.yml"));

        Assert.Contains("MCP_PUBLISHER_VERSION: \"1.8.1\"", workflow, StringComparison.Ordinal);
        Assert.Contains("a06c9096dcb9727c13555b6be26c7effa707b01f06a4c561ba7a3635443cf2cc", workflow, StringComparison.Ordinal);
        Assert.Contains("-McpPublisherPath $env:MCP_PUBLISHER_PATH", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_ProtectsMcpPackagingAndTrustedPublication()
    {
        string workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "workflows",
            "release.yml"));

        Assert.Contains("validate_mcp_package.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("./.github/scripts/validate_mcp_rc.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("DotNetRepoInspector.Mcp.${RELEASE_VERSION}.nupkg", workflow, StringComparison.Ordinal);
        Assert.Contains("DotNetRepoInspector.Mcp.${RELEASE_VERSION}.snupkg", workflow, StringComparison.Ordinal);
        Assert.Contains("invoke_mcp_package_smoke.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("id-token: write", workflow, StringComparison.Ordinal);
        Assert.Contains("NuGet/login@", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: release", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("NUGET_API_KEY: ${{ secrets.", workflow, StringComparison.Ordinal);
        Assert.Contains("--skip-duplicate", workflow, StringComparison.Ordinal);
        Assert.Contains("Wait for MCP package propagation on NuGet.org", workflow, StringComparison.Ordinal);
        Assert.Contains("v3-flatcontainer/$packageId/index.json", workflow, StringComparison.Ordinal);
        Assert.Contains("$maxAttempts = 10", workflow, StringComparison.Ordinal);
        Assert.Contains("[Math]::Pow(2, $attempt - 1)", workflow, StringComparison.Ordinal);

        Assert.Contains("name: Validate, build, test and pack", workflow, StringComparison.Ordinal);
        Assert.Contains("create_release_tag:", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Create release tag", workflow, StringComparison.Ordinal);
        Assert.Contains("publish_nuget:", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Publish to NuGet.org", workflow, StringComparison.Ordinal);
        Assert.Contains("publish_github_packages:", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Publish to GitHub Packages", workflow, StringComparison.Ordinal);
        Assert.Contains("publish_container:", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Publish container images", workflow, StringComparison.Ordinal);
        Assert.Contains("create_github_release:", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Create GitHub Release", workflow, StringComparison.Ordinal);
        Assert.Contains("needs: [build, create_release_tag]", workflow, StringComparison.Ordinal);
        Assert.Contains("- publish_nuget", workflow, StringComparison.Ordinal);
        Assert.Contains("- publish_github_packages", workflow, StringComparison.Ordinal);
        Assert.Contains("- publish_container", workflow, StringComparison.Ordinal);

        int tagIndex = workflow.IndexOf("name: Create release tag", StringComparison.Ordinal);
        int nugetIndex = workflow.IndexOf("name: Publish to NuGet.org", StringComparison.Ordinal);
        int packagesIndex = workflow.IndexOf("name: Publish to GitHub Packages", StringComparison.Ordinal);
        int containerIndex = workflow.IndexOf("name: Publish container images", StringComparison.Ordinal);
        int releaseIndex = workflow.IndexOf("name: Create GitHub Release", StringComparison.Ordinal);
        Assert.True(tagIndex >= 0);
        Assert.True(nugetIndex > tagIndex);
        Assert.True(packagesIndex > tagIndex);
        Assert.True(containerIndex > tagIndex);
        Assert.True(releaseIndex > nugetIndex);
        Assert.True(releaseIndex > packagesIndex);
        Assert.True(releaseIndex > containerIndex);
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static JsonElement LoadBaseline()
    {
        string path = Path.Combine(RepositoryRoot, ".github", "release-readiness-v1.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
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
