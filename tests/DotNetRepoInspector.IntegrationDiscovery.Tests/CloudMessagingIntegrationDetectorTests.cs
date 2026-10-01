using DotNetRepoInspector.Core.Contracts;

using Xunit;

namespace DotNetRepoInspector.IntegrationDiscovery.Tests;

public sealed class CloudMessagingIntegrationDetectorTests
{
    [Fact]
    public async Task DetectsAwsMessagingServicesAndDirections()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync(new AwsMessagingIntegrationDetector());

        AssertFinding(result, "aws-sqs", IntegrationDirection.Publish, "orders", "queue");
        AssertFinding(result, "aws-sqs", IntegrationDirection.Consume, null, "queue", "Messaging:Aws:InputQueue");
        AssertFinding(result, "aws-sns", IntegrationDirection.Publish, "order-events", "topic");
        AssertFinding(result, "aws-eventbridge", IntegrationDirection.Publish, null, "event-bus", "Messaging:Aws:EventBus");
        AssertFinding(result, "aws-kinesis", IntegrationDirection.Publish, "orders-stream", "stream");
        AssertFinding(result, "aws-kinesis", IntegrationDirection.Consume, null, "stream", "Messaging:Aws:InputStream");
    }

    [Fact]
    public async Task DetectsAzureMessagingServicesAndDirections()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync(new AzureMessagingIntegrationDetector());

        AssertFinding(result, "azure-servicebus", IntegrationDirection.Publish, "orders", "queue");
        AssertFinding(result, "azure-servicebus", IntegrationDirection.Consume, null, "queue", "Messaging:Azure:InputQueue");
        AssertFinding(result, "azure-eventhubs", IntegrationDirection.Publish, null, "event-hub", "Messaging:Azure:EventHub");
        AssertFinding(result, "azure-eventhubs", IntegrationDirection.Consume, "incoming-hub", "event-hub");
        AssertFinding(result, "azure-eventgrid", IntegrationDirection.Publish, "orders-topic", "topic");
    }

    [Fact]
    public async Task DetectsGooglePubSubPublishAndConsume()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync(new GooglePubSubIntegrationDetector());

        AssertFinding(result, "google-pubsub", IntegrationDirection.Publish, "orders-topic", "topic");
        AssertFinding(result, "google-pubsub", IntegrationDirection.Consume, null, "subscription", "Messaging:Gcp:Subscription");
    }

    [Fact]
    public async Task IgnoresLookalikesAndNeverSerializesCredentialsOrPayloads()
    {
        IntegrationDiscoveryResult result = await DiscoverAsync(
            new AwsMessagingIntegrationDetector(),
            new AzureMessagingIntegrationDetector(),
            new GooglePubSubIntegrationDetector());
        string json = InspectionJsonSerializer.Serialize(InspectionReport.Create(
            new RepositoryMetadata("fixture", null, null, null, false),
            new DotNetSdkMetadata(null, null, null),
            Array.Empty<ProjectInspection>(),
            result.Diagnostics) with
        {
            Integrations = result.Findings
        });

        Assert.DoesNotContain("CloudMessaging/Negative.cs", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-", json, StringComparison.Ordinal);
        Assert.DoesNotContain("123456789012", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private-project-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.servicebus", json, StringComparison.Ordinal);
        Assert.All(result.Findings, static finding =>
        {
            Assert.Equal(IntegrationKind.Messaging, finding.Kind);
            Assert.Equal("CloudMessaging/CloudMessaging.csproj", finding.ProjectPath);
            Assert.StartsWith("CloudMessaging/", finding.Source.Path, StringComparison.Ordinal);
            Assert.True(finding.Source.Line > 0);
            Assert.NotEmpty(finding.Signals);
        });
    }

    private static void AssertFinding(
        IntegrationDiscoveryResult result,
        string technology,
        string direction,
        string? target,
        string resourceType,
        string? configurationKey = null) =>
        Assert.Contains(result.Findings, finding =>
            finding.Technology == technology &&
            finding.Direction == direction &&
            finding.Target == target &&
            finding.ResourceType == resourceType &&
            finding.ConfigurationKey == configurationKey);

    private static async Task<IntegrationDiscoveryResult> DiscoverAsync(params IIntegrationDetector[] detectors)
    {
        var pipeline = new IntegrationDiscoveryPipeline(detectors);
        return await pipeline.DiscoverAsync(
            new IntegrationDiscoveryRequest(
                FixtureRoot,
                [new IntegrationDiscoveryProject("CloudMessaging/CloudMessaging.csproj")]),
            TestContext.Current.CancellationToken);
    }

    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "IntegrationDiscovery");
}
