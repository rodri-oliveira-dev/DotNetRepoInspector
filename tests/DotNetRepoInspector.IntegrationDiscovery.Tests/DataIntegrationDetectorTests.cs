using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.IntegrationDiscovery.Tests;

public sealed class DataIntegrationDetectorTests
{
    [Fact]
    public async Task DetectsRelationalProvidersWithoutConnectionStringValues()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync(new RelationalDatabaseIntegrationDetector());

        Assert.Contains(result.Findings, finding => finding.Technology == "postgresql" && finding.Target == "LoanDb" && finding.ConfigurationKey == "ConnectionStrings:LoanDb");
        Assert.Contains(result.Findings, finding => finding.Technology == "sqlserver" && finding.Direction == IntegrationDirection.Read);
        Assert.Contains(result.Findings, finding => finding.Technology == "mysql" && finding.Target == "OrdersMySql");
        Assert.Contains(result.Findings, finding => finding.Technology == "oracle" && finding.Target == "LedgerOracle");
        Assert.Contains(result.Findings, finding => finding.Technology == "mysql" && finding.Direction == IntegrationDirection.Write);
        Assert.Contains(result.Findings, finding => finding.Technology == "oracle" && finding.Direction == IntegrationDirection.Read);
    }

    [Fact]
    public async Task DetectsRedisMongoCosmosAndDynamoDb()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync(new CacheAndNoSqlIntegrationDetector());

        AssertFinding(result, IntegrationKind.Cache, "redis", IntegrationDirection.Bidirectional, "LoanCache", "cache", "Redis:Connection");
        AssertFinding(result, IntegrationKind.Database, "mongodb", IntegrationDirection.Bidirectional, "loans", "database");
        AssertFinding(result, IntegrationKind.Database, "mongodb", IntegrationDirection.Read, "applications", "collection");
        AssertFinding(result, IntegrationKind.Database, "azure-cosmosdb", IntegrationDirection.Bidirectional, "loan-items", "container");
        AssertFinding(result, IntegrationKind.Database, "aws-dynamodb", IntegrationDirection.Read, null, "table", "Dynamo:Table");
        AssertFinding(result, IntegrationKind.Database, "aws-dynamodb", IntegrationDirection.Write, "loan-events", "table");
    }

    [Fact]
    public async Task DetectsCloudDataAndObjectStorage()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync(new CloudStorageIntegrationDetector());

        AssertFinding(result, IntegrationKind.Storage, "aws-s3", IntegrationDirection.Write, "loan-documents", "bucket");
        AssertFinding(result, IntegrationKind.Storage, "aws-s3", IntegrationDirection.Read, null, "bucket", "Storage:S3:Bucket");
        AssertFinding(result, IntegrationKind.Storage, "google-cloud-storage", IntegrationDirection.Write, null, "bucket", "Storage:Gcs:Bucket");
        AssertFinding(result, IntegrationKind.Storage, "google-cloud-storage", IntegrationDirection.Read, "loan-archive", "bucket");
        AssertFinding(result, IntegrationKind.Storage, "azure-blob-storage", IntegrationDirection.Bidirectional, null, "container", "Storage:Azure:Container");
        AssertFinding(result, IntegrationKind.Database, "google-bigquery", IntegrationDirection.Read, "lending", "dataset");
        AssertFinding(result, IntegrationKind.Database, "google-bigquery", IntegrationDirection.Read, "applications", "table");
        AssertFinding(result, IntegrationKind.Database, "google-bigquery", IntegrationDirection.Unknown, null, "project", "BigQuery:Project");
    }

    [Fact]
    public async Task ResolvesReceiverTypesWithinTheirLexicalScopes()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync(new CloudStorageIntegrationDetector());

        Assert.Contains(result.Findings, finding =>
            finding.Technology == "aws-s3" && finding.Target == "scoped-real");
        Assert.DoesNotContain(result.Findings, finding => finding.Target == "scoped-lookalike");
    }

    [Fact]
    public async Task IsDeterministicIgnoresLookalikesAndDoesNotSerializeSensitiveValues()
    {
        IIntegrationDetector[] detectors =
        [
            new RelationalDatabaseIntegrationDetector(),
            new CacheAndNoSqlIntegrationDetector(),
            new CloudStorageIntegrationDetector()
        ];
        IntegrationDiscoveryResult first = await DiscoverAsync(detectors);
        IntegrationDiscoveryResult second = await DiscoverAsync(detectors);
        string firstJson = Serialize(first);
        string secondJson = Serialize(second);

        Assert.Equal(firstJson, secondJson);
        Assert.DoesNotContain("DataDependencies/Negative.cs", firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-", firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE SQL", firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=", firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", firstJson, StringComparison.Ordinal);
        Assert.All(first.Findings, static finding =>
        {
            Assert.Contains(finding.Kind, new[] { IntegrationKind.Database, IntegrationKind.Cache, IntegrationKind.Storage });
            Assert.Equal("DataDependencies/DataDependencies.csproj", finding.ProjectPath);
            Assert.StartsWith("DataDependencies/", finding.Source.Path, StringComparison.Ordinal);
            Assert.True(finding.Source.Line > 0);
        });
    }

    private static void AssertFinding(
        IntegrationDiscoveryResult result,
        string kind,
        string technology,
        string direction,
        string? target,
        string resourceType,
        string? configurationKey = null) =>
        Assert.Contains(result.Findings, finding =>
            finding.Kind == kind && finding.Technology == technology && finding.Direction == direction &&
            finding.Target == target && finding.ResourceType == resourceType && finding.ConfigurationKey == configurationKey);

    private static async Task<IntegrationDiscoveryResult> DiscoverAsync(params IIntegrationDetector[] detectors) =>
        await new IntegrationDiscoveryPipeline(detectors).DiscoverAsync(
            new IntegrationDiscoveryRequest(
                FixtureRoot,
                [new IntegrationDiscoveryProject("DataDependencies/DataDependencies.csproj")]),
            TestContext.Current.CancellationToken);

    private static string Serialize(IntegrationDiscoveryResult result) =>
        InspectionJsonSerializer.Serialize(InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, false),
            new DotNetSdkMetadata(null, null, null),
            Array.Empty<ProjectInspection>(),
            result.Diagnostics) with
        {
            Integrations = result.Findings
        });

    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "IntegrationDiscovery");
}
