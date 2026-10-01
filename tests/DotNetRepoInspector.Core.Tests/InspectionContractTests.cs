using System.Text.Json;

using DotNetRepoInspector.Core.Contracts;
using DotNetRepoInspector.Core.Policies;

using Xunit;

namespace DotNetRepoInspector.Core.Tests;

public sealed class InspectionContractTests
{
    [Fact]
    public void Serialize_ProducesVersionedCamelCaseCanonicalContract()
    {
        var report = CreateReport(reverseCollections: true);

        var json = InspectionJsonSerializer.Serialize(report);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(InspectionSchema.CurrentVersion, root.GetProperty("schemaVersion").GetString());
        Assert.Equal(
            new[] { "schemaVersion", "repository", "dotNetSdk", "projects", "diagnostics", "policyFindings", "integrations" },
            root.EnumerateObject().Select(property => property.Name).ToArray());

        var repository = root.GetProperty("repository");
        Assert.Equal("sample", repository.GetProperty("name").GetString());
        Assert.False(repository.GetProperty("isDirty").GetBoolean());
        Assert.False(repository.TryGetProperty("branch", out _));

        var projects = root.GetProperty("projects").EnumerateArray().ToArray();
        Assert.Equal("src/App/App.csproj", projects[0].GetProperty("path").GetString());
        Assert.Equal("src/Library/Library.csproj", projects[1].GetProperty("path").GetString());

        Assert.Equal(
            new[] { "net10.0", "net8.0" },
            projects[0]
                .GetProperty("targetFrameworks")
                .EnumerateArray()
                .Select(element => element.GetString())
                .ToArray());
        Assert.Equal(
            new[] { "linux-x64", "win-x64" },
            projects[0]
                .GetProperty("runtimeIdentifiers")
                .EnumerateArray()
                .Select(element => element.GetString())
                .ToArray());
        Assert.Equal(
            new[] { "signal-a", "signal-z" },
            projects[0]
                .GetProperty("classification")
                .GetProperty("signals")
                .EnumerateArray()
                .Select(element => element.GetString())
                .ToArray());
        Assert.False(projects[0].GetProperty("classification").TryGetProperty("subtype", out _));
        Assert.Empty(root.GetProperty("policyFindings").EnumerateArray());
        Assert.Empty(root.GetProperty("integrations").EnumerateArray());
    }

    [Fact]
    public void Serialize_EmitsPolicyFindingsSeparateFromDiagnostics()
    {
        var report = CreateReport(reverseCollections: false) with
        {
            PolicyFindings =
            [
                new PolicyFinding(
                    TargetFrameworkPolicyRule.RuleCode,
                    PolicySeverity.Error,
                    "Project targets one or more frameworks that are not allowed by policy.",
                    PolicyFindingScope.Project("src\\App\\App.csproj"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["targetFrameworks"] = "net7.0",
                        ["allowedTargetFrameworks"] = "net8.0,net10.0"
                    })
            ]
        };

        var json = InspectionJsonSerializer.Serialize(report);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("diagnostics").GetArrayLength());

        var finding = root.GetProperty("policyFindings")[0];
        Assert.Equal("DRP0001", finding.GetProperty("ruleCode").GetString());
        Assert.Equal("error", finding.GetProperty("severity").GetString());
        Assert.Equal("project", finding.GetProperty("scope").GetProperty("kind").GetString());
        Assert.Equal(
            "src/App/App.csproj",
            finding.GetProperty("scope").GetProperty("projectPath").GetString());
        Assert.Equal(
            new[] { "allowedTargetFrameworks", "targetFrameworks" },
            finding.GetProperty("context")
                .EnumerateObject()
                .Select(static property => property.Name)
                .ToArray());
    }

    [Fact]
    public void Serialize_EmitsClassificationSubtypeWhenPresent()
    {
        var report = CreateReport(reverseCollections: false);
        var project = report.Projects[0];
        var reportWithSubtype = report with
        {
            Projects = new[]
            {
                project with
                {
                    Classification = project.Classification! with
                    {
                        Subtype = "sample-subtype"
                    }
                }
            }
        };

        var json = InspectionJsonSerializer.Serialize(reportWithSubtype);

        using var document = JsonDocument.Parse(json);
        var classification = document.RootElement
            .GetProperty("projects")[0]
            .GetProperty("classification");

        Assert.Equal("web", classification.GetProperty("kind").GetString());
        Assert.Equal("sample-subtype", classification.GetProperty("subtype").GetString());
    }

    [Fact]
    public void Serialize_RejectsWhitespaceClassificationSubtype()
    {
        var report = CreateReport(reverseCollections: false);
        var project = report.Projects[0];
        var reportWithWhitespaceSubtype = report with
        {
            Projects = new[]
            {
                project with
                {
                    Classification = project.Classification! with
                    {
                        Subtype = " "
                    }
                }
            }
        };

        Assert.Throws<JsonException>(() => InspectionJsonSerializer.Serialize(reportWithWhitespaceSubtype));
    }

    [Fact]
    public void Deserialize_AcceptsOlderPayloadWithoutClassificationSubtype()
    {
        var json = InspectionJsonSerializer.Serialize(CreateReport(reverseCollections: false));
        json = json.Replace(
            $"\"schemaVersion\": \"{InspectionSchema.CurrentVersion}\"",
            "\"schemaVersion\": \"1.3\"",
            StringComparison.Ordinal);

        var report = InspectionJsonSerializer.Deserialize(json);

        Assert.Equal("1.3", report.SchemaVersion);
        Assert.Null(report.Projects[0].Classification!.Subtype);
    }

    [Fact]
    public void Deserialize_AcceptsOlderPayloadWithoutPolicyFindings()
    {
        var json = InspectionJsonSerializer.Serialize(CreateReport(reverseCollections: false))
            .Replace(
                $"\"schemaVersion\": \"{InspectionSchema.CurrentVersion}\"",
                "\"schemaVersion\": \"1.4\"",
                StringComparison.Ordinal)
            .Replace(
                ",\n  \"policyFindings\": []",
                string.Empty,
                StringComparison.Ordinal);

        var report = InspectionJsonSerializer.Deserialize(json);

        Assert.Equal("1.4", report.SchemaVersion);
        Assert.Empty(report.PolicyFindings);
    }

    [Fact]
    public void Deserialize_AcceptsOlderPayloadWithoutIntegrations()
    {
        var json = InspectionJsonSerializer.Serialize(CreateReport(reverseCollections: false))
            .Replace(
                $"\"schemaVersion\": \"{InspectionSchema.CurrentVersion}\"",
                "\"schemaVersion\": \"1.5\"",
                StringComparison.Ordinal)
            .Replace(
                ",\n  \"integrations\": []",
                string.Empty,
                StringComparison.Ordinal);

        var report = InspectionJsonSerializer.Deserialize(json);

        Assert.Equal("1.5", report.SchemaVersion);
        Assert.Empty(report.Integrations);
    }

    [Fact]
    public void Serialize_NormalizesAndOrdersIntegrationFindings()
    {
        IntegrationFinding first = CreateIntegrationFinding(
            "src\\Zeta\\Zeta.csproj",
            "src\\Zeta\\Client.cs",
            20,
            ["refit:contract", "http:client"]);
        IntegrationFinding second = CreateIntegrationFinding(
            "src/Alpha/Alpha.csproj",
            "src/Alpha/Client.cs",
            10,
            ["http:client", "refit:contract"]);
        var report = CreateReport(reverseCollections: false) with
        {
            Integrations = [first, second]
        };

        using JsonDocument document = JsonDocument.Parse(InspectionJsonSerializer.Serialize(report));
        JsonElement.ArrayEnumerator integrations = document.RootElement
            .GetProperty("integrations")
            .EnumerateArray();
        JsonElement[] items = integrations.ToArray();

        Assert.Equal("src/Alpha/Alpha.csproj", items[0].GetProperty("projectPath").GetString());
        Assert.Equal("src/Zeta/Client.cs", items[1].GetProperty("source").GetProperty("path").GetString());
        Assert.Equal(
            ["http:client", "refit:contract"],
            items[1].GetProperty("signals").EnumerateArray().Select(static item => item.GetString()));
    }

    [Fact]
    public void Serialize_RejectsIntegrationIdThatDoesNotMatchCanonicalEvidence()
    {
        IntegrationFinding finding = CreateIntegrationFinding(
            "src/App/App.csproj",
            "src/App/Client.cs",
            7,
            ["http:client"]);
        var report = CreateReport(reverseCollections: false) with
        {
            Integrations = [finding with { Target = "changed-after-id-generation" }]
        };

        Assert.Throws<JsonException>(() => InspectionJsonSerializer.Serialize(report));
    }

    [Fact]
    public void Serialize_IsDeterministicAcrossCollectionOrder()
    {
        var firstReport = CreateReport(reverseCollections: true) with
        {
            PolicyFindings =
            [
                CreatePolicyFinding("DRP0002", "src/Zeta/Zeta.csproj"),
                CreatePolicyFinding("DRP0001", "src/Alpha/Alpha.csproj")
            ]
        };
        var secondReport = CreateReport(reverseCollections: false) with
        {
            PolicyFindings =
            [
                CreatePolicyFinding("DRP0001", "src/Alpha/Alpha.csproj"),
                CreatePolicyFinding("DRP0002", "src/Zeta/Zeta.csproj")
            ]
        };

        var first = InspectionJsonSerializer.Serialize(firstReport);
        var second = InspectionJsonSerializer.Serialize(secondReport);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1.2", true)]
    [InlineData("1.7", true)]
    [InlineData("2.0", false)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    public void SchemaCompatibility_IsBasedOnMajorVersion(string version, bool expected)
    {
        Assert.Equal(expected, InspectionSchema.IsCompatibleVersion(version));
    }

    [Fact]
    public void Deserialize_RejectsBreakingSchemaVersion()
    {
        var json = InspectionJsonSerializer.Serialize(CreateReport(reverseCollections: false));
        json = json.Replace(
            $"\"schemaVersion\": \"{InspectionSchema.CurrentVersion}\"",
            "\"schemaVersion\": \"2.0\"",
            StringComparison.Ordinal);

        Assert.Throws<NotSupportedException>(() => InspectionJsonSerializer.Deserialize(json));
    }

    [Fact]
    public void ExamplePayload_RoundTripsAsCanonicalJson()
    {
        var examplePath = Path.Combine(
            AppContext.BaseDirectory,
            "Examples",
            "inspection-v1.example.json");
        var example = File.ReadAllText(examplePath).TrimEnd();

        var report = InspectionJsonSerializer.Deserialize(example);
        var serialized = InspectionJsonSerializer.Serialize(report);

        Assert.Equal(InspectionSchema.CurrentVersion, report.SchemaVersion);
        Assert.Equal(example, serialized);
    }

    [Fact]
    public void CoreContract_DoesNotReferenceMicrosoftBuildAssemblies()
    {
        var references = typeof(InspectionReport)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();

        Assert.DoesNotContain(
            references,
            name => name!.StartsWith("Microsoft.Build", StringComparison.Ordinal));
    }

    private static PolicyFinding CreatePolicyFinding(string ruleCode, string projectPath) =>
        new(
            ruleCode,
            PolicySeverity.Warning,
            "Policy finding.",
            PolicyFindingScope.Project(projectPath));

    private static IntegrationFinding CreateIntegrationFinding(
        string projectPath,
        string sourcePath,
        int line,
        IReadOnlyList<string> signals) =>
        IntegrationFinding.Create(
            projectPath,
            IntegrationKind.Http,
            IntegrationDirection.Outbound,
            "refit",
            new IntegrationSourceLocation(sourcePath, line),
            IntegrationConfidence.High,
            signals,
            target: "Serasa",
            configurationKey: "Serasa:BaseUrl",
            contract: "ISerasaApi");

    private static InspectionReport CreateReport(bool reverseCollections)
    {
        var app = new ProjectInspection(
            "src\\App\\App.csproj",
            "App",
            "10.0.100",
            reverseCollections
                ? new[]
                {
                    new ProjectSdkMetadata("Example.Sdk", "2.0.0"),
                    new ProjectSdkMetadata("Microsoft.NET.Sdk.Web", null)
                }
                : new[]
                {
                    new ProjectSdkMetadata("Microsoft.NET.Sdk.Web", null),
                    new ProjectSdkMetadata("Example.Sdk", "2.0.0")
                },
            reverseCollections
                ? new[] { "net8.0", "net10.0" }
                : new[] { "net10.0", "net8.0" },
            "Exe",
            false,
            false,
            reverseCollections
                ? new[] { "win-x64", "linux-x64" }
                : new[] { "linux-x64", "win-x64" },
            new ProjectClassification(
                "web",
                "high",
                reverseCollections
                    ? new[] { "signal-z", "signal-a" }
                    : new[] { "signal-a", "signal-z" }),
            reverseCollections
                ? new[]
                {
                    new ProjectReferenceMetadata("src\\Shared\\Shared.csproj"),
                    new ProjectReferenceMetadata("src\\Core\\Core.csproj")
                }
                : new[]
                {
                    new ProjectReferenceMetadata("src\\Core\\Core.csproj"),
                    new ProjectReferenceMetadata("src\\Shared\\Shared.csproj")
                },
            Array.Empty<InspectionDiagnostic>());

        var library = new ProjectInspection(
            "src/Library/Library.csproj",
            "Library",
            "10.0.100",
            new[] { new ProjectSdkMetadata("Microsoft.NET.Sdk", null) },
            new[] { "net10.0" },
            "Library",
            false,
            true,
            Array.Empty<string>(),
            new ProjectClassification("library", null, Array.Empty<string>()),
            Array.Empty<ProjectReferenceMetadata>(),
            Array.Empty<InspectionDiagnostic>());

        var diagnostics = reverseCollections
            ? new[]
            {
                new InspectionDiagnostic("DRI9002", "warning", "Second", null, null),
                new InspectionDiagnostic("DRI9001", "error", "First", "repository", null)
            }
            : new[]
            {
                new InspectionDiagnostic("DRI9001", "error", "First", "repository", null),
                new InspectionDiagnostic("DRI9002", "warning", "Second", null, null)
            };

        return new InspectionReport(
            InspectionSchema.CurrentVersion,
            new RepositoryMetadata("sample", "abc123", null, null, false),
            new DotNetSdkMetadata(
                "..\\global.json",
                new ConfiguredDotNetSdk("10.0.100", "latestFeature", false),
                "10.0.100"),
            reverseCollections
                ? new[] { library, app }
                : new[] { app, library },
            diagnostics);
    }
}
