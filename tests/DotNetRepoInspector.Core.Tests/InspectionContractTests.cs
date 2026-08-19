using System.Text.Json;

using DotNetRepoInspector.Core.Contracts;

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
            new[] { "schemaVersion", "repository", "dotNetSdk", "projects", "diagnostics" },
            root.EnumerateObject().Select(property => property.Name).ToArray());

        var repository = root.GetProperty("repository");
        Assert.Equal("sample", repository.GetProperty("name").GetString());
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
    }

    [Fact]
    public void Serialize_IsDeterministicAcrossCollectionOrder()
    {
        var first = InspectionJsonSerializer.Serialize(CreateReport(reverseCollections: true));
        var second = InspectionJsonSerializer.Serialize(CreateReport(reverseCollections: false));

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("1.0", true)]
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
                new InspectionDiagnostic("Z002", "warning", "Second", null, null),
                new InspectionDiagnostic("A001", "error", "First", "repository", null)
            }
            : new[]
            {
                new InspectionDiagnostic("A001", "error", "First", "repository", null),
                new InspectionDiagnostic("Z002", "warning", "Second", null, null)
            };

        return new InspectionReport(
            InspectionSchema.CurrentVersion,
            new RepositoryMetadata("sample", "abc123", null, null),
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
