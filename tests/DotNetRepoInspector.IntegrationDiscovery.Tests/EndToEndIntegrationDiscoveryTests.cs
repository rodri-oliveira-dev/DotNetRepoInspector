using System.Text;

using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.IntegrationDiscovery.Tests;

public sealed class EndToEndIntegrationDiscoveryTests
{
    [Fact]
    public async Task Catalog_ProducesDeterministicSafeGoldenSnapshotAcrossProjects()
    {
        IntegrationDiscoveryResult first = await DiscoverAsync();
        IntegrationDiscoveryResult second = await DiscoverAsync();
        string firstSnapshot = Snapshot(first);
        string secondSnapshot = Snapshot(second);
        string expected = (await File.ReadAllTextAsync(
            Path.Combine(FixtureRoot, "EndToEnd", "integration-discovery.snapshot.txt"),
            TestContext.Current.CancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

        Assert.Equal(expected, firstSnapshot);
        Assert.Equal(firstSnapshot, secondSnapshot);
        Assert.False(first.Truncated);
        Assert.Empty(first.Diagnostics);
        Assert.Equal(2, first.Findings.Select(static finding => finding.ProjectPath).Distinct().Count());

        string json = InspectionJsonSerializer.Serialize(InspectionReport.Create(
            new RepositoryMetadata("e2e", null, null, null, false),
            new DotNetSdkMetadata(null, null, null),
            Array.Empty<ProjectInspection>(),
            first.Diagnostics) with
        {
            Integrations = first.Findings,
            IntegrationDiscovery = IntegrationDiscoveryMetadata.Complete(first.Truncated)
        });
        foreach (string forbidden in new[]
        {
            "E2E-PRIVATE", "Password=", "Authorization", "Bearer ", "SELECT ",
            "?sig=", "class E2E_PRIVATE_SOURCE_BODY", "123456789012"
        })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(first.Findings, static finding => finding.Source.Path.EndsWith("Negative.cs", StringComparison.Ordinal));
        Assert.Contains(first.Findings, static finding => finding.Technology == "httpclient" && finding.Target == "billing");
        Assert.Contains(first.Findings, static finding => finding.Technology == "refit" && finding.Contract == "IExternalCatalogApi");
        Assert.Contains(first.Findings, static finding => finding.Technology == "google-pubsub");
        Assert.Contains(first.Findings, static finding => finding.Technology == "azure-servicebus");
        Assert.Contains(first.Findings, static finding => finding.Technology == "aws-sqs");
        Assert.Contains(first.Findings, static finding => finding.Technology == "postgresql");
        Assert.Contains(first.Findings, static finding => finding.Technology == "redis");
        Assert.Contains(first.Findings, static finding => finding.Technology == "aws-s3");
        Assert.Contains(first.Findings, static finding => finding.Technology == IntegrationKind.Unknown);
    }

    private static async Task<IntegrationDiscoveryResult> DiscoverAsync() =>
        await new IntegrationDiscoveryPipeline(IntegrationDetectorCatalog.CreateDefault()).DiscoverAsync(
            new IntegrationDiscoveryRequest(
                FixtureRoot,
                [
                    new IntegrationDiscoveryProject("EndToEnd/App/App.csproj"),
                    new IntegrationDiscoveryProject("EndToEnd/Worker/Worker.csproj")
                ]),
            TestContext.Current.CancellationToken);

    private static string Snapshot(IntegrationDiscoveryResult result)
    {
        var builder = new StringBuilder();
        foreach (IntegrationFinding finding in result.Findings)
        {
            builder.Append(finding.Id).Append('|')
                .Append(finding.ProjectPath).Append('|')
                .Append(finding.Kind).Append('|')
                .Append(finding.Direction).Append('|')
                .Append(finding.Technology).Append('|')
                .Append(finding.Target).Append('|')
                .Append(finding.ResourceType).Append('|')
                .Append(finding.ConfigurationKey).Append('|')
                .Append(finding.Contract).Append('|')
                .Append(finding.Source.Path).Append(':').Append(finding.Source.Line).Append('|')
                .Append(finding.Confidence).Append('|')
                .AppendJoin(',', finding.Signals)
                .AppendLine();
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    }

    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "IntegrationDiscovery");
}
