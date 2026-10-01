using System.Text.Json;

using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.Core.Tests;

public sealed class SecurityContractTests
{
    [Theory]
    [InlineData("line one\nline two")]
    [InlineData(" ")]
    [InlineData("Server=db;Password=must-not-be-serialized")]
    [InlineData("https://example.test/path?token=must-not-be-serialized")]
    public void Serialize_RejectsUnsafeIntegrationEvidence(string target)
    {
        IntegrationFinding finding = IntegrationFinding.Create(
            "src/App/App.csproj",
            IntegrationKind.Http,
            IntegrationDirection.Outbound,
            "httpclient",
            new IntegrationSourceLocation("src/App/Client.cs", 1),
            IntegrationConfidence.Low,
            ["http:client"],
            target: target);
        InspectionReport report = InspectionReport.Create(
            new RepositoryMetadata("sample", null, null, null, null),
            new DotNetSdkMetadata(null, null, null),
            Array.Empty<ProjectInspection>(),
            Array.Empty<InspectionDiagnostic>()) with
        {
            Integrations = [finding]
        };

        Assert.Throws<JsonException>(() => InspectionJsonSerializer.Serialize(report));
    }

    [Fact]
    public void Serialize_RedactsSensitiveDiagnosticContextValues()
    {
        var diagnostic = new InspectionDiagnostic(
            "DRI9000",
            InspectionDiagnosticSeverity.Warning,
            "Security test diagnostic.",
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["accessToken"] = "must-not-be-serialized",
                ["connection_string"] = "Server=example;Password=secret",
                ["project"] = "src/App/App.csproj"
            });
        var report = InspectionReport.Create(
            new RepositoryMetadata("sample", null, null, null, null),
            new DotNetSdkMetadata(null, null, null),
            Array.Empty<ProjectInspection>(),
            [diagnostic]);

        var json = InspectionJsonSerializer.Serialize(report);

        Assert.DoesNotContain("must-not-be-serialized", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=secret", json, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json);
        var context = document.RootElement
            .GetProperty("diagnostics")[0]
            .GetProperty("context");

        Assert.Equal("<redacted>", context.GetProperty("accessToken").GetString());
        Assert.Equal("<redacted>", context.GetProperty("connection_string").GetString());
        Assert.Equal("src/App/App.csproj", context.GetProperty("project").GetString());
    }
}
